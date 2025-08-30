using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Flow.Launcher.Plugin.SharedModels;
using Flow.Launcher.Plugin.VisualStudio.Models;

namespace Flow.Launcher.Plugin.VisualStudio
{
    public class Main : IAsyncPlugin, IContextMenu, ISettingProvider, IAsyncReloadable
    {
        private const string ProjectSearch = "p:";
        private const string FileSearch = "f:";
        
        private PluginInitContext context;
        private VisualStudioPlugin plugin;
        private Settings settings;
        private IconProvider iconProvider;

        public async Task InitAsync(PluginInitContext context)
        {
            this.context = context;
            settings = context.API.LoadSettingJsonStorage<Settings>();
            context.API.VisibilityChanged += OnVisibilityChanged;

            iconProvider = new IconProvider(context);
            plugin = await VisualStudioPlugin.Create(settings, context, iconProvider);
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

            if (!plugin.EntryResults.Any())
            {
                return SingleResult("No recent items found");
            }


            Func<EntryResult, bool> filter;
            string search;
            switch (query.Search)
            {
                case { } s when s.StartsWith(ProjectSearch, StringComparison.OrdinalIgnoreCase):
                    filter = static entryResult => entryResult.EntryType == EntryType.ProjectOrSolution;
                    search = query.Search[ProjectSearch.Length..];
                    break;
                case { } s when s.StartsWith(FileSearch, StringComparison.OrdinalIgnoreCase):
                    filter = static entryResult => entryResult.EntryType == EntryType.FileOrFolder;
                    search = query.Search[FileSearch.Length..];
                    break;
                default:
                    filter = static _ => true;
                    search = query.Search;
                    break;
            }

            if (string.IsNullOrWhiteSpace(search))
            {
                return plugin.EntryResults//.OrderBy(e => e.IsFavorite).ThenBy
                                          .OrderBy(e => e.LastAccessed)
                                          .Where(e => filter(e)) 
                                          .Select((e,i) => EntryResultToResult(e, false, i, null))
                                          .ToList();
            }
            
            
            return plugin.EntryResults.Select(x => new QueryData(x))
                                      .Where(q => filter(q.EntryResult) && FuzzySearch(q, search))
                                      .Select(q => EntryResultToResult(q.EntryResult, true, q.Score, q.HighlightData))
                                      .ToList();
        }

        private bool FuzzySearch(QueryData data, string search)
        {
            MatchResult titleMatch = context.API.FuzzySearch(search, Path.GetFileName(data.EntryResult.Path));
            data.HighlightData = titleMatch.MatchData;

            if (!settings.SearchGitBranch && !settings.SearchPath)
            {
                data.Score = titleMatch.Score;
                return titleMatch.IsSearchPrecisionScoreMet();
            }

            int pathScore = 0;
            if (settings.SearchPath)
            {
                pathScore = context.API.FuzzySearch(search, data.EntryResult.Path).Score;
            }
                
            int branchScore = 0;
            if (data.EntryResult.HasGit && settings.SearchGitBranch)
            {
                branchScore = context.API.FuzzySearch(search, data.EntryResult.GitBranch).Score;
            }

            data.Score = (int)(titleMatch.Score * 1.6 + pathScore + branchScore);
            return data.Score > 0;

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
        
        private Result EntryResultToResult(EntryResult entryResult, bool addSelectedScore, int score, List<int> highlightData)
        {
            string title = Path.GetFileName(entryResult.Path);
            bool validPath = Path.Exists(entryResult.Path);

            string titleToolTip = title;
            if (entryResult.HasGit)
            {
                title += $"  (↱{entryResult.GitBranch})"; //Could use ⇈ or ↱ or ↑
                titleToolTip += $"\n\nBranch:\t{entryResult.GitBranch}";
            }

            return new Result
            {
                Title = title,
                TitleHighlightData = highlightData,
                TitleToolTip = titleToolTip,
                SubTitle = entryResult.IsFavorite ? $"★  {entryResult.Path}" : entryResult.Path,
                SubTitleToolTip = $"{entryResult.Path}\n\nLast Accessed:\t{entryResult.LastAccessed:F}",
                ContextData = new ContextData(entryResult, title, validPath),
                Score = score,
                AddSelectedCount = addSelectedScore,
                IcoPath = IconProvider.DefaultIcon, //TODO: add icons if favorite and/or is (git or invalid)
                AsyncAction = async _ =>
                {
                    if (!validPath)
                    {
                        MessageBoxResult result = context.API.ShowMsgBox(
                            $"\"{title}\" does not exist. Do you want to remove it from your recent items in Visual Studio?", 
                            "Invalid Entry",
                            MessageBoxButton.YesNo, 
                            MessageBoxImage.Warning, 
                            MessageBoxResult.Yes);
                        
                        if (result == MessageBoxResult.Yes)
                        {
                            await plugin.RemoveEntry(entryResult);
                        }
                        return true;
                    }
                    
                    if (!string.IsNullOrWhiteSpace(settings.DefaultVSId))
                    {
                        VisualStudioInstance vsInstance = plugin.VSInstances.FirstOrDefault(i => i.InstanceId == settings.DefaultVSId);
                        if (vsInstance != null)
                        {
                            //iconPath = iconProvider.GetIconPath(instance);
                            context.API.ShellRun($"\"{entryResult.Path}\"", $"\"{vsInstance.ExePath}\"");
                            return true;
                        }
                    }

                    context.API.ShellRun($"\"{entryResult.Path}\"");
                    return true;
                }
            };
        }
        
        public List<Result> LoadContextMenus(Result selectedResult)
        {
            if (selectedResult.ContextData is not ContextData contextData)
                return null;
            
            List<Result> results = new()
            {
                new Result
                {
                    Title = $"Remove \"{contextData.Title}\" from recent items list.",
                    SubTitle = selectedResult.SubTitle,
                    IcoPath = IconProvider.Remove,
                    Score = 1,
                    AddSelectedCount = false,
                    AsyncAction = async _ =>
                    {
                        await plugin.RemoveEntry(contextData.EntryResult);
                        await Task.Delay(100);

                        context.API.ChangeQuery(context.CurrentPluginMetadata.ActionKeyword);
                        return true;
                    }
                }
            };
                
            if (!contextData.ValidPath)
            {
                return results;
            }
                
            results.InsertRange(0, plugin.VSInstances.Select(vs => new Result
            {
                Title = $"Open in \"{vs.DisplayName}\" [Version: {vs.DisplayVersion}]",
                SubTitle = vs.ExePath,
                IcoPath = iconProvider.GetIconPath(vs),
                Score = 2,
                AddSelectedCount = false,
                Action = _ =>
                {
                    context.API.ShellRun($"\"{contextData.EntryResult.Path}\"", $"\"{vs.ExePath}\"");
                    return true;
                }
            }));
            results.Add(new Result
            {
                Title = "Open in File Explorer",
                SubTitle = contextData.EntryResult.Path,
                IcoPath = IconProvider.Folder,
                Score = 0,
                AddSelectedCount = false,
                Action = _ =>
                {
                    context.API.OpenDirectory(Path.GetDirectoryName(contextData.EntryResult.Path), contextData.EntryResult.Path);
                    return true;
                }
            });
            return results;
        }
        
        public System.Windows.Controls.Control CreateSettingPanel()
        {
            return new UI.SettingsView(new UI.SettingsViewModel(settings, plugin, iconProvider, this));
        }

        private record QueryData(EntryResult EntryResult)
        {
            public int Score { get; set; }
            public List<int> HighlightData { get; set; }
        }

        private record ContextData(EntryResult EntryResult, string Title, bool ValidPath);

    }
}