using HusqaCockpit.App.ViewModels;
using HusqaCockpit.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HusqaCockpit.App.Views;

public sealed record LegendItem(string Name, Brush Brush);

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
            .Select((m, i) => MapTrack.From(m.Mower, MapTrack.ColorFor(i)))
            .Where(t => t.Points.Count > 0)
            .ToList();

        Legend.ItemsSource = tracks.Select(t => new LegendItem(t.Name, new SolidColorBrush(ParseColor(t.Color)))).ToList();
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
