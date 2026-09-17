namespace BusyLight.Models;

/// <summary>
/// Battery telemetry received from the firmware via the telemetry BLE characteristic.
/// Parsed from a 3-byte binary packet: [voltage_mv_lo, voltage_mv_hi, soc_percent]
/// </summary>
public sealed record BatteryReading(int VoltageMv, int SocPercent)
{
    /// <summary>
    /// Below this a reading is not a low battery but a broken measurement.
    ///
    /// A Li-Ion 18650 in service never gets here: its protection circuit cuts out
    /// between 2.5 and 3.0 V, and the step-up converter gives up well before that — a
    /// cell that low could not be powering the device that reports it.  What does
    /// produce such values is USB being plugged in: the measured node then collapses to
    /// around 1.7 V while the cell itself is perfectly healthy.  Reading that as
    /// "almost empty" is worse than admitting we do not know — it fires the low-battery
    /// warning through every development session and writes fiction into the history.
    /// </summary>
    public const int MinPlausibleMv = 2500;

    /// <summary>False when this cannot be a real cell voltage — see <see cref="MinPlausibleMv"/>.</summary>
    public bool IsPlausible => VoltageMv >= MinPlausibleMv;

    /// <summary>Battery voltage in Volts.</summary>
    public float VoltageV => VoltageMv / 1000f;

    /// <summary>Human-readable representation, e.g. "3,75 V (45 %)".</summary>
    public override string ToString() => $"{VoltageV:F2} V ({SocPercent} %)";
}
