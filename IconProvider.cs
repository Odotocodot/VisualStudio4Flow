using System.Collections.Concurrent;
using System.IO;

namespace Flow.Launcher.Plugin.VisualStudio
{
    //TODO: Convert to non static class IconProvider
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

        public bool TryGetIconPath(string iconFileName, out string iconPath)
        {
            return vsIcons.TryGetValue(iconFileName, out iconPath);
        }
    }
}