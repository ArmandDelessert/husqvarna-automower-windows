using System.Text.Json;
using System.Text.Json.Nodes;
using HusqaCockpit.Core.Api;
using HusqaCockpit.Core.Json;
using HusqaCockpit.Core.Models;

namespace HusqaCockpit.Core.Fleet;

public sealed class MowerChangedEventArgs(Mower? previous, Mower current) : EventArgs
{
    /// <summary>Null when the mower was just discovered.</summary>
    public Mower? Previous { get; } = previous;

    public Mower Current { get; } = current;
}

public sealed class MowerRemovedEventArgs(Mower mower) : EventArgs
{
    public Mower Mower { get; } = mower;
}

public sealed class MowerMessageEventArgs(Mower mower, MowerMessage message) : EventArgs
{
    public Mower Mower { get; } = mower;
    public MowerMessage Message { get; } = message;
}

/// <summary>
/// The current state of every mower on the account. REST snapshots replace the state;
/// WebSocket events are merged into it. Events are raised outside the internal lock,
/// on the calling thread.
/// </summary>
public sealed class MowerFleet(TimeZoneInfo? mowerTimeZone = null)
{
    private const int MaxPositions = 50;

    private readonly TimeZoneInfo _timeZone = mowerTimeZone ?? TimeZoneInfo.Local;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, JsonObject> _raw = [];
    private readonly Dictionary<string, Mower> _mowers = [];

    public event EventHandler<MowerChangedEventArgs>? MowerChanged;
    public event EventHandler<MowerRemovedEventArgs>? MowerRemoved;
    public event EventHandler<MowerMessageEventArgs>? MessageReceived;

    /// <summary>All known mowers, ordered by name.</summary>
    public IReadOnlyList<Mower> Mowers
    {
        get
        {
            lock (_lock)
            {
                return _mowers.Values.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
        }
    }

    public Mower? Find(string mowerId)
    {
        lock (_lock)
        {
            return _mowers.GetValueOrDefault(mowerId);
        }
    }

    /// <summary>Replaces the whole state with a REST snapshot (the "data" items of GET /mowers).</summary>
    public void ApplySnapshot(IEnumerable<JsonObject> resources)
    {
        var changes = new List<MowerChangedEventArgs>();
        var removed = new List<Mower>();
        lock (_lock)
        {
            var seen = new HashSet<string>();
            foreach (var resource in resources)
            {
                if (resource["id"]?.GetValue<string>() is not { } id
                    || id == AutomowerEventFeed.InvalidMowerId
                    || resource["attributes"] is not JsonObject attributes)
                {
                    continue;
                }

                seen.Add(id);
                _raw[id] = (JsonObject)attributes.DeepClone();
                changes.Add(Rebuild(id));
            }

            foreach (var id in _mowers.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                removed.Add(_mowers[id]);
                _mowers.Remove(id);
                _raw.Remove(id);
            }
        }

        foreach (var mower in removed)
        {
            MowerRemoved?.Invoke(this, new MowerRemovedEventArgs(mower));
        }
        foreach (var change in changes)
        {
            MowerChanged?.Invoke(this, change);
        }
    }

    /// <summary>Merges a real-time event. Returns false when the mower is unknown (a snapshot refresh is then needed).</summary>
    public bool ApplyEvent(AutomowerEvent evt)
    {
        MowerChangedEventArgs? change = null;
        MowerMessageEventArgs? message = null;
        lock (_lock)
        {
            if (!_raw.TryGetValue(evt.MowerId, out var raw))
            {
                return false;
            }

            var attributes = evt.Attributes;
            if (attributes["message"] is JsonObject messageNode)
            {
                var parsed = messageNode.Deserialize<MowerMessage>(AutomowerJson.Options);
                if (parsed is not null)
                {
                    message = new MowerMessageEventArgs(_mowers[evt.MowerId], parsed);
                }
            }
            else
            {
                MergeEvent(raw, attributes);
                change = Rebuild(evt.MowerId);
            }
        }

        if (change is not null)
        {
            MowerChanged?.Invoke(this, change);
        }
        if (message is not null)
        {
            MessageReceived?.Invoke(this, message);
        }
        return true;
    }

    private MowerChangedEventArgs Rebuild(string id)
    {
        var previous = _mowers.GetValueOrDefault(id);
        var current = new Mower
        {
            Id = id,
            Attributes = _raw[id].Deserialize<MowerAttributes>(AutomowerJson.Options) ?? new MowerAttributes(),
            TimeZone = _timeZone,
        };
        _mowers[id] = current;
        return new MowerChangedEventArgs(previous, current);
    }

    /// <summary>Applies an event's attributes to the stored REST representation.</summary>
    internal static void MergeEvent(JsonObject raw, JsonObject attributes)
    {
        // A few v2 events use a shape that differs from the REST attributes.
        if (attributes["cuttingHeight"] is JsonObject cuttingHeight)
        {
            Settings(raw)["cuttingHeight"] = cuttingHeight["height"]?.DeepClone();
            return;
        }
        if (attributes["headlights"] is JsonObject headlights)
        {
            Settings(raw)["headlight"] = new JsonObject { ["mode"] = headlights["mode"]?.DeepClone() };
            return;
        }
        if (attributes["position"] is JsonObject position)
        {
            if (raw["positions"] is not JsonArray positions)
            {
                raw["positions"] = positions = [];
            }
            positions.Insert(0, position.DeepClone());
            while (positions.Count > MaxPositions)
            {
                positions.RemoveAt(positions.Count - 1);
            }
            return;
        }

        DeepMerge(raw, attributes);
    }

    private static JsonObject Settings(JsonObject raw)
    {
        if (raw["settings"] is not JsonObject settings)
        {
            raw["settings"] = settings = [];
        }
        return settings;
    }

    private static void DeepMerge(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (value is JsonObject sourceChild && target[key] is JsonObject targetChild)
            {
                DeepMerge(targetChild, sourceChild);
            }
            else
            {
                target[key] = value?.DeepClone();
            }
        }
    }
}
