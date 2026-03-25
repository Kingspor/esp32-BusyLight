namespace BusyLight.Models;

/// <summary>
/// Battery telemetry received from the firmware via the telemetry BLE characteristic.
/// Parsed from a 3-byte binary packet: [voltage_mv_lo, voltage_mv_hi, soc_percent]
/// </summary>
public sealed record BatteryReading(int VoltageMv, int SocPercent)
{
    /// <summary>Battery voltage in Volts.</summary>
    public float VoltageV => VoltageMv / 1000f;

    /// <summary>Human-readable representation, e.g. "3,75 V (45 %)".</summary>
    public override string ToString() => $"{VoltageV:F2} V ({SocPercent} %)";
}
