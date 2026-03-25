namespace BusyLight.Models;

/// <summary>
/// One continuous presence interval — either a live Teams status or a manual override.
/// The last record in the history list has <see cref="End"/> == null (still active).
/// </summary>
public sealed class PresenceRecord
{
    public required DateTime  Start        { get; init; }
    public          DateTime? End          { get; set; }   // null = currently active
    public required string    Availability { get; init; }
    public required bool      IsOverride   { get; init; }

    /// <summary>Duration of this interval (up to now when still active).</summary>
    public TimeSpan Duration => (End ?? DateTime.Now) - Start;
}
