namespace BusyLight.Models;

/// <summary>Battery measurement with timestamp for the history graph.</summary>
public sealed record BatteryDataPoint(DateTime Timestamp, BatteryReading Reading);
