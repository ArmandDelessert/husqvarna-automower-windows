using System.Text.Json;
using HusqaCockpit.App.Services;
using HusqaCockpit.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.System;

namespace HusqaCockpit.App.Views;

/// <summary>
/// OpenStreetMap map (Leaflet in a WebView2) showing the GPS tracks of mowers.
/// The page is served from the app's Assets\Map folder under a virtual host name; only the map tiles
/// are fetched from the Internet.
/// </summary>
public sealed partial class MapView : UserControl
{
    private const string HostName = "appassets.husqa";
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);
    private static CoreWebView2Environment? s_environment;

    private IReadOnlyList<MapTrack>? _tracks;
    private bool _labels;
    private bool _fitNext;
    private string? _postedKey;
    private bool _pageReady;
    private bool _initializing;

    public MapView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitializeWebViewAsync();
    }

    /// <summary>Draws the given tracks. <paramref name="labels"/> shows the mower names next to their position.</summary>
    public void SetTracks(IReadOnlyList<MapTrack> tracks, bool labels, bool recenter = false)
    {
        _tracks = tracks;
        _labels = labels;
        _fitNext |= recenter;
        Flush();
    }

    /// <summary>Zooms the map back on the tracks.</summary>
    public void Recenter()
    {
        _fitNext = true;
        Flush();
    }

    /// <summary>Releases the browser process; the control cannot be used afterwards.</summary>
    public void CloseWebView()
    {
        _pageReady = false;
        Web.Close();
    }

    private string Serialize(bool fit) => JsonSerializer.Serialize(new { tracks = _tracks, labels = _labels, fit }, s_json);

    private void Flush()
    {
        if (!_pageReady || _tracks is null)
        {
            return;
        }

        // Frequent position events: skip the message when nothing changed (unless a recenter was asked).
        var key = Serialize(fit: false);
        if (key == _postedKey && !_fitNext)
        {
            return;
        }

        _postedKey = key;
        var json = _fitNext ? Serialize(fit: true) : key;
        _fitNext = false;
        Web.CoreWebView2.PostWebMessageAsJson(json);
    }

    private async Task InitializeWebViewAsync()
    {
        if (_initializing || Web.CoreWebView2 is not null)
        {
            return;
        }
        _initializing = true;

        try
        {
            s_environment ??= await CoreWebView2Environment.CreateWithOptionsAsync(
                browserExecutableFolder: null,
                userDataFolder: Path.Combine(AppPaths.DataFolder, "WebView2"),
                options: new CoreWebView2EnvironmentOptions());
            await Web.EnsureCoreWebView2Async(s_environment);

            var core = Web.CoreWebView2 ?? throw new InvalidOperationException("WebView2 did not initialize.");
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.SetVirtualHostNameToFolderMapping(
                HostName, Path.Combine(AppContext.BaseDirectory, "Assets", "Map"), CoreWebView2HostResourceAccessKind.Allow);

            // The page signals when its script is running: posting earlier would lose the message.
            core.WebMessageReceived += (sender, e) =>
            {
                if (e.TryGetWebMessageAsString() == "ready")
                {
                    _pageReady = true;
                    _postedKey = null;
                    Flush();
                }
            };

            // Links (e.g. the OpenStreetMap attribution) open in the default browser; the map never navigates away.
            core.NewWindowRequested += (sender, e) =>
            {
                e.Handled = true;
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                {
                    _ = Launcher.LaunchUriAsync(uri);
                }
            };
            core.NavigationStarting += (sender, e) =>
            {
                if (!e.Uri.StartsWith($"https://{HostName}/", StringComparison.OrdinalIgnoreCase))
                {
                    e.Cancel = true;
                }
            };

            Web.Source = new Uri($"https://{HostName}/map.html");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or FileNotFoundException)
        {
            // Typically: the WebView2 runtime is not installed (Windows 10 without Edge WebView2).
            ErrorText.Text = Loc.Get("Map_Unavailable");
            ErrorPanel.Visibility = Visibility.Visible;
            Web.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _initializing = false;
        }
    }
}
