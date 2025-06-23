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

            if (!plugin.ValidVswherePath)
            {
                return SingleResult("Could not find vswhere.exe. Please set the path in the plugin settings.", context.API.OpenSettingDialog);
            }

            if (!plugin.IsVSInstalled)
            {
                return SingleResult("No installed version of Visual Studio was found");
            }

            if (query.IsReQuery)
            {
                await plugin.GetRecentEntries(token);
            }

            if (!plugin.RecentEntries.Any())
            {
                return SingleResult("No recent items found");
            }

            entryHighlightData.Clear();

            if (string.IsNullOrWhiteSpace(query.Search))
            {
                return plugin.RecentEntries.OrderBy(e => e.Value.LastAccessed)
                                           .Select(CreateEntryResult)
                                           .ToList();
            }

            Func<EntryScore, Query, bool> searchFunc = query.Search switch
            {
                string search when search.StartsWith(ProjectsOnly.Keyword) => (x, query) => TypeSearch(x, query, ProjectsOnly),
                string search when search.StartsWith(FilesOnly.Keyword) => (x, query) => TypeSearch(x, query, FilesOnly),
                _ => (x, query) => FuzzySearch(x, query.Search),
            };

            return plugin.RecentEntries.Select(x => new EntryScore(x))
                                       .Where(x => searchFunc(x, query))
                                       .Select(x => CreateEntryResult(x.Entry, x.Score))
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
                        Score = 2,
                        AddSelectedCount = false,
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
                    Score = 1,
                    AddSelectedCount = false,
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
                    Score = 0,
                    AddSelectedCount = false,
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

        private static List<Result> SingleResult(string title, Action action = null)
        {
            return new List<Result>
            {
                new Result
                {
                    Title = title,
                    IcoPath = IconProvider.DefaultIcon,
                    Action = _ =>
                    {
                        action?.Invoke();
                        return action != null;
                    }
                }
            };
        }

        private Result CreateEntryResult(Entry e, int score)
        {
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
                Score = score,
                AddSelectedCount = false,
                IcoPath = IconProvider.DefaultIcon,
                Action = _ =>
                {
                    action();
                    return true;
                }
            };
        }

        private bool FuzzySearch(EntryScore entryScore, string search)
        {
            var entry = entryScore.Entry;
            var matchResult = context.API.FuzzySearch(search, Path.GetFileNameWithoutExtension(entry.Path));
            entryHighlightData[entry] = matchResult.MatchData;
            entryScore.Score = matchResult.Score;
            return matchResult.Success;
        }

        private bool TypeSearch(EntryScore entryScore, Query query, TypeKeyword typeKeyword)
        {
            var entry = entryScore.Entry;
            var search = query.Search[typeKeyword.Keyword.Length..];
            if (string.IsNullOrWhiteSpace(search))
            {
                return entry.ItemType == typeKeyword.Type;
            }
            else
            {
                return entry.ItemType == typeKeyword.Type && FuzzySearch(entryScore, search);
            }
        }

        public System.Windows.Controls.Control CreateSettingPanel()
        {
            return new UI.SettingsView(new UI.SettingsViewModel(settings, plugin, iconProvider, this));
        }

        private record struct TypeKeyword(int Type, string Keyword);
        private record EntryScore(Entry Entry)
        {
            public int Score { get; set; } = 0;
        }
    }
}