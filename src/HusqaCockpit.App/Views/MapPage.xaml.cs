using HusqaCockpit.Presentation;
using HusqaCockpit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HusqaCockpit.App.Views;

/// <summary>One mower in the legend of the map.</summary>
/// <param name="Brush">The color of its track.</param>
/// <param name="Status">What it is doing ("Mowing", "Parked", "Disconnected").</param>
/// <param name="Detail">More about it: when it was last heard of, for a mower out of reach.</param>
/// <param name="StatusBrush">The color of the status (green when all is well, red for an error, grey out of reach).</param>
/// <param name="StatusGlyph">A dot, or a "no connection" sign for a mower out of reach.</param>
public sealed record LegendItem(string Name, Brush Brush, string Status, string Detail, Brush StatusBrush, string StatusGlyph);

/// <summary>One map with the last GPS track of every mower.</summary>
public sealed partial class MapPage : Page
{
    public MapPage()
    {
        InitializeComponent();
        // The page is cached for the life of the app, so this subscription is intentionally permanent.
        ViewModel.SummaryChanged += (_, _) => Refresh(recenter: false);
        Refresh(recenter: true);
    }

    private static DashboardViewModel ViewModel => App.Current.Dashboard;

    private void Refresh(bool recenter)
    {
        var mowers = ViewModel.Mowers;
        var tracks = mowers
            .Select((m, i) => MapTrack.From(m.Mower, MapTrack.ColorFor(i), m.StatusTitle))
            .Where(t => t.Points.Count > 0)
            .ToList();

        // Every mower is listed, also one that reports no position: its status is what matters then.
        Legend.ItemsSource = mowers
            .Select((m, i) => new LegendItem(
                m.Name,
                new SolidColorBrush(ParseColor(MapTrack.ColorFor(i))),
                m.StatusTitle,
                m.StatusDetail,
                BindingHelpers.SeverityBrush(m.Severity),
                m.IsConnected ? "\uEA3B" : "\uEB5E"))
            .ToList();
        EmptyText.Visibility = tracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // New mowers appearing (or all positions arriving after startup) should be framed once.
        Map.SetTracks(tracks, labels: true, recenter);
    }

    private void Recenter_Click(object sender, RoutedEventArgs e) => Map.Recenter();

    private static Color ParseColor(string hex) => Color.FromArgb(
        255,
        Convert.ToByte(hex.Substring(1, 2), 16),
        Convert.ToByte(hex.Substring(3, 2), 16),
        Convert.ToByte(hex.Substring(5, 2), 16));
}
