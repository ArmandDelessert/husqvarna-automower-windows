using System.Runtime.InteropServices;
using HusqaCockpit.App.Services;
using HusqaCockpit.App.Views;
using HusqaCockpit.Presentation;
using HusqaCockpit.Presentation.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;

namespace HusqaCockpit.App;

public sealed partial class MainWindow : Window
{
    // Room for one mower card and the navigation bar.
    private const int MinimumWidth = 600;
    private const int MinimumHeight = 500;

    private bool _boundsRestored;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(AppPaths.IconPath);
        AppWindow.Title = Loc.Get("AppName");
        AppWindow.Closing += OnClosing;

        // Caption buttons (minimize/maximize/close) follow the light/dark app theme.
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            var scale = GetDpiForWindow(Win32Interop.GetWindowFromWindowId(AppWindow.Id)) / 96.0;
            presenter.PreferredMinimumWidth = (int)(MinimumWidth * scale);
            presenter.PreferredMinimumHeight = (int)(MinimumHeight * scale);
        }

        ContentFrame.Navigate(typeof(DashboardPage));
        if (!App.Current.Host.HasCredentials)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
        }
    }

    public DashboardViewModel Dashboard { get; } = App.Current.Dashboard;

    public void ShowAndActivate()
    {
        if (!_boundsRestored)
        {
            RestoreBounds();
            _boundsRestored = true;
        }
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }
        Activate();
        SetForegroundWindow(Win32Interop.GetWindowFromWindowId(AppWindow.Id));
    }

    public void ShowMower(string mowerId)
    {
        if (ContentFrame.Content is MowerDetailPage { MowerId: var current } && current == mowerId)
        {
            return;
        }
        ContentFrame.Navigate(typeof(MowerDetailPage), mowerId, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
    }

    public void ShowSettings() => ContentFrame.Navigate(typeof(SettingsPage));

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        SaveBounds();
        if (App.Current.IsExiting)
        {
            return;
        }

        args.Cancel = true;
        if (App.Current.Settings.CloseToTray)
        {
            AppWindow.Hide();
            App.Current.OnWindowHiddenToTray();
        }
        else
        {
            App.Current.Quit();
        }
    }

    // ItemInvoked (not SelectionChanged): clicking "My mowers" must also leave a mower's detail page,
    // although that item is already selected there.
    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        var target = args.IsSettingsInvoked ? typeof(SettingsPage)
            : ReferenceEquals(args.InvokedItemContainer, MapItem) ? typeof(MapPage)
            : typeof(DashboardPage);
        if (ContentFrame.Content?.GetType() != target)
        {
            ContentFrame.Navigate(target);
        }
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        // Keep the navigation selection in sync with back navigation.
        NavView.SelectedItem = e.SourcePageType == typeof(SettingsPage) ? NavView.SettingsItem
            : e.SourcePageType == typeof(MapPage) ? MapItem
            : DashboardItem;
    }

    private void AppTitleBar_BackRequested(TitleBar sender, object args)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args) => NavView.IsPaneOpen = !NavView.IsPaneOpen;

    private void RestoreBounds()
    {
        var bounds = App.Current.Settings.Window;
        if (bounds is not null && IsOnScreen(bounds))
        {
            AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            if (bounds.Maximized && AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
            return;
        }

        // First launch: wide enough for three cards side by side, centered.
        var scale = Content?.XamlRoot?.RasterizationScale ?? GetDpiForWindow(Win32Interop.GetWindowFromWindowId(AppWindow.Id)) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)(1500 * scale), area.Width);
        var height = Math.Min((int)(820 * scale), area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
    }

    private void SaveBounds()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter || presenter.State == OverlappedPresenterState.Minimized || !AppWindow.IsVisible)
        {
            return;
        }
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        var maximized = presenter.State == OverlappedPresenterState.Maximized;
        var previous = App.Current.Settings.Window;
        App.Current.Settings.Window = maximized && previous is not null
            ? previous with { Maximized = true }
            : new WindowBounds(position.X, position.Y, size.Width, size.Height, maximized);
        AppSettingsStore.Default.Save(App.Current.Settings);
    }

    private static bool IsOnScreen(WindowBounds bounds)
    {
        // DisplayArea.FindAll() must be accessed by index: enumerating it throws InvalidCastException.
        var displays = DisplayArea.FindAll();
        for (var i = 0; i < displays.Count; i++)
        {
            if (Contains(displays[i].WorkArea, bounds))
            {
                return true;
            }
        }
        return false;
    }

    private static bool Contains(RectInt32 area, WindowBounds bounds) =>
        bounds.Width > 200 && bounds.Height > 200
        && bounds.X + 50 >= area.X && bounds.Y >= area.Y - 10
        && bounds.X + 100 <= area.X + area.Width && bounds.Y + 50 <= area.Y + area.Height;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
