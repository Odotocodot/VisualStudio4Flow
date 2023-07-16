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
        }

        private void ClearInvalid_Click(object sender, RoutedEventArgs e)
        {
            //TODO:
            var result = MessageBox.Show("This will clear # items from the recent items list: \nYAH YAH \nYAH",
                                         "Remove Items",
                                         MessageBoxButton.OKCancel,
                                         MessageBoxImage.Warning,
                                         MessageBoxResult.Cancel);

            if(result == MessageBoxResult.OK)
            {
                viewModel.ClearInvalidRecentItems();
            }
            
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("This will clear all items on the recent items list.",
                                         "Remove Items",
                                         MessageBoxButton.OKCancel,
                                         MessageBoxImage.Warning,
                                         MessageBoxResult.Cancel);
            if(result == MessageBoxResult.OK)
            {
                viewModel.ClearAllRecentItems();    
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            button.IsEnabled = false;
            await viewModel.RefreshInstances();
            button.IsEnabled = true;
        }
    }
}
