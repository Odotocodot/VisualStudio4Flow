using System.Collections.Concurrent;
using System.IO;

namespace Flow.Launcher.Plugin.VisualStudio
{
    public static class Icons
    {
        public const string DefaultIcon = "Images\\icon.png";
        public const string Remove = "Images\\delete.png";

        private static string iconDirectoryPath;
        private static ConcurrentDictionary<string, string> VSIcons;
        public static string VSIconsDirectoryPath => iconDirectoryPath;

        public static void Init(PluginInitContext context)
        {
            VSIcons = new ConcurrentDictionary<string, string>();

            iconDirectoryPath = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "Images", "VSIcons");
            Directory.CreateDirectory(iconDirectoryPath);

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