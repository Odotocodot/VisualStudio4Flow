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

        public static readonly TypeKeyword FilesOnly = new(1, "f:");
        public static readonly TypeKeyword ProjectsOnly = new(0, "p:");

        private static bool IsVSInstalled;
        public async Task InitAsync(PluginInitContext context)
        {
            this.context = context;
            Icons.Init(context);
            vsInstances = await GetVisualStudioInstances(new CancellationTokenSource());
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

            VisualStudioInstance vs = vsInstances[0];
            //TODO: Cache the list of recent entries
            //only needs to be updated on making the mainwindowvisble.

            IAsyncEnumerable<Entry> allRecentItems = GetRecentItems(vs, token);
            IAsyncEnumerable<Entry> selectedItems = null;

            if (!await allRecentItems.AnyAsync(token))
            {
                return SingleResult("No recent items found");
            }

            if (string.IsNullOrWhiteSpace(query.Search))
            {
                selectedItems = allRecentItems;
            }
            else if (query.Search.StartsWith(ProjectsOnly.Keyword))
            {
                selectedItems = allRecentItems.Where(e => TypeSearch(e, query, ProjectsOnly));
            }
            else if (query.Search.StartsWith(FilesOnly.Keyword))
            {
                selectedItems = allRecentItems.Where(e => TypeSearch(e, query, FilesOnly));
            }
            else
            {
                selectedItems = allRecentItems.Where(e => FuzzySearch(e, query.Search));
            }

            return await selectedItems.Select(CreateEntryResult).ToListAsync(cancellationToken: token);
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
                    Title = $"Remove \"{selectedResult.Title}\" from recents list.",
                    SubTitle = selectedResult.SubTitle,
                    IcoPath = Icons.Remove,
                    AsyncAction = async c =>
                    {
                        //TODO: Get cached results
                        var vs = vsInstances[0];
                        await UpdateRecentItems(vs, await GetRecentItems(vs).Where(e => e != currentEntry).ToArrayAsync());
                        context.API.ChangeQuery(context.CurrentPluginMetadata.ActionKeyword, true);
                        return false;
                    }
                }).ToList();
            }
            return null;
        }

        private static async IAsyncEnumerable<Entry> GetRecentItems(VisualStudioInstance vs, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var fileStream = new FileStream(vs.RecentItemsPath, FileMode.Open, FileAccess.ReadWrite);
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
            try
            {
                entries = JsonSerializer.DeserializeAsyncEnumerable<Entry>(memoryStream, cancellationToken: cancellationToken);
            }
            catch (Exception) //no recent items;
            {
                await memoryStream.DisposeAsync();
                await fileStream.DisposeAsync();
                reader.Dispose();
                yield break;
            }
            await foreach (var entry in entries)
            {
                yield return entry;
            }
            //await memoryStream.DisposeAsync();
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
                IsVSInstalled = false;
                //throw new InvalidOperationException("No installed version of Visual Studio was found");
            }
            IsVSInstalled = true;

            var bag = new ConcurrentBag<VisualStudioInstance>();

            await Parallel.ForEachAsync(doc.RootElement.EnumerateArray(), new ParallelOptions { MaxDegreeOfParallelism = 10, CancellationToken = ctSource.Token }, async (element, ct) =>
            {
                if (ct.IsCancellationRequested)
                    return;

                var vs = new VisualStudioInstance(element);
                await vs.SetIconPath(ct);
                bag.Add(vs);
            });
            return bag.ToList();
        }

        private static async Task UpdateRecentItems(VisualStudioInstance vs, Entry[] newEntries, CancellationToken cancellationToken = default)
        {
            using var memoryStream = new MemoryStream();

            var json = JsonSerializer.SerializeAsync(memoryStream, newEntries, cancellationToken: cancellationToken);
            ///Open xml document
            using var fileStream = new FileStream(vs.RecentItemsPath, FileMode.Open, FileAccess.ReadWrite);
            var root = await XDocument.LoadAsync(fileStream, LoadOptions.None, cancellationToken);
            var recent = root.Element("content")
                             .Element("indexed")
                             .Elements("collection")
                             .Where(e => (string)e.Attribute("name") == "CodeContainers.Offline")
                             .First()
                             .Element("value");
            ///Make sure Json is converted
            await json;

            using var streamReader = new StreamReader(memoryStream, Encoding.UTF8);
            streamReader.BaseStream.Position = 0;
            recent.Value = await streamReader.ReadToEndAsync(cancellationToken);

            fileStream.SetLength(0);
            using var streamWriter = new StreamWriter(fileStream, Encoding.UTF8);
            await root.SaveAsync(streamWriter, SaveOptions.DisableFormatting, cancellationToken);
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