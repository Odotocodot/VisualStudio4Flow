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
using System.Windows.Controls;
using System.Xml;
using System.Xml.Linq;

namespace Flow.Launcher.Plugin.VisualStudio
{
    public class VisualStudio : IAsyncPlugin, IContextMenu//, ISettingProvider
	{
        public static readonly TypeKeyword FilesOnly = new(1, "f:");
        public static readonly TypeKeyword ProjectsOnly = new(0, "p:");
        private PluginInitContext context;
        private List<VisualStudioInstance> vsInstances;


        public async Task InitAsync(PluginInitContext context)
        {
            this.context = context;
            vsInstances = await GetVisualStudioInstances();
        }

        public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            var vs = vsInstances[0];
            //TODO: Cache the list of recent entries

            var allRecentItems = GetRecentItems(vs, token);
            IAsyncEnumerable<Entry> selectedItems = null;

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
            return null;
        }

        //TODO: add check if there is no recent items!
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
            await foreach (var entry in JsonSerializer.DeserializeAsyncEnumerable<Entry>(memoryStream, cancellationToken: cancellationToken))
            {
                yield return entry;
            }
            //await memoryStream.DisposeAsync();
        }

        private static async Task<List<VisualStudioInstance>> GetVisualStudioInstances(CancellationToken cancellationToken = default)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "C:\\Program Files (x86)\\Microsoft Visual Studio\\Installer\\vswhere.exe",
                Arguments = "-sort -format json",
                RedirectStandardOutput = true,
            };

            var process = Process.Start(psi);
            using var doc = await JsonDocument.ParseAsync(process.StandardOutput.BaseStream, cancellationToken: cancellationToken);
            ///TODO make this really async, do Task.Whenall
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() < 1)
            {
                throw new InvalidOperationException("No installed version of Visual Studio was found");
            }
            // get iconpath as well
            return doc.RootElement.EnumerateArray()
                                  .Select(i => new VisualStudioInstance(i))
                                  .ToList();
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

        private Result CreateEntryResult(Entry e)
        {
            return new Result
            {
                Title = Path.GetFileNameWithoutExtension(e.Properties.LocalProperties.FullPath),
                SubTitle = e.Properties.IsFavorite ? $"★  {e.Properties.LocalProperties.FullPath}" : e.Properties.LocalProperties.FullPath,
                SubTitleToolTip = $"{e.Properties.LocalProperties.FullPath}\n\nLast Accessed:\t{e.Properties.LastAccessed:F}",
                IcoPath = Images.Icon,
                Action = c =>
                {
                    context.API.ShellRun(e.Properties.LocalProperties.FullPath);
                    return true;
                }
            };
        }

        private bool FuzzySearch(Entry entry, string search)
        {
            var matchResult = context.API.FuzzySearch(search, Path.GetFileNameWithoutExtension(entry.Properties.LocalProperties.FullPath));
            //TODO: Highlight data
            return matchResult.IsSearchPrecisionScoreMet();
        }

        private bool TypeSearch(Entry entry, Query query, TypeKeyword typeKeyword)
        {
            var search = query.Search[typeKeyword.Keyword.Length..];

            if (string.IsNullOrWhiteSpace(search))
                return entry.Properties.LocalProperties.Type == typeKeyword.Type;
            else
                return entry.Properties.LocalProperties.Type == typeKeyword.Type && FuzzySearch(entry, search);
        }

        public Control CreateSettingPanel()
        {
            throw new NotImplementedException();
        }

        public static class Images
		{
			public const string Icon = "Images\\icon.png";
		}
        public record struct TypeKeyword(int Type, string Keyword);
	}


}