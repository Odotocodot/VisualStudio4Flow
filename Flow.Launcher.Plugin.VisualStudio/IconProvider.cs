using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Flow.Launcher.Plugin.VisualStudio.Models;

namespace Flow.Launcher.Plugin.VisualStudio
{
    public class IconProvider
    {
        private const string ImageFolderName = "Images";
        private const string VSIconsFolderName = "VSIcons";
        
        public const string DefaultIcon = ImageFolderName + "\\icon.png";
        public const string Remove = ImageFolderName + "\\delete.png";
        public const string Folder = ImageFolderName + "\\folder.png";

        private readonly ConcurrentDictionary<string, string> vsIcons;
        private readonly string vsIconsDirectoryPath;
        private readonly PluginInitContext context;

        public string Windows { get; }
        public string Notification { get; }

        public IconProvider(PluginInitContext context)
        {
            this.context = context;
            vsIcons = new ConcurrentDictionary<string, string>();

            Windows = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, ImageFolderName, "windows.png");
            Notification = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, ImageFolderName, "notification.png");

            vsIconsDirectoryPath = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, ImageFolderName, VSIconsFolderName);
            Directory.CreateDirectory(vsIconsDirectoryPath);
            
            ReloadIcons();
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
            if (vsIcons.TryGetValue(vs.InstanceId, out string iconPath))
                return iconPath;
            
            try
            {
                var icon = Icon.ExtractAssociatedIcon(vs.ExePath);
                var bitmap = icon.ToBitmap();
                iconPath = Path.Combine(vsIconsDirectoryPath, $"{vs.InstanceId}.png");
                using var fileStream = new FileStream(iconPath, FileMode.CreateNew);
                bitmap.Save(fileStream, ImageFormat.Png);
                vsIcons.TryAdd(vs.InstanceId, iconPath);
            }
            catch (Exception e)
            {
                context.API.LogException(typeof(IconProvider).FullName, $"Failed at creating an icon for \"{vs.DisplayName}\"", e);
                iconPath = DefaultIcon;
            }
            return iconPath;
        }
    }
}