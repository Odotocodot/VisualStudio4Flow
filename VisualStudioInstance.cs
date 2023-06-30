using System;
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
        public VisualStudioInstance(JsonElement element)
        {
            InstanceId = element.GetProperty("instanceId").GetString();
            InstallationVersion = element.GetProperty("installationVersion").Deserialize<Version>();
            ExePath = element.GetProperty("productPath").GetString();
            DisplayName = element.GetProperty("displayName").GetString();
            Description = element.GetProperty("description").GetString();

            RecentItemsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                           "Microsoft\\VisualStudio",
                                           $"{InstallationVersion.Major}.0_{InstanceId}",
                                           "ApplicationPrivateSettings.xml");
        }

        public async Task SetIconPath(CancellationToken cancellationToken = default)
        {
            string iconFileName = $"{DisplayName} {InstanceId}";
            if (Icons.TryGetIconPath(iconFileName, out string iconPath))
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
                        var iconPath = Path.Combine(Icons.VSIconsDirectoryPath, $"{iconFileName}.png");
                        using var fileStream = new FileStream(iconPath, FileMode.CreateNew);
                        bitmap.Save(fileStream, ImageFormat.Png);
                        IconPath = iconPath;
                    }, cancellationToken);
                }
                catch (Exception)
                {
                    IconPath = Icons.DefaultIcon;
                }
            }
        }
    }

}
