using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin.VisualStudio
{
    public class Main : IAsyncPlugin, IContextMenu, ISettingProvider, IAsyncReloadable
    {
        private PluginInitContext context;
        private VisualStudioPlugin plugin;

        private static readonly TypeKeyword ProjectsOnly = new(0, "p:");
        private static readonly TypeKeyword FilesOnly = new(1, "f:");

        private Settings settings;
        private IconProvider iconProvider;
        private Dictionary<Entry, List<int>> entryHighlightData;

        public async Task InitAsync(PluginInitContext context)
        {
            this.context = context;
            settings = context.API.LoadSettingJsonStorage<Settings>();
            context.API.VisibilityChanged += OnVisibilityChanged;

            iconProvider = new IconProvider(context);
            plugin = await VisualStudioPlugin.Create(settings, context, iconProvider);
            entryHighlightData = new Dictionary<Entry, List<int>>();
        }

        private void OnVisibilityChanged(object sender, VisibilityChangedEventArgs args)
        {
            if (context.CurrentPluginMetadata.Disabled)
                return;

            if (args.IsVisible)
            {
                Task.Run(async () => await plugin.GetRecentEntries());
            }
        }

        public async Task ReloadDataAsync()
        {
            await plugin.GetVisualStudioInstances();
            iconProvider.ReloadIcons();
        }

        public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return null;
            }

            if (!plugin.IsVSInstalled)
            {
                return SingleResult("No installed version of Visual Studio was found");
            }

            if(query.IsReQuery)
            {
                await plugin.GetRecentEntries(token);
            }    

            if (!plugin.RecentEntries.Any())
            {
                return SingleResult("No recent items found");
            }

            entryHighlightData.Clear();
            var selectedRecentItems = query.Search switch
            {
                string search when string.IsNullOrEmpty(search) => plugin.RecentEntries,
                string search when search.StartsWith(ProjectsOnly.Keyword) => plugin.RecentEntries.Where(e => TypeSearch(e, query, ProjectsOnly)),
                string search when search.StartsWith(FilesOnly.Keyword) => plugin.RecentEntries.Where(e => TypeSearch(e, query, FilesOnly)),
                _ => plugin.RecentEntries.Where(e => FuzzySearch(e, query.Search))
            };

            return selectedRecentItems.OrderBy(e => e.Value.LastAccessed)
                                      .Select(CreateEntryResult)
                                      .ToList();
        }
        public List<Result> LoadContextMenus(Result selectedResult)
        {
            if (selectedResult.ContextData is Entry currentEntry)
            {
                return plugin.VSInstances.Select(vs =>
                {
                    return new Result
                    {
                        Title = $"Open in \"{vs.DisplayName}\" [Version: {vs.DisplayVersion}]",
                        SubTitle = vs.ExePath,
                        IcoPath = iconProvider.GetIconPath(vs),
                        Action = _ =>
                        {
                            context.API.ShellRun($"\"{currentEntry.Path}\"", $"\"{vs.ExePath}\"");
                            return true;
                        }
                    };
                }).Append(new Result
                {
                    Title = $"Remove \"{selectedResult.Title}\" from recent items list.",
                    SubTitle = selectedResult.SubTitle,
                    IcoPath = IconProvider.Remove,
                    AsyncAction = async _ =>
                    {
                        await plugin.RemoveEntry(currentEntry);
                        await Task.Delay(100);
                        
                        context.API.ChangeQuery(context.CurrentPluginMetadata.ActionKeyword);
                        return true;
                    }
                }).Append(new Result
                {
                    Title = $"Open in File Explorer",
                    SubTitle = currentEntry.Path,
                    IcoPath = IconProvider.Folder,
                    Action = _ =>
                    {
                        context.API.OpenDirectory(Path.GetDirectoryName(currentEntry.Path), currentEntry.Path);
                        return true;
                    }
                })
                .ToList();
            }
            return null;
        }

        private static List<Result> SingleResult(string title)
        {
            return new List<Result>
            {
                new Result
                {
                    Title = title,
                    IcoPath = IconProvider.DefaultIcon,
                }
            };
        }
        private Result CreateEntryResult(Entry e)
        {
            string iconPath  = IconProvider.DefaultIcon;
            Action action = () => context.API.ShellRun($"\"{e.Path}\"");
            if (!string.IsNullOrWhiteSpace(settings.DefaultVSId))
            {
                var instance = plugin.VSInstances.FirstOrDefault(i => i.InstanceId == settings.DefaultVSId);
                if (instance != null)
                {
                    //iconPath = iconProvider.GetIconPath(instance);
                    action = () => context.API.ShellRun($"\"{e.Path}\"", $"\"{instance.ExePath}\"");
                }
            }
            entryHighlightData.TryGetValue(e, out var highlightData);
            return new Result
            {
                Title = Path.GetFileNameWithoutExtension(e.Path),
                TitleHighlightData = highlightData,
                SubTitle = e.Value.IsFavorite ? $"★  {e.Path}" : e.Path,
                SubTitleToolTip = $"{e.Path}\n\nLast Accessed:\t{e.Value.LastAccessed:F}",
                ContextData = e,
                IcoPath =  iconPath,
                Action = _ =>
                {
                    action();
                    return true;
                }
            };
        }

        private bool FuzzySearch(Entry entry, string search)
        {
            var matchResult = context.API.FuzzySearch(search, Path.GetFileNameWithoutExtension(entry.Path));
            entryHighlightData[entry] = matchResult.MatchData;
            return matchResult.IsSearchPrecisionScoreMet();
        }
        private bool TypeSearch(Entry entry, Query query, TypeKeyword typeKeyword)
        {
            var search = query.Search[typeKeyword.Keyword.Length..];
            if (string.IsNullOrWhiteSpace(search))
            {
                return entry.ItemType == typeKeyword.Type;
            }
            else
            {
                return entry.ItemType == typeKeyword.Type && FuzzySearch(entry, search);
            }
        }
        public System.Windows.Controls.Control CreateSettingPanel()
        {
            return new UI.SettingsView(new UI.SettingsViewModel(settings, plugin, iconProvider, this));
        }
        public record struct TypeKeyword(int Type, string Keyword);
    }
}