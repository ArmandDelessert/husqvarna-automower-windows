using System.Text.Json;
using HusqaCockpit.Core.Models;
using HusqaCockpit.Presentation.Tests.Support;

namespace HusqaCockpit.Presentation.Tests;

public class MapTrackTests
{
    // The options MapView serializes with.
    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyList<GeoPosition> s_newestFirst =
    [
        new() { Latitude = 46.3, Longitude = 6.3 },
        new() { Latitude = 46.2, Longitude = 6.2 },
        new() { Latitude = 46.1, Longitude = 6.1 },
    ];

    private static Mower Create(bool connected, string name = "Alpha")
    {
        var attributes = TestMowers.Parked(name);
        return TestMowers.Create(attributes with
        {
            Positions = s_newestFirst,
            Metadata = attributes.Metadata with { Connected = connected },
        });
    }

    [Fact]
    public void The_track_runs_from_the_oldest_position_to_the_newest()
    {
        var track = MapTrack.From(Create(connected: true), "#2E7D32");

        Assert.Equal([[46.1, 6.1], [46.2, 6.2], [46.3, 6.3]], track.Points);
    }

    [Fact]
    public void A_mower_in_reach_is_drawn_as_online_with_its_status()
    {
        var track = MapTrack.From(Create(connected: true), "#2E7D32", "Parked");

        Assert.False(track.Offline);
        Assert.Equal("Parked", track.Status);
        Assert.Equal("Alpha", track.Name);
        Assert.Equal("#2E7D32", track.Color);
    }

    [Fact]
    public void A_mower_out_of_reach_is_drawn_as_offline()
    {
        var track = MapTrack.From(Create(connected: false), "#1565C0", "Disconnected");

        Assert.True(track.Offline);
        Assert.Equal("Disconnected", track.Status);
    }

    [Fact]
    public void The_status_is_optional()
    {
        Assert.Equal("", MapTrack.From(Create(connected: true), "#2E7D32").Status);
    }

    [Fact]
    public void A_mower_without_positions_has_an_empty_track()
    {
        var mower = TestMowers.Create(TestMowers.Parked());

        Assert.Empty(MapTrack.From(mower, "#2E7D32").Points);
    }

    [Fact]
    public void The_colors_repeat_after_the_last_one()
    {
        Assert.Equal(MapTrack.Palette[0], MapTrack.ColorFor(0));
        Assert.Equal(MapTrack.Palette[^1], MapTrack.ColorFor(MapTrack.Palette.Count - 1));
        Assert.Equal(MapTrack.Palette[0], MapTrack.ColorFor(MapTrack.Palette.Count));
        Assert.Equal(MapTrack.Palette.Count, MapTrack.Palette.Distinct().Count());
    }

    [Fact]
    public void The_page_of_the_map_receives_the_status_and_the_offline_flag_in_camel_case()
    {
        // map.js reads track.status and track.offline: the names must not change on the way (JsonSerializerDefaults.Web).
        var json = JsonSerializer.Serialize(MapTrack.From(Create(connected: false), "#2E7D32", "Disconnected"), s_web);

        Assert.Contains("\"status\":\"Disconnected\"", json, StringComparison.Ordinal);
        Assert.Contains("\"offline\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"points\":[[46.1,6.1]", json, StringComparison.Ordinal);
    }
}
