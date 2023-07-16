using Flow.Launcher.Plugin.VisualStudio.UI;
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
using System.Windows.Controls;
using System.Xml;
using System.Xml.Linq;

namespace Flow.Launcher.Plugin.VisualStudio
{
    public class VisualStudioPlugin : IAsyncPlugin, IContextMenu, ISettingProvider, IAsyncReloadable
    {
        private PluginInitContext context;
        private IList<VisualStudioInstance> vsInstances;
        private bool IsVSInstalled;

        private static readonly TypeKeyword ProjectsOnly = new(0, "p:");
        private static readonly TypeKeyword FilesOnly = new(1, "f:");

        private Settings settings;
        //TODO: change icon of all results based on settings of DefaultVSId;
        private ConcurrentDictionary<string, Entry> recentEntries;
        private IEnumerable<Entry> RecentEntries => recentEntries.Select(kvp => kvp.Value);
        public IList<VisualStudioInstance> VSInstances => vsInstances;

        private bool startedBackup = false;
        
        public async Task InitAsync(PluginInitContext context)
        {
            this.context = context;
            settings = context.API.LoadSettingJsonStorage<Settings>();
            context.API.VisibilityChanged += OnVisibilityChanged;

            Icons.Init(context);
            await ReloadDataAsync();
            recentEntries = new ConcurrentDictionary<string, Entry>();
        }

        private void OnVisibilityChanged(object sender, VisibilityChangedEventArgs args)
        {
            if (context.CurrentPluginMetadata.Disabled)
                return;

            if (args.IsVisible)
            {
                Task.Run(async () => await GetRecentEntries());
            }
        }

        public async Task ReloadDataAsync()
        {
            Icons.Reload();
            vsInstances = await GetVisualStudioInstances(new CancellationTokenSource());
            IsVSInstalled = VSInstances.Any();
        }

        public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return null;
            }

            if (!IsVSInstalled)
            {
                return SingleResult("No installed version of Visual Studio was found");
            }

            if(query.IsReQuery)
            {
                await GetRecentEntries(token);
            }    

            if (!recentEntries.Any())
            {
                return SingleResult("No recent items found");
            }

            var selectedRecentItems = query.Search switch
            {
                string search when string.IsNullOrEmpty(search) => RecentEntries,
                string search when search.StartsWith(ProjectsOnly.Keyword) => RecentEntries.Where(e => TypeSearch(e, query, ProjectsOnly)),
                string search when search.StartsWith(FilesOnly.Keyword) => RecentEntries.Where(e => TypeSearch(e, query, FilesOnly)),
                _ => RecentEntries.Where(e => FuzzySearch(e, query.Search))
            };

            return selectedRecentItems.OrderBy(e => e.Value.LastAccessed).Select(CreateEntryResult).ToList();
        }
        public List<Result> LoadContextMenus(Result selectedResult)
        {
            if (selectedResult.ContextData is Entry currentEntry)
            {
                return VSInstances.Select(instance => new Result
                {
                    Title = $"Open in \"{instance.DisplayName}\" [{instance.DisplayVersion}]",
                    SubTitle = instance.Description,
                    IcoPath = instance.IconPath,
                    Action = c =>
                    {
                        context.API.ShellRun($"\"{currentEntry.Path}\"", $"\"{instance.ExePath}\"");
                        return true;
                    }
                }).Append(new Result
                {
                    Title = $"Remove \"{selectedResult.Title}\" from all visual studio recent items lists.",
                    SubTitle = selectedResult.SubTitle,
                    IcoPath = Icons.Remove,
                    AsyncAction = async c =>
                    {
                        await RemoveEntries(currentEntry);
                        await Task.Delay(100);

                        context.API.ChangeQuery(context.CurrentPluginMetadata.ActionKeyword, true);
                        return false;
                    }
                }).ToList();
            }
            return null;
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

        private static async Task<VisualStudioInstance[]> GetVisualStudioInstances(CancellationTokenSource ctSource)
        {
            var vswhereProcess = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio\\Installer\\vswhere.exe"),
                Arguments = "-sort -format json",
                RedirectStandardOutput = true,
            });

            using var doc = await JsonDocument.ParseAsync(vswhereProcess.StandardOutput.BaseStream, cancellationToken: ctSource.Token);

            int count;
            if (doc.RootElement.ValueKind != JsonValueKind.Array || (count = doc.RootElement.GetArrayLength()) < 1)
            {
                ctSource.Cancel();
                return Array.Empty<VisualStudioInstance>();
            }

            var tasks = new List<Task<VisualStudioInstance>>();
            for (int i = 0; i < count; i++)
            {
                tasks.Add(VisualStudioInstance.Create(doc.RootElement[i], ctSource.Token));
            }

            return await Task.WhenAll(tasks);
        }

        private async Task GetRecentEntries(CancellationToken token = default)
        {
            var newestVS = VSInstances.MaxBy(vs => File.GetLastWriteTimeUtc(vs.RecentItemsPath));
            recentEntries.Clear();

            await foreach (var entry in GetRecentEntriesFromInstance(newestVS, token))
            {
                recentEntries.TryAdd(entry.Key,entry);
            } 

            if(!startedBackup)
            {
                startedBackup = true;
                settings.Backup(context, recentEntries.Values.ToArray());
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

        private static List<Result> SingleResult(string title)
        {
            return new List<Result>
            {
                new Result
                {
                    Title = title,
                    IcoPath = Icons.DefaultIcon,
                }
            };
        }
        private Result CreateEntryResult(Entry e)
        {
            return new Result
            {
                Title = Path.GetFileNameWithoutExtension(e.Path),
                TitleHighlightData = e.HighlightData,
                SubTitle = e.Value.IsFavorite ? $"★  {e.Path}" : e.Path,
                SubTitleToolTip = $"{e.Path}\n\nLast Accessed:\t{e.Value.LastAccessed:F}",
                ContextData = e,
                IcoPath = Icons.DefaultIcon,//TODO: Change
                Action = c =>
                {
                    if (!string.IsNullOrWhiteSpace(settings.DefaultVSId))
                    {
                        var instance = VSInstances.FirstOrDefault(i => i.InstanceId == settings.DefaultVSId);
                        if (instance != null)
                        {
                            context.API.ShellRun($"\"{e.Path}\"", $"\"{instance.ExePath}\"");
                            return true;
                        }
                    }
                    context.API.ShellRun($"\"{e.Path}\"");
                    return true;
                }
            };
        }

        private bool FuzzySearch(Entry entry, string search)
        {
            var matchResult = context.API.FuzzySearch(search, Path.GetFileNameWithoutExtension(entry.Path));
            entry.HighlightData = matchResult.MatchData;
            return matchResult.IsSearchPrecisionScoreMet();
        }
        private bool TypeSearch(Entry entry, Query query, TypeKeyword typeKeyword)
        {
            var search = query.Search[typeKeyword.Keyword.Length..];
            if (string.IsNullOrWhiteSpace(search))
                return entry.ItemType == typeKeyword.Type;
            else
                return entry.ItemType == typeKeyword.Type && FuzzySearch(entry, search);
        }
        public Control CreateSettingPanel()
        {
            return new SettingsView(new SettingsViewModel(settings, this));
        }



        public record struct TypeKeyword(int Type, string Keyword);
    }
}