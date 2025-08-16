using System;
using System.IO;

namespace Flow.Launcher.Plugin.VisualStudio.Models
{
    public class Settings
    {
        #nullable enable
        //If null let windows decide which Visual Studio to use.
        public string? DefaultVSId { get; set; }
        #nullable restore
        public bool AutoUpdateBackup { get; set; } = true;
        public DateTime LastBackup { get; set; } = DateTime.MinValue;
        public Entry[] EntriesBackup { get; set; }
        public string VswherePath { get; set; } = DefaultVswherePath;
        
        public static string DefaultVswherePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), 
            "Microsoft Visual Studio\\Installer\\vswhere.exe");
    }
}