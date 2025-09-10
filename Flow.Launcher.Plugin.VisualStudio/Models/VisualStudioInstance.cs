using System;
using System.IO;
using System.Text.Json;

namespace Flow.Launcher.Plugin.VisualStudio.Models
{
    public class VisualStudioInstance
    {
        public string InstanceId { get; }
        public string ExePath { get; }
        public string DisplayName { get; }
        public string RecentItemsPath { get; }
        public string DisplayVersion { get; }

        public VisualStudioInstance(JsonElement element)
        {
            var instanceId = element.GetProperty("instanceId").GetString();
            var installationVersion = element.GetProperty("installationVersion").Deserialize<Version>();
            ExePath = element.GetProperty("productPath").GetString();
            DisplayName = element.GetProperty("displayName").GetString();
            DisplayVersion = element.GetProperty("catalog").GetProperty("productDisplayVersion").GetString();

            InstanceId = $"{installationVersion.Major}.0_{instanceId}";

            RecentItemsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                           "Microsoft\\VisualStudio",
                                           InstanceId,
                                           "ApplicationPrivateSettings.xml");
        }
    }

}
