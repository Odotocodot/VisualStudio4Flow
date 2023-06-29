using System;
using System.IO;
using System.Text.Json;

namespace Flow.Launcher.Plugin.VisualStudio
{
    public class VisualStudioInstance
    {
        public string InstanceId { get; init; }
        public Version InstallationVersion { get; init; }
        public string ExePath { get; init; }
        //https://stackoverflow.com/questions/462270/get-file-icon-used-by-shell
        //https://stackoverflow.com/questions/39958247/convert-icon-to-png
        public string IconPath { get; init; } 
        public string RecentItemsPath { get; init; }
        public VisualStudioInstance(JsonElement element)
        {
            InstanceId = element.GetProperty("instanceId").GetString();
            InstallationVersion = element.GetProperty("installationVersion").Deserialize<Version>();
            ExePath = element.GetProperty("productPath").GetString();

            RecentItemsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft\\VisualStudio"
                , $"{InstallationVersion.Major}.0_{InstanceId}",
                "ApplicationPrivateSettings.xml");
        }
    }

}
