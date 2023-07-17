using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin.VisualStudio.UI
{
    public class SettingsViewModel : BaseModel
    {
        private readonly Settings settings;
        private readonly VisualStudioPlugin plugin;
        private readonly IAsyncReloadable reloadable;
        private readonly IconProvider iconProvider;
        private VisualStudioModel selectedVSInstance;

        public SettingsViewModel(Settings settings, VisualStudioPlugin plugin, IconProvider iconProvider, IAsyncReloadable reloadable)
        {
            this.settings = settings;
            this.plugin = plugin;
            this.iconProvider = iconProvider;
            this.reloadable = reloadable;
            SetupVSInstances(settings, plugin);
        }
        public List<VisualStudioModel> VSInstances { get; set; }
        public VisualStudioModel SelectedVSInstance
        {
            get => selectedVSInstance; 
            set
            {
                SetProperty(ref selectedVSInstance, value);
                settings.DefaultVSId = selectedVSInstance.InstanceId;
            }
        }

        private void SetupVSInstances(Settings settings, VisualStudioPlugin plugin)
        {
            VSInstances = new List<VisualStudioModel>(plugin.VSInstances.Select(vs => 
            {
                return new VisualStudioModel
                {
                    IconPath = iconProvider.TryGetIconPath(vs),
                    Name = $"{vs.DisplayName} [Version: {vs.DisplayVersion}]",
                    InstanceId = vs.InstanceId,
                };
            }));
            VSInstances.Insert(0, new VisualStudioModel
            {
                IconPath = iconProvider.Windows,
                Name = "Let Windows Decide (Default)",
                InstanceId = null,
            });
            SelectedVSInstance = VSInstances.FirstOrDefault(i => i.InstanceId == settings.DefaultVSId);
        }
        public async Task RefreshInstances()
        {
            await reloadable.ReloadDataAsync();
            SetupVSInstances(settings, plugin);
            OnPropertyChanged(nameof(VSInstances));
        }
        public void ClearInvalidRecentItems()
        {
            //TODO: after implementing backup
            //context.API.ShowMsg($"Removed Recent Item", $"Removed \"{currentEntry.Key}\" from recent items list");

        }
        public void ClearAllRecentItems()
        {
            //TODO: after implementing backup
            //context.API.ShowMsg($"Removed Recent Item", $"Removed \"{currentEntry.Key}\" from recent items list");
        }
        protected bool SetProperty<T>(ref T field, T newValue, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, newValue))
                return false;

            field = newValue;
            OnPropertyChanged(propertyName);
            return true;
        }

        public class VisualStudioModel
        {
            public string IconPath { get; init; }
            public string Name { get; init; }
            public string InstanceId { get; init; }
        }
    }
}
