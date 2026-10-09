using HusqaCockpit.App.Services;
using HusqaCockpit.Presentation.ViewModels;
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

    private void ScrollToApiKey()
    {
        var top = ApiHeader.TransformToVisual(Sections).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
        Scroller.ChangeView(horizontalOffset: null, verticalOffset: top, zoomFactor: null, disableAnimation: true);
    }

    /// <summary>Navigation parameter: open the page on the section of the Husqvarna API key.</summary>
    public const string ApiKeyTarget = "api-key";

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel.RefreshQuota();
        if (Equals(e.Parameter, ApiKeyTarget))
        {
            // The section is near the bottom of the page: scroll to it once the page has been laid out.
            if (IsLoaded)
            {
                ScrollToApiKey();
            }
            else
            {
                Loaded += (_, _) => ScrollToApiKey();
            }
        }
        base.OnNavigatedTo(e);
    }
}
