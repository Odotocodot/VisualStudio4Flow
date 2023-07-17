using System.Collections.Concurrent;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;

namespace Flow.Launcher.Plugin.VisualStudio
{
    public class IconProvider
    {
        public const string DefaultIcon = ImageFolderName + "\\icon.png";
        public const string Remove = ImageFolderName +"\\delete.png";

        private const string ImageFolderName = "Images";
        private const string VSIconsFolderName = "VSIcons";

        private readonly ConcurrentDictionary<string, string> vsIcons;
        private readonly string vsIconsDirectoryPath;

        public string Windows { get; init; }

        public IconProvider(PluginInitContext context)
        {
            vsIcons = new ConcurrentDictionary<string, string>();

            Windows = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, ImageFolderName, "windows.png");

            vsIconsDirectoryPath = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, ImageFolderName, VSIconsFolderName);
            Directory.CreateDirectory(vsIconsDirectoryPath);

            foreach (var iconPath in Directory.EnumerateFiles(vsIconsDirectoryPath))
            {
                vsIcons.TryAdd(Path.GetFileNameWithoutExtension(iconPath), iconPath);
            }
        }

        public void ReloadIcons()
        {
            vsIcons.Clear();
            foreach (var iconPath in Directory.EnumerateFiles(vsIconsDirectoryPath))
            {
                vsIcons.TryAdd(Path.GetFileNameWithoutExtension(iconPath), iconPath);
            }
        }


        public string GetIconPath(VisualStudioInstance vs)
        {
            if (!vsIcons.TryGetValue(vs.InstanceId, out string iconPath))
            {
                try
                {
                    var icon = Icon.ExtractAssociatedIcon(vs.ExePath);
                    var bitmap = icon.ToBitmap();
                    iconPath = Path.Combine(vsIconsDirectoryPath, $"{vs.InstanceId}.png");
                    using var fileStream = new FileStream(iconPath, FileMode.CreateNew);
                    bitmap.Save(fileStream, ImageFormat.Png);
                    vsIcons.TryAdd(vs.InstanceId, iconPath);
                }
                catch (System.Exception)
                {
                    iconPath = DefaultIcon;
                }
            }
            return iconPath;
        }
    }
}