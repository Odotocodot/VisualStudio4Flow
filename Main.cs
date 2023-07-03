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
    public class VisualStudioPlugin : IAsyncPlugin, IContextMenu//, IResultUpdated//, ISettingProvider
    {
        private PluginInitContext context;
        private List<VisualStudioInstance> vsInstances;
        private bool IsVSInstalled;

        private static readonly TypeKeyword ProjectsOnly = new(0, "p:");
        private static readonly TypeKeyword FilesOnly = new(1, "f:");

        private ConcurrentDictionary<string, Entry> allRecentItems;

        private IEnumerable<Entry> AllEntries => allRecentItems./*Values;//*/Select(kvp =>  kvp.Value);

        public async Task InitAsync(PluginInitContext context)
        {
            this.context = context;
            Icons.Init(context);
            vsInstances = await GetVisualStudioInstances(new CancellationTokenSource());
            IsVSInstalled = vsInstances.Any();
            allRecentItems = new ConcurrentDictionary<string, Entry>();
        }

        public async Task GetAllRecentItems(CancellationToken token = default)
        {
            allRecentItems.Clear();
            await Parallel.ForEachAsync(vsInstances, async (instance, ct) =>
            {
                instance.ClearRecents();
                await foreach (var entry in GetRecentItems(instance, token))
                {
                    instance.AddRecentItem(entry);
                    allRecentItems.TryAdd(entry.Key, entry);
                }
            });
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

            //TODO: Cache the list of recent entries
            //only needs to be updated on making the mainwindowvisble.
            await GetAllRecentItems(token);
            if (!allRecentItems.Any())
            {
                return SingleResult("No recent items found");
            }

            var selectedRecentItems = query.Search switch
            {
                string search when string.IsNullOrEmpty(search) => AllEntries,
                string search when search.StartsWith(ProjectsOnly.Keyword) => AllEntries.Where(e => TypeSearch(e, query, ProjectsOnly)),
                string search when search.StartsWith(FilesOnly.Keyword) => AllEntries.Where(e => TypeSearch(e, query, FilesOnly)),
                _ => AllEntries.Where(e => FuzzySearch(e, query.Search))
            };

            return selectedRecentItems.OrderBy(e => e.Value.LastAccessed).Select(CreateEntryResult).ToList();
        }
        public List<Result> LoadContextMenus(Result selectedResult)
        {
            if (selectedResult.ContextData is Entry currentEntry)
            {
                return vsInstances.Select(instance => new Result
                {
                    Title = "Open in \"" + instance.DisplayName + "\"",
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
                        //TODO: Get cached results
                        await Parallel.ForEachAsync(vsInstances, async (instance, ct) =>
                        {
                            allRecentItems.TryRemove(currentEntry.Key, out _);
                            if(instance.RemoveRecentItem(currentEntry))
                            {
                                await UpdateRecentItems(instance, ct);
                            }
                        });
                        context.API.ChangeQuery(context.CurrentPluginMetadata.ActionKeyword, true);
                        return false;
                    }
                }).ToList();
            }
            return null;
        }

        private static async IAsyncEnumerable<Entry> GetRecentItems(VisualStudioInstance vs, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var fileStream = new FileStream(vs.RecentItemsPath, FileMode.Open, FileAccess.Read);
            using var reader = XmlReader.Create(fileStream, new XmlReaderSettings() { Async = true });
            await reader.MoveToContentAsync();

            while (await reader.ReadAsync())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.HasAttributes && reader.GetAttribute(0) == "CodeContainers.Offline")
                {
                    break;
                }
            }
            while (await reader.ReadAsync())
            {
                if (reader.HasValue)
                {
                    break;
                }
            }

            using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(await reader.GetValueAsync()));

            IAsyncEnumerable<Entry> entries = AsyncEnumerable.Empty<Entry>();
            //TODO: no recent items
            entries = JsonSerializer.DeserializeAsyncEnumerable<Entry>(memoryStream, cancellationToken: cancellationToken);
            //try
            //{
            //}
            //catch (Exception) //no recent items;
            //{
            //    //await memoryStream.DisposeAsync();
            //    //await fileStream.DisposeAsync();
            //    //reader.Dispose();
            //    yield break;
            //}
            await foreach (var entry in entries)
            {
                yield return entry;
            }
            //await memoryStream.DisposeAsync();
            //await fileStream.DisposeAsync();
            //reader.Dispose();
        }

        private static async Task<List<VisualStudioInstance>> GetVisualStudioInstances(CancellationTokenSource ctSource)
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
                return new List<VisualStudioInstance>();
                //throw new InvalidOperationException("No installed version of Visual Studio was found");
            }

            var instances = doc.RootElement.EnumerateArray()
                                           .Select(element => new VisualStudioInstance(element))
                                           .ToList();
            
            //set icons
            await Parallel.ForEachAsync(instances, new ParallelOptions { MaxDegreeOfParallelism = 10, CancellationToken = ctSource.Token }, async (instance, ct) =>
            {
                if (ct.IsCancellationRequested)
                    return;
                await instance.SetIconPath(ct);
            });

            return instances;
        }

        private static async Task UpdateRecentItems(VisualStudioInstance vs, CancellationToken cancellationToken = default)
        {
            if (!vs.Entries.Any())
                return;

            using var memoryStream = new MemoryStream();

            var json = JsonSerializer.SerializeAsync(memoryStream, vs.Entries.ToArray(), cancellationToken: cancellationToken);
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
                SubTitle = e.Value.IsFavorite ? $"★  {e.Path}" : e.Path,
                SubTitleToolTip = $"{e.Path}\n\nLast Accessed:\t{e.Value.LastAccessed:F}",
                ContextData = e,
                IcoPath = Icons.DefaultIcon,
                Action = c =>
                {
                    context.API.ShellRun($"\"{e.Path}\"");
                    return true;
                }
            };
        }


        private bool FuzzySearch(Entry entry, string search)
        {
            var matchResult = context.API.FuzzySearch(search, Path.GetFileNameWithoutExtension(entry.Path));
            //TODO: Highlight data
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
            throw new NotImplementedException();
        }
        public record struct TypeKeyword(int Type, string Keyword);
    }
}