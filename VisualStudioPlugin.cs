using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Flow.Launcher.Plugin.VisualStudio
{
    public class VisualStudioPlugin
    {
        private readonly PluginInitContext context;
        private readonly Settings settings;
        private readonly ConcurrentDictionary<string, Entry> recentEntries;
        private bool doneBackupToday = false;

        private VisualStudioInstance[] vsInstances;

        public static async Task<VisualStudioPlugin> Create(Settings settings, PluginInitContext context)
        {
            var plugin = new VisualStudioPlugin(settings, context);
            await plugin.GetVisualStudioInstances();
            return plugin;
        }
        private VisualStudioPlugin(Settings settings, PluginInitContext context)
        {
            this.context = context;
            this.settings = settings;
            recentEntries = new ConcurrentDictionary<string, Entry>();
        }
        public bool IsVSInstalled => vsInstances.Any();
        public IEnumerable<Entry> RecentEntries => recentEntries.Select(kvp => kvp.Value);
        public IList<VisualStudioInstance> VSInstances => vsInstances;

        public async Task GetVisualStudioInstances()
        {
            using var vswhereProcess = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio\\Installer\\vswhere.exe"),
                Arguments = "-sort -format json",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
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
            if (!doneBackupToday && (DateTime.UtcNow.Date - settings.LastBackup.Date).Days > 0)
            {
                doneBackupToday = true;
                settings.LastBackup = DateTime.UtcNow.Date;
                settings.EntriesBackup = RecentEntries.ToArray();
                context.API.SaveSettingJsonStorage<Settings>();
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

            //var entries = AsyncEnumerable.Empty<Entry>();
            string json = await reader.GetValueAsync();

            if (string.IsNullOrWhiteSpace(json))
            {
                //await foreach (var entry in entries)
                //{
                //    yield return entry;
                //}
                yield break;
            }

            using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var entries = JsonSerializer.DeserializeAsyncEnumerable<Entry>(memoryStream, cancellationToken: cancellationToken);

            await foreach (var entry in entries)
            {
                yield return entry;
            }
        }
        
        public async Task RevertToBackup()
        {
            recentEntries.Clear();
            foreach (var entry in settings.EntriesBackup)
            {
                recentEntries.TryAdd(entry.Key, entry);
            }

            await UpdateVisualStudioInstances();
            context.API.ShowMsg($"Backup {settings.LastBackup.ToShortDateString()} Restored",$"Restored {recentEntries.Count} entr{(recentEntries.Count != 1 ? "ies" : "y")}." );
        }

        public async Task RemoveAllEntries()
        {
            await RemoveEntries(false);
        }
        public async Task RemoveInvalidEntries()
        {
            await RemoveEntries(true);
        }
        public async Task RemoveEntry(Entry entryToRemove)
        {
            if(recentEntries.TryRemove(entryToRemove.Key, out _))
            {
                await UpdateVisualStudioInstances();
                context.API.ShowMsg($"Removed Recent Item", $"Removed \"{entryToRemove.Key}\" from recent items list");
            }

        }
        private async Task RemoveEntries(bool missingOnly)
        {
            IEnumerable<Entry> entriesToRemove = recentEntries.Values;
            if (missingOnly)
            {
                entriesToRemove = entriesToRemove.Where(entry => !File.Exists(entry.Path) && !Directory.Exists(entry.Path));
            }

            bool removed = false;
            foreach (var entry in entriesToRemove)
            {
                removed |= recentEntries.TryRemove(entry.Key, out _);
            }

            if(removed)
            {
                await UpdateVisualStudioInstances();
            }

            int count = entriesToRemove.Count();
            context.API.ShowMsg("Visual Studio Plugin", $"Removed {(missingOnly ? string.Empty : "all")} {count} entr{(count != 1 ? "ies" : "y")}");
        }

        private async Task UpdateVisualStudioInstances()
        {
            await Parallel.ForEachAsync(VSInstances, async (vs, ct) =>
            {
                using var memoryStream = new MemoryStream();

                var json = JsonSerializer.SerializeAsync(memoryStream, RecentEntries.ToArray(), cancellationToken: ct);
                ///Open xml document
                using var fileStream = new FileStream(vs.RecentItemsPath, FileMode.Open, FileAccess.ReadWrite);
                var root = await XDocument.LoadAsync(fileStream, LoadOptions.None, ct);
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
                recent.Value = await streamReader.ReadToEndAsync(ct);

                //save file
                fileStream.SetLength(0);
                using var streamWriter = new StreamWriter(fileStream, Encoding.UTF8);
                await root.SaveAsync(streamWriter, SaveOptions.DisableFormatting, ct);
            });
        }
    }
}
