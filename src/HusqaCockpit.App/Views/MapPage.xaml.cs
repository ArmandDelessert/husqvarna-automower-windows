using HusqaCockpit.Presentation;
using HusqaCockpit.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HusqaCockpit.App.Views;

/// <summary>One mower in the legend of the map: the card binds to the view model, so its texts follow the mower.</summary>
/// <param name="Mower">The mower, whose status, icon and last update the card shows.</param>
/// <param name="TrackBrush">The color of its track on the map, drawn as the bar at the left of the card.</param>
public sealed record LegendItem(MowerViewModel Mower, Brush TrackBrush);

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
            .Select((m, i) => new LegendItem(m, new SolidColorBrush(ParseColor(MapTrack.ColorFor(i)))))
            .ToList();
        EmptyText.Visibility = tracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // New mowers appearing (or all positions arriving after startup) should be framed once.
        Map.SetTracks(tracks, labels: true, recenter);
    }

    private void OpenDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string mowerId })
        {
            App.Current.Window.ShowMower(mowerId);
        }
    }

    private void Recenter_Click(object sender, RoutedEventArgs e) => Map.Recenter();

    private static Color ParseColor(string hex) => Color.FromArgb(
        255,
        Convert.ToByte(hex.Substring(1, 2), 16),
        Convert.ToByte(hex.Substring(3, 2), 16),
        Convert.ToByte(hex.Substring(5, 2), 16));
}
