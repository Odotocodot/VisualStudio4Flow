using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Flow.Launcher.Plugin.VisualStudio.Models;

namespace Flow.Launcher.Plugin.VisualStudio.UI
{
    public class SettingsViewModel : BaseModel
    {
        private readonly Settings settings;
        private readonly VisualStudioPlugin plugin;
        private readonly IconProvider iconProvider;
        private readonly IAsyncReloadable reloadable;

        private VisualStudioViewModel selectedVSInstance;

        public SettingsViewModel(Settings settings, VisualStudioPlugin plugin, IconProvider iconProvider, IAsyncReloadable reloadable)
        {
            this.settings = settings;
            this.plugin = plugin;
            this.iconProvider = iconProvider;
            this.reloadable = reloadable;
            SetupVSInstances(settings, plugin);
        }

        public List<VisualStudioViewModel> VSInstances { get; set; }
        public VisualStudioViewModel SelectedVSInstance
        {
            get => selectedVSInstance;
            set
            {
                selectedVSInstance = value;
                settings.DefaultVSId = selectedVSInstance.InstanceId;
                OnPropertyChanged();
            }
        }

        public string VswherePath
        {
            get => settings.VswherePath;
            set
            {
                settings.VswherePath = value;
                OnPropertyChanged();
            }
        }

        public string DefaultVswherePath => $"Default Path: \"{Settings.DefaultVswherePath}\"";
        public string LastBackup => $"[Last Backup: {settings.LastBackup.ToLocalTime()}]";
        public bool AutoUpdateBackup
        {
            get => settings.AutoUpdateBackup;
            set
            {
                settings.AutoUpdateBackup = value;
                OnPropertyChanged();
            }
        }

        private void SetupVSInstances(Settings settings, VisualStudioPlugin plugin)
        {
            VSInstances = new List<VisualStudioViewModel>()
            {
                new VisualStudioViewModel
                {
                    IconPath = iconProvider.Windows,
                    Name = "Let Windows Decide (Default)",
                    InstanceId = null,
                }
            };

            VSInstances.AddRange(plugin.VSInstances.Select(vs =>
            {
                return new VisualStudioViewModel
                {
                    IconPath = iconProvider.GetIconPath(vs),
                    Name = $"{vs.DisplayName} [Version: {vs.DisplayVersion}]",
                    InstanceId = vs.InstanceId,
                };
            }));

            SelectedVSInstance = VSInstances.FirstOrDefault(i => i.InstanceId == settings.DefaultVSId, VSInstances[0]);
        }
        public async Task RefreshInstances()
        {
            await reloadable.ReloadDataAsync();
            SetupVSInstances(settings, plugin);
            OnPropertyChanged(nameof(VSInstances));
        }
        public async Task ClearInvalidRecentItems() => await plugin.RemoveInvalidEntries();
        public async Task ClearAllRecentItems() => await plugin.RemoveAllEntries();
        public async Task RevertToBackup() => await plugin.RevertToBackup();
        public async Task BackupNow() => await Task.Run(plugin.UpdateBackup);
        public void UpdateLastBackupTime() => OnPropertyChanged(nameof(LastBackup));


    }
}
