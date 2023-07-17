namespace Flow.Launcher.Plugin.VisualStudio
{
    public class Settings
    {
        //If null let windows decide;
        public string DefaultVSId { get; set; }
        public Entry[] EntriesBackup { get; set; }

        public void Backup(PluginInitContext context, Entry[] entries)
        {
            EntriesBackup = entries;
            context.API.SaveSettingJsonStorage<Settings>();
        }
    }
}