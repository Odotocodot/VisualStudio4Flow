using System.Collections.Concurrent;
using System.IO;

namespace Flow.Launcher.Plugin.VisualStudio
{
    //TODO: Convert to non static class IconProvider
    public static class Icons
    {
        public const string DefaultIcon = "Images\\icon.png";
        public const string Remove = "Images\\delete.png";
        public static string Windows => windows;

        private static string windows;
        private static string iconDirectoryPath;
        private static ConcurrentDictionary<string, string> VSIcons;
        public static string VSIconsDirectoryPath => iconDirectoryPath;

        public static void Init(PluginInitContext context)
        {
            VSIcons = new ConcurrentDictionary<string, string>();

            windows = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "Images", "windows.png");

            iconDirectoryPath = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "Images", "VSIcons");
            Directory.CreateDirectory(iconDirectoryPath);

            foreach (var iconPath in Directory.EnumerateFiles(iconDirectoryPath))
            {
                VSIcons.TryAdd(Path.GetFileNameWithoutExtension(iconPath), iconPath);
            }
        }

        public static void Reload()
        {
            foreach (var iconPath in Directory.EnumerateFiles(iconDirectoryPath))
            {
                VSIcons.TryAdd(Path.GetFileNameWithoutExtension(iconPath), iconPath);
            }
        }

        public static bool TryGetIconPath(string iconFileName, out string iconPath)
        {
            return VSIcons.TryGetValue(iconFileName, out iconPath);
        }
    }
}