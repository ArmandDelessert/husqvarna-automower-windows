using System.Text.Json.Nodes;
using HusqaCockpit.Core.Api;
using HusqaCockpit.Core.Fleet;
using HusqaCockpit.Core.Models;

namespace HusqaCockpit.Core.Tests;

public class MowerFleetTests
{
    private static AutomowerEvent Event(string type, string json, string id = TestData.FrontLawnId) =>
        new(type, id, JsonNode.Parse(json)!.AsObject());

    [Fact]
    public void Snapshot_raises_changes_with_no_previous_state()
    {
        var fleet = new MowerFleet(TestData.Zurich);
        var changes = new List<MowerChangedEventArgs>();
        fleet.MowerChanged += (_, e) => changes.Add(e);

        fleet.ApplySnapshot(TestData.MowerResources());

        Assert.Equal(2, changes.Count);
        Assert.All(changes, c => Assert.Null(c.Previous));
        Assert.Equal(["Back Garden", "Front Lawn"], fleet.Mowers.Select(m => m.Name));
    }

    [Fact]
    public void Snapshot_removes_mowers_that_disappeared()
    {
        var fleet = TestData.LoadedFleet();
        var removed = new List<Mower>();
        fleet.MowerRemoved += (_, e) => removed.Add(e.Mower);

        fleet.ApplySnapshot(TestData.MowerResources().Where(r => r["id"]!.GetValue<string>() == TestData.FrontLawnId));

        Assert.Equal("Back Garden", Assert.Single(removed).Name);
        Assert.Single(fleet.Mowers);
    }

    [Fact]
    public void Mower_event_is_merged_and_keeps_other_fields()
    {
        var fleet = TestData.LoadedFleet();
        MowerChangedEventArgs? change = null;
        fleet.MowerChanged += (_, e) => change = e;

        var known = fleet.ApplyEvent(Event("mower-event-v2", """
            { "mower": { "mode": "MAIN_AREA", "activity": "MOWING", "inactiveReason": "NONE", "state": "IN_OPERATION",
                         "errorCode": 0, "isErrorConfirmable": false, "errorCodeTimestamp": 0 } }
            """));

        Assert.True(known);
        Assert.NotNull(change);
        Assert.Equal(MowerActivity.ParkedInCs, change.Previous!.Activity);
        Assert.Equal(MowerActivity.Mowing, change.Current.Activity);
        Assert.Equal(MowerState.InOperation, change.Current.State);
        Assert.Equal(100, change.Current.BatteryPercent);
        Assert.Equal("Front Lawn", change.Current.Name);
    }

    [Fact]
    public void Battery_event_updates_only_the_battery_percentage()
    {
        var fleet = TestData.LoadedFleet();

        fleet.ApplyEvent(Event("battery-event-v2", """{ "battery": { "batteryPercent": 77 } }"""));

        var mower = fleet.Find(TestData.FrontLawnId)!;
        Assert.Equal(77, mower.BatteryPercent);
        Assert.Equal(0, mower.Attributes.Battery.RemainingChargingTime);
    }

    [Fact]
    public void Cutting_height_and_headlight_events_update_settings()
    {
        var fleet = TestData.LoadedFleet();

        fleet.ApplyEvent(Event("cuttingHeight-event-v2", """{ "cuttingHeight": { "height": 3 } }"""));
        fleet.ApplyEvent(Event("headlights-event-v2", """{ "headlights": { "mode": "ALWAYS_ON" } }"""));

        var settings = fleet.Find(TestData.FrontLawnId)!.Attributes.Settings;
        Assert.Equal(3, settings.CuttingHeight);
        Assert.Equal(HeadlightMode.AlwaysOn, settings.Headlight.Mode);
    }

    [Fact]
    public void Position_event_prepends_and_caps_history()
    {
        var fleet = TestData.LoadedFleet();

        for (var i = 0; i < 60; i++)
        {
            fleet.ApplyEvent(Event("position-event-v2", $$"""{ "position": { "latitude": {{40 + i}}, "longitude": 7 } }"""));
        }

        var positions = fleet.Find(TestData.FrontLawnId)!.Attributes.Positions;
        Assert.Equal(50, positions.Count);
        Assert.Equal(99, positions[0].Latitude);
    }

    [Fact]
    public void Message_event_is_reported_without_changing_state()
    {
        var fleet = TestData.LoadedFleet();
        var changed = false;
        MowerMessageEventArgs? received = null;
        fleet.MowerChanged += (_, _) => changed = true;
        fleet.MessageReceived += (_, e) => received = e;

        fleet.ApplyEvent(Event("message-event-v2", """
            { "message": { "time": 1728034996, "code": 3, "severity": "WARNING", "latitude": 46.5, "longitude": 6.6 } }
            """));

        Assert.False(changed);
        Assert.NotNull(received);
        Assert.Equal(3, received.Message.Code);
        Assert.Equal(MessageSeverity.Warning, received.Message.Severity);
        Assert.Equal("Front Lawn", received.Mower.Name);
    }

    [Fact]
    public void Event_for_unknown_mower_is_rejected()
    {
        var fleet = TestData.LoadedFleet();

        Assert.False(fleet.ApplyEvent(Event("battery-event-v2", """{ "battery": { "batteryPercent": 1 } }""", id: "unknown")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"ready":true,"connectionId":"abc="}""")]
    [InlineData("""{"id":"0-0","type":"battery-event-v2","attributes":{}}""")]
    [InlineData("not json")]
    public void Non_event_frames_are_ignored(string frame)
    {
        Assert.Null(AutomowerEventStream.ParseEvent(frame));
    }

    [Fact]
    public void Event_frames_are_parsed()
    {
        var evt = AutomowerEventStream.ParseEvent("""{"id":"abc","type":"battery-event-v2","attributes":{"battery":{"batteryPercent":50}}}""");

        Assert.NotNull(evt);
        Assert.Equal("battery-event-v2", evt.Type);
        Assert.Equal("abc", evt.MowerId);
        Assert.Equal(50, evt.Attributes["battery"]!["batteryPercent"]!.GetValue<int>());
    }
}
