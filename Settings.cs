using System;

namespace Flow.Launcher.Plugin.VisualStudio
{
    public class Settings
    {
        //If null let windows decide;
        public string DefaultVSId { get; set; }
        public bool AutoUpdateBackup { get; set; } = true;
        public DateTime LastBackup { get; set; } = DateTime.MinValue;
        public Entry[] EntriesBackup { get; set; }
    }
}