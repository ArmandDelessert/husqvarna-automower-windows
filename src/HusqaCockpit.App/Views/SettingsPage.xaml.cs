using HusqaCockpit.App.Services;
using HusqaCockpit.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HusqaCockpit.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        var app = App.Current;
        ViewModel = new SettingsViewModel(app.Settings, AppSettingsStore.Default, app.Credentials, app.Host, app.Shell, Loc.Strings, TimeProvider.System);
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel.RefreshQuota();
        base.OnNavigatedTo(e);
    }
}
