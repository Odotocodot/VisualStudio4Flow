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
        public string RecentItemsPath { get; init; }
        public string DisplayVersion { get; init; }

        public static VisualStudioInstance Create(JsonElement element, IconProvider iconProvider)
        {
            var vs = new VisualStudioInstance(element);
            if(!iconProvider.TryGetIconPath(vs.InstanceId, out _))
                iconProvider.CreateIcon(vs);
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
    }

}
