using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Flow.Launcher.Plugin.VisualStudio.UI
{
    public partial class SettingsView : UserControl
    {
        private readonly SettingsViewModel viewModel;
        public SettingsView(SettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = this.viewModel = viewModel;
            CtxMenu.DataContext = viewModel; //https://stackoverflow.com/questions/59584206/wpf-contextmenu-loses-datacontext-if-it-is-displayed-using-a-left-click-event
        }

        private async void ClearInvalid_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("This will clear all invalid (missing) items from the recent items list in Visual Studio.\nAre you sure you want to continue?",
                                         "Remove Items",
                                         MessageBoxButton.OKCancel,
                                         MessageBoxImage.Warning,
                                         MessageBoxResult.Cancel);

            if(result == MessageBoxResult.OK)
            {
                await viewModel.ClearInvalidRecentItems();
            }
            
        }

        private async void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("This will clear all items on the recent items list in Visual Studio.\nAre you sure you want to continue?",
                                         "Remove Items",
                                         MessageBoxButton.OKCancel,
                                         MessageBoxImage.Warning,
                                         MessageBoxResult.Cancel);

            if(result == MessageBoxResult.OK)
            {
                await viewModel.ClearAllRecentItems();    
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await DisableSenderWhileAwaiting(sender, viewModel.RefreshInstances);

        private async void RevertToBackup(object sender, RoutedEventArgs e) => await DisableSenderWhileAwaiting(sender, viewModel.RevertToBackup);

        private async void BackupNow(object sender, RoutedEventArgs e) => await DisableSenderWhileAwaiting(sender, viewModel.BackupNow);

        private void UpdateBackupTime(object sender, RoutedEventArgs e) => viewModel.UpdateLastBackupTime();

        private static async Task DisableSenderWhileAwaiting(object sender, Func<Task> action)
        {
            var element = (UIElement)sender;
            element.IsEnabled = false;
            await action();
            element.IsEnabled = true;
        }
    }
}
