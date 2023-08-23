using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private readonly IconProvider iconProvider;
        private readonly ConcurrentDictionary<string, Entry> recentEntries = new();
        private readonly ConcurrentBag<VisualStudioInstance> vsInstances = new();
        private bool doneBackupToday = false;

        public static async Task<VisualStudioPlugin> Create(Settings settings, PluginInitContext context, IconProvider iconProvider)
        {
            var plugin = new VisualStudioPlugin(settings, context, iconProvider);
            await plugin.GetVisualStudioInstances();
            return plugin;
        }
        private VisualStudioPlugin(Settings settings, PluginInitContext context, IconProvider iconProvider)
        {
            this.context = context;
            this.settings = settings;
            this.iconProvider = iconProvider;
        }

        public bool IsVSInstalled => vsInstances.Any();
        public IEnumerable<Entry> RecentEntries => recentEntries.Select(kvp => kvp.Value);
        public IEnumerable<VisualStudioInstance> VSInstances => vsInstances;

        public async Task GetVisualStudioInstances()
        {
            using var vswhere = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio\\Installer\\vswhere.exe"),
                Arguments = "-sort -format json",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });


            using var doc = await JsonDocument.ParseAsync(vswhere.StandardOutput.BaseStream);

            vsInstances.Clear();

            int count;
            if (doc.RootElement.ValueKind != JsonValueKind.Array || (count = doc.RootElement.GetArrayLength()) < 1)
                return;

            Parallel.For(0,count, index => vsInstances.Add(new VisualStudioInstance(doc.RootElement[index])));

        }
        public async Task GetRecentEntries(CancellationToken token = default)
        {
            var newestVS = VSInstances.MaxBy(vs => File.GetLastWriteTimeUtc(vs.RecentItemsPath));
            recentEntries.Clear();

            var entries = await GetRecentEntriesFromInstance(newestVS, token);

            Parallel.ForEach(entries, entry => recentEntries.TryAdd(entry.Key, entry));

            if (settings.AutoUpdateBackup
                && !doneBackupToday
                && (DateTime.UtcNow.Date - settings.LastBackup.Date).Days > 0)
            {
                doneBackupToday = true;
                UpdateBackup();
            }
        }

        public void UpdateBackup()
        {
            settings.LastBackup = DateTime.UtcNow;
            settings.EntriesBackup = RecentEntries.ToArray();
            context.API.SaveSettingJsonStorage<Settings>();
        }

        private static async Task<Entry[]> GetRecentEntriesFromInstance(VisualStudioInstance vs, CancellationToken cancellationToken = default)
        {
            using var fileStream = new FileStream(vs.RecentItemsPath, FileMode.Open, FileAccess.Read);
            using var reader = XmlReader.Create(fileStream, new XmlReaderSettings() { Async = true });
            await reader.MoveToContentAsync();

            var correctElement = false;
            while (await reader.ReadAsync())
            {
                if (correctElement
                    && reader.NodeType == XmlNodeType.Element
                    && reader.HasAttributes
                    && reader.GetAttribute(0) == "value")
                {
                    await reader.ReadAsync();
                    break;
                }
                if (reader.NodeType == XmlNodeType.Element
                    && reader.HasAttributes
                    && reader.GetAttribute(0) == "CodeContainers.Offline")
                {
                    correctElement = true;
                }
            }

            var json = await reader.GetValueAsync();
            
            try
            {
                using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
                return await JsonSerializer.DeserializeAsync<Entry[]>(memoryStream, cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is JsonException || ex is ArgumentNullException)
            {
                return Array.Empty<Entry>();
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
            context.API.ShowMsg("Visual Studio Plugin", $"Restored {recentEntries.Count} entr{(recentEntries.Count != 1 ? "ies" : "y")} from {settings.LastBackup} backup.",iconProvider.Notification);
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
            if (recentEntries.TryRemove(entryToRemove.Key, out _))
            {
                await UpdateVisualStudioInstances();
                context.API.ShowMsg($"Visual Studio Plugin", $"Removed \"{entryToRemove.Key}\" from the recent items list",iconProvider.Notification);
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

            if (removed)
            {
                await UpdateVisualStudioInstances();
            }

            int count = entriesToRemove.Count();
            context.API.ShowMsg("Visual Studio Plugin", $"Removed {(missingOnly ? string.Empty : "all")} {count} entr{(count != 1 ? "ies" : "y")}", iconProvider.Notification);
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
