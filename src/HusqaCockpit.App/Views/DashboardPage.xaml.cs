using HusqaCockpit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HusqaCockpit.App.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
    }

    public DashboardViewModel ViewModel { get; } = App.Current.Dashboard;

    private void OpenDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string mowerId })
        {
            App.Current.Window.ShowMower(mowerId);
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => App.Current.Window.ShowSettings();

    private void CommandInfo_Closed(InfoBar sender, InfoBarClosedEventArgs args) => ViewModel.CommandMessage = null;
}
