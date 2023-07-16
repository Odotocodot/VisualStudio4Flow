﻿using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin.VisualStudio.UI
{
    public class SettingsViewModel : BaseModel
    {
        private readonly Settings settings;
        private readonly VisualStudioPlugin plugin;
        private VisualStudioModel selectedVSInstance;

        public SettingsViewModel(Settings settings, VisualStudioPlugin plugin)
        {
            this.settings = settings;
            this.plugin = plugin;
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
            VSInstances = new List<VisualStudioModel>(plugin.VSInstances.Select(vs => new VisualStudioModel
            {
                IconPath = vs.IconPath,
                Name = $"{vs.DisplayName} [Version: {vs.DisplayVersion}]",
                InstanceId = vs.InstanceId,
            }));
            VSInstances.Insert(0, new VisualStudioModel
            {
                IconPath = Icons.Windows,
                Name = "Let Windows Decide (Default)",
                InstanceId = null,
            });
            SelectedVSInstance = VSInstances.FirstOrDefault(i => i.InstanceId == settings.DefaultVSId);
        }
        public async Task RefreshInstances()
        {
            await plugin.ReloadDataAsync();
            SetupVSInstances(settings, plugin);
            OnPropertyChanged(nameof(VSInstances));
        }
        public void ClearInvalidRecentItems()
        {
            //TODO: after implementing backup
        }
        public void ClearAllRecentItems()
        {
            //TODO: after implementing backup
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