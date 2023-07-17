using System.Collections.Concurrent;
using System.IO;

namespace Flow.Launcher.Plugin.VisualStudio
{
    public class IconProvider
    {
        public const string DefaultIcon = "Images\\icon.png";
        public const string Remove = "Images\\delete.png";

        private readonly ConcurrentDictionary<string, string> vsIcons;

        public string Windows { get; }
        public string VSIconsDirectoryPath { get; }

        public IconProvider(PluginInitContext context)
        {
            vsIcons = new ConcurrentDictionary<string, string>();

            Windows = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "Images", "windows.png");

            VSIconsDirectoryPath = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "Images", "VSIcons");
            Directory.CreateDirectory(VSIconsDirectoryPath);

            foreach (var iconPath in Directory.EnumerateFiles(VSIconsDirectoryPath))
            {
                vsIcons.TryAdd(Path.GetFileNameWithoutExtension(iconPath), iconPath);
            }
        }

        public void ReloadIcons()
        {
            foreach (var iconPath in Directory.EnumerateFiles(VSIconsDirectoryPath))
            {
                vsIcons.TryAdd(Path.GetFileNameWithoutExtension(iconPath), iconPath);
            }
        }

        /// <param name="vsInstanceId"></param>
        /// <param name="iconPath"></param>
        /// <returns><see cref="DefaultIcon"/> if <paramref name="vsInstanceId"/> is <see langword="null"/></returns>
        public bool TryGetIconPath(string vsInstanceId, out string iconPath)
        {
            if (!string.IsNullOrWhiteSpace(vsInstanceId) && vsIcons.TryGetValue(vsInstanceId, out iconPath))
            {
                return true;
            }
            iconPath = DefaultIcon;
            return false;
        }
    }
}