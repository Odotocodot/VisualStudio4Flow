using System;
using System.IO;
using System.Text.Json;

namespace Flow.Launcher.Plugin.VisualStudio.Models
{
    public class VisualStudioInstance
    {
        public string InstanceId { get; init; }
        public Version InstallationVersion { get; init; }
        public string ExePath { get; init; }
        public string DisplayName { get; init; }
        public string RecentItemsPath { get; init; }
        public string DisplayVersion { get; init; }

        public VisualStudioInstance(JsonElement element)
        {
            var instanceId = element.GetProperty("instanceId").GetString();
            InstallationVersion = element.GetProperty("installationVersion").Deserialize<Version>();
            ExePath = element.GetProperty("productPath").GetString();
            DisplayName = element.GetProperty("displayName").GetString();
            DisplayVersion = element.GetProperty("catalog").GetProperty("productDisplayVersion").GetString();

            InstanceId = $"{InstallationVersion.Major}.0_{instanceId}";

            RecentItemsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                           "Microsoft\\VisualStudio",
                                           InstanceId,
                                           "ApplicationPrivateSettings.xml");
        }
    }

}
