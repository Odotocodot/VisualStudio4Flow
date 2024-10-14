using System;
using System.IO;

namespace Flow.Launcher.Plugin.VisualStudio
{
    public class Settings
    {
        //If null let windows decide;
        public string DefaultVSId { get; set; }
        public bool AutoUpdateBackup { get; set; } = true;
        public DateTime LastBackup { get; set; } = DateTime.MinValue;
        public Entry[] EntriesBackup { get; set; }
        public string VswherePath { get; set; } = DefaultVswherePath;
        
        public static string DefaultVswherePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), 
            "Microsoft Visual Studio\\Installer\\vswhere.exe");
    }
}