using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin.VisualStudio
{
    public class VisualStudioInstance
    {
        public string InstanceId { get; init; }
        public Version InstallationVersion { get; init; }
        public string ExePath { get; init; }
        public string DisplayName { get; init; }
        public string Description { get; init; }
        public string IconPath { get; private set; }
        public string RecentItemsPath { get; init; }
        public string DisplayVersion { get; init; }

        public static async Task<VisualStudioInstance> Create(JsonElement element, IconProvider iconProvider, CancellationToken token = default)
        {
            var vs = new VisualStudioInstance(element);
            await vs.SetIconPath(iconProvider, token);
            return vs;
        }

        private VisualStudioInstance(JsonElement element)
        {
            var instanceId = element.GetProperty("instanceId").GetString();
            InstallationVersion = element.GetProperty("installationVersion").Deserialize<Version>();
            ExePath = element.GetProperty("productPath").GetString();
            DisplayName = element.GetProperty("displayName").GetString();
            Description = element.GetProperty("description").GetString();
            DisplayVersion = element.GetProperty("catalog").GetProperty("productDisplayVersion").GetString();

            InstanceId = $"{InstallationVersion.Major}.0_{instanceId}";

            RecentItemsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                           "Microsoft\\VisualStudio",
                                           InstanceId,
                                           "ApplicationPrivateSettings.xml");
        }

        private async Task SetIconPath(IconProvider iconProvider, CancellationToken cancellationToken = default)
        {
            string iconFileName = InstanceId;
            if (iconProvider.TryGetIconPath(iconFileName, out string iconPath))
            {
                IconPath = iconPath;
            }
            else
            {
                try
                {
                    await Task.Run(() =>
                    {
                        var icon = Icon.ExtractAssociatedIcon(ExePath);
                        var bitmap = icon.ToBitmap();
                        var iconPath = Path.Combine(iconProvider.VSIconsDirectoryPath, $"{iconFileName}.png");
                        using var fileStream = new FileStream(iconPath, FileMode.CreateNew);
                        bitmap.Save(fileStream, ImageFormat.Png);
                        IconPath = iconPath;
                    }, cancellationToken);
                }
                catch (Exception)
                {
                    IconPath = IconProvider.DefaultIcon;
                }
            }
        }
    }

}
