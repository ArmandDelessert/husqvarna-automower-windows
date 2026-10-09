using HusqaCockpit.Core.Models;

namespace HusqaCockpit.Presentation;

/// <summary>The GPS track of one mower, ready to be drawn on the map.</summary>
/// <param name="Points">Positions as [latitude, longitude], oldest first, newest (current) last.</param>
/// <param name="Status">What the mower is doing ("Mowing", "Parked", "Disconnected"…), shown next to its name.</param>
/// <param name="Offline">
/// The mower is not connected to the Husqvarna cloud: its last positions are old, and the map draws them as such
/// (dashed, faded, in grey at the end).
/// </param>
public sealed record MapTrack(string Id, string Name, string Color, IReadOnlyList<double[]> Points, string Status = "", bool Offline = false)
{
    /// <summary>Distinct colors for the mowers of a fleet (readable on the OpenStreetMap background).</summary>
    public static readonly IReadOnlyList<string> Palette =
        ["#2E7D32", "#1565C0", "#EF6C00", "#6A1B9A", "#C62828", "#00838F"];

    public static string ColorFor(int index) => Palette[index % Palette.Count];

    public static MapTrack From(Mower mower, string color, string status = "") => new(
        mower.Id,
        mower.Name,
        color,
        // The API lists the newest position first.
        mower.Attributes.Positions.Reverse().Select(p => new[] { p.Latitude, p.Longitude }).ToList(),
        status,
        Offline: !mower.IsConnected);
}
