using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Flow.Launcher.Plugin.VisualStudio.Models;

namespace Flow.Launcher.Plugin.VisualStudio
{
    public class VisualStudioPlugin
    {
        private readonly PluginInitContext context;
        private readonly Settings settings;
        private readonly IconProvider iconProvider;
        private readonly ConcurrentDictionary<string, EntryResult> recentEntries = new();
        private readonly ConcurrentBag<VisualStudioInstance> vsInstances = new();
        private bool doneBackupToday;
        private bool validVswherePath;

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

        private IEnumerable<Entry> Entries => recentEntries.Select(x => x.Value.Entry);
        public IEnumerable<EntryResult> EntryResults => recentEntries.Select(x => x.Value);
        public IEnumerable<VisualStudioInstance> VSInstances => vsInstances;
        public bool ValidVswherePath => validVswherePath;
        public bool IsVSInstalled => !vsInstances.IsEmpty;

        public async Task GetVisualStudioInstances()
        {

            if (!File.Exists(settings.VswherePath))
            {
                validVswherePath = false;
                return;
            }

            validVswherePath = true;

            Process vswhere;
            try
            {
                vswhere = Process.Start(new ProcessStartInfo
                {
                    FileName = settings.VswherePath,
                    Arguments = "-sort -format json -utf8 -prerelease",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
                context.API.LogException(nameof(VisualStudioPlugin), "Failed to start vswhere.exe", ex);
                validVswherePath = false;
                return;
            }

            var doc = await JsonDocument.ParseAsync(vswhere.StandardOutput.BaseStream);

            vsInstances.Clear();

            int count;
            if (doc.RootElement.ValueKind != JsonValueKind.Array || (count = doc.RootElement.GetArrayLength()) < 1)
                return;

            Parallel.For(0, count, index => vsInstances.Add(new VisualStudioInstance(doc.RootElement[index])));
            
            doc?.Dispose();
            vswhere?.Dispose();

        }
        
        public async Task GetRecentEntries(CancellationToken token = default)
        {
            var newestVS = VSInstances.MaxBy(vs => File.GetLastWriteTimeUtc(vs.RecentItemsPath));
            recentEntries.Clear();

            Entry[] entries = await GetRecentEntriesFromInstance(newestVS, token);
            
            await GetEntryResults(entries, token);

            if (settings.AutoUpdateBackup
                && !doneBackupToday
                && (DateTime.UtcNow.Date - settings.LastBackup.Date).Days > 0)
            {
                doneBackupToday = true;
                UpdateBackup();
            }
        }
        
        private static async Task<Entry[]> GetRecentEntriesFromInstance(VisualStudioInstance vs, CancellationToken cancellationToken = default)
        {
            await using var fileStream = new FileStream(vs.RecentItemsPath, FileMode.Open, FileAccess.Read);
            using var reader = XmlReader.Create(fileStream, new XmlReaderSettings { Async = true });
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

        private async Task GetEntryResults(Entry[] entries, CancellationToken token = default)
        {
            await Parallel.ForEachAsync(entries, token, async (entry, ct) =>
            {
                EntryResult entryResult = recentEntries.GetOrAdd(entry.Key, static (_, e) => new EntryResult { Entry = e }, entry);
                // if (settings.DisplayGitBranch) //TODO: 
                // {
                //     
                // }
                entryResult.Entry = entry;
                var path = entryResult.EntryType switch
                {
                    EntryType.ProjectOrSolution => Path.GetDirectoryName(entryResult.Path),
                    EntryType.FileOrFolder => entryResult.Path,
                    _ => string.Empty,
                };
                entryResult.GitBranch = await GetGitBranch(path, ct);
            });
        }

        private static async Task<string> GetGitBranch(string path, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.Exists(Path.Combine(path, ".git")))
            {
                return EntryResult.NoGit;
            }

            try
            {
                using Process process = Process.Start(new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse --abbrev-ref HEAD",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = path,
                    RedirectStandardOutput = true,
                    
                });
                string branch = await process.StandardOutput.ReadToEndAsync(token);
                return string.IsNullOrWhiteSpace(branch) ? EntryResult.NoGit : branch.TrimEnd();
            }
            catch (Win32Exception)
            {
                return EntryResult.NoGit;
            }
        }

        public async Task RemoveAllEntries() => await RemoveEntries(false);

        public async Task RemoveInvalidEntries() => await RemoveEntries(true);

        public async Task RemoveEntry(EntryResult entryToRemove)
        {
            if (recentEntries.TryRemove(entryToRemove.Id, out _))
            {
                await UpdateDiskRecentEntries();
                context.API.ShowMsg($"Visual Studio Plugin", $"Removed \"{entryToRemove.Id}\" from the recent items list", iconProvider.Notification);
            }
        }
        
        private async Task RemoveEntries(bool missingOnly)
        {
            IEnumerable<EntryResult> entriesToRemove = recentEntries.Values; //In moment snapshot
            if (missingOnly)
            {
                entriesToRemove = entriesToRemove.Where(entryResult => !Path.Exists(entryResult.Path));
            }

            bool removed = false;
            foreach (var entryResult in entriesToRemove)
            {
                removed |= recentEntries.TryRemove(entryResult.Id, out _);
            }

            if (removed)
            {
                await UpdateDiskRecentEntries();
            }

            int count = entriesToRemove.Count();
            context.API.ShowMsg("Visual Studio Plugin", $"Removed {(missingOnly ? string.Empty : "all")} {count} entr{(count != 1 ? "ies" : "y")}", iconProvider.Notification);
        }

        private async Task UpdateDiskRecentEntries()
        {
            await Parallel.ForEachAsync(VSInstances, async (vs, ct) =>
            {
                using var memoryStream = new MemoryStream();
                
                //Convert entries to json
                Task json = JsonSerializer.SerializeAsync(memoryStream, Entries.ToArray(), cancellationToken: ct);
                
                //Open xml document
                await using var fileStream = new FileStream(vs.RecentItemsPath, FileMode.Open, FileAccess.ReadWrite);
                XDocument root = await XDocument.LoadAsync(fileStream, LoadOptions.None, ct);
                XElement recent = root.Element("content")
                                      .Element("indexed")
                                      .Elements("collection")
                                      .First(e => (string)e.Attribute("name") == "CodeContainers.Offline")
                                      .Element("value");
                //Make sure Json is serialized
                await json;

                //write new entries to xml value
                memoryStream.Position = 0;
                using var streamReader = new StreamReader(memoryStream, Encoding.UTF8);
                recent.Value = await streamReader.ReadToEndAsync(ct);

                //save file
                fileStream.SetLength(0);
                await using var streamWriter = new StreamWriter(fileStream, Encoding.UTF8);
                await root.SaveAsync(streamWriter, SaveOptions.DisableFormatting, ct);
            });
        }
        
        public async Task RevertToBackup()
        {
            recentEntries.Clear();
            await GetEntryResults(settings.EntriesBackup);
            await UpdateDiskRecentEntries();
            context.API.ShowMsg("Visual Studio Plugin", $"Restored {recentEntries.Count} entr{(recentEntries.Count != 1 ? "ies" : "y")} from {settings.LastBackup} backup.", iconProvider.Notification);
        }
        
        public void UpdateBackup()
        {
            settings.LastBackup = DateTime.UtcNow;
            settings.EntriesBackup = Entries.ToArray();
            context.API.SaveSettingJsonStorage<Settings>();
        }
    }
}
