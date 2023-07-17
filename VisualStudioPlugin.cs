using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml;
using System.Collections.Concurrent;

namespace Flow.Launcher.Plugin.VisualStudio
{
    public class VisualStudioPlugin
    {
        private VisualStudioInstance[] vsInstances;
        private readonly ConcurrentDictionary<string, Entry> recentEntries;

        public static async Task<VisualStudioPlugin> Create()
        {
            var plugin = new VisualStudioPlugin();
            await plugin.GetVisualStudioInstances();
            return plugin;
        }
        private VisualStudioPlugin()
        {
            recentEntries = new ConcurrentDictionary<string, Entry>();
        }
        public bool IsVSInstalled => vsInstances.Any();
        public IEnumerable<Entry> RecentEntries => recentEntries.Select(kvp => kvp.Value);
        public IList<VisualStudioInstance> VSInstances => vsInstances;

        public async Task GetVisualStudioInstances()
        {
            var vswhereProcess = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio\\Installer\\vswhere.exe"),
                Arguments = "-sort -format json",
                RedirectStandardOutput = true,
            });

            using var doc = await JsonDocument.ParseAsync(vswhereProcess.StandardOutput.BaseStream);

            int count;
            if (doc.RootElement.ValueKind != JsonValueKind.Array || (count = doc.RootElement.GetArrayLength()) < 1)
            {
                vsInstances = Array.Empty<VisualStudioInstance>();
                return;
            }

            var tasks = new List<Task<VisualStudioInstance>>();
            for (int i = 0; i < count; i++)
            {
                int index = i;
                tasks.Add(Task.Run(() => new VisualStudioInstance(doc.RootElement[index])));
            }

            vsInstances = await Task.WhenAll(tasks);
        }
        public async Task GetRecentEntries(CancellationToken token = default)
        {
            var newestVS = VSInstances.MaxBy(vs => File.GetLastWriteTimeUtc(vs.RecentItemsPath));
            recentEntries.Clear();

            await foreach (var entry in GetRecentEntriesFromInstance(newestVS, token))
            {
                recentEntries.TryAdd(entry.Key, entry);
            }
        }
        private static async IAsyncEnumerable<Entry> GetRecentEntriesFromInstance(VisualStudioInstance vs, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var fileStream = new FileStream(vs.RecentItemsPath, FileMode.Open, FileAccess.Read);
            using var reader = XmlReader.Create(fileStream, new XmlReaderSettings() { Async = true });
            await reader.MoveToContentAsync();

            bool correctElement = false;
            while (await reader.ReadAsync())
            {
                if (correctElement && reader.NodeType == XmlNodeType.Element && reader.HasAttributes && reader.GetAttribute(0) == "value")
                {
                    await reader.ReadAsync();
                    break;
                }
                if (reader.NodeType == XmlNodeType.Element && reader.HasAttributes && reader.GetAttribute(0) == "CodeContainers.Offline")
                {
                    correctElement = true;
                }
            }

            var entries = AsyncEnumerable.Empty<Entry>();
            string json = await reader.GetValueAsync();

            if (string.IsNullOrWhiteSpace(json))
            {
                await foreach (var entry in entries)
                {
                    yield return entry;
                }
                yield break;
            }

            using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            entries = JsonSerializer.DeserializeAsyncEnumerable<Entry>(memoryStream, cancellationToken: cancellationToken);

            await foreach (var entry in entries)
            {
                yield return entry;
            }
        }
        public async Task RemoveEntries(params Entry[] entriesToRemove)
        {
            foreach (var entry in entriesToRemove)
            {
                recentEntries.TryRemove(entry.Key, out _);
            }
            await Parallel.ForEachAsync(VSInstances, async (instance, ct) =>
            {
                await UpdateRecentItems(instance, ct);
            });
        }
        private async Task UpdateRecentItems(VisualStudioInstance vs, CancellationToken cancellationToken = default)
        {
            if (!RecentEntries.Any())
                return;

            using var memoryStream = new MemoryStream();

            var json = JsonSerializer.SerializeAsync(memoryStream, RecentEntries.ToArray(), cancellationToken: cancellationToken);
            ///Open xml document
            using (var fileStream = new FileStream(vs.RecentItemsPath, FileMode.Open, FileAccess.ReadWrite))
            {
                var root = await XDocument.LoadAsync(fileStream, LoadOptions.None, cancellationToken);
                var recent = root.Element("content")
                                 .Element("indexed")
                                 .Elements("collection")
                                 .Where(e => (string)e.Attribute("name") == "CodeContainers.Offline")
                                 .First()
                                 .Element("value");
                ///Make sure Json is serialized
                await json;

                //write new entries to xml value
                memoryStream.Position = 0;
                using var streamReader = new StreamReader(memoryStream, Encoding.UTF8);
                recent.Value = await streamReader.ReadToEndAsync(cancellationToken);

                //save file
                fileStream.SetLength(0);
                using var streamWriter = new StreamWriter(fileStream, Encoding.UTF8);
                await root.SaveAsync(streamWriter, SaveOptions.DisableFormatting, cancellationToken);
            }

        }
    }
}
