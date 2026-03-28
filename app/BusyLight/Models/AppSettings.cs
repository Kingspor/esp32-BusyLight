using System.Collections.Generic;

namespace BusyLight.Models;

/// <summary>
/// Root configuration object deserialized from appsettings.json.
/// </summary>
public class AppSettings
{
    public AzureAdSettings AzureAd { get; set; } = new();
    public PollingSettings Polling { get; set; } = new();

    /// <summary>
    /// Maps Teams presence status names to LED configurations.
    /// Keys match the Graph API Availability strings:
    /// Available, Busy, DoNotDisturb, Away, BeRightBack, Offline, PresenceUnknown
    /// </summary>
    public Dictionary<string, PresenceSettings> PresenceMap { get; set; } = new();

    /// <summary>
    /// Maps incoming Teams presence keys to the PresenceMap key that will be
    /// used for the LED command.  Allows e.g. DoNotDisturb → Busy so that
    /// both states light up the same colour.
    /// Absent keys are treated as identity (map to themselves).
    /// </summary>
    public Dictionary<string, string> PresenceMapping { get; set; } = new();

    /// <summary>
    /// The single BLE peripheral to connect to.
    /// Set by the device picker dialog on first run (or via Settings → BLE-Gerät).
    /// Null means no device has been selected yet — shows the picker on next startup.
    /// </summary>
    public BleDeviceSettings? BleDevice { get; set; }

    /// <summary>
    /// Persisted manual override presence key. Null = no override active.
    /// Only restored on startup when <see cref="KeepOverrideOnRestart"/> is true.
    /// </summary>
    public string? ActiveOverride { get; set; }

    /// <summary>When true the active override is saved and restored on next startup.</summary>
    public bool KeepOverrideOnRestart { get; set; } = false;
}

/// <summary>
/// Azure AD / Entra ID application registration settings.
/// </summary>
public class AzureAdSettings
{
    /// <summary>Application (client) ID from the Azure portal.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Directory (tenant) ID, or "common" / "organizations".</summary>
    public string TenantId { get; set; } = string.Empty;
}

/// <summary>
/// Timing settings for Graph polling and BLE reconnect retries.
/// </summary>
public class PollingSettings
{
    /// <summary>How often to query the Graph presence endpoint (seconds).</summary>
    public int GraphIntervalSeconds { get; set; } = 30;

    /// <summary>How long to wait before attempting to reconnect to BLE (seconds).</summary>
    public int BleRetryIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// Maximum LED brightness as a fraction 0.0–1.0 applied on the app side before
    /// sending the command. The firmware trusts this value without capping it further.
    /// Default 0.6 matches the previous firmware-side BRIGHTNESS_CAP_FACTOR.
    /// </summary>
    public float BrightnessCap { get; set; } = 0.6f;

    /// <summary>
    /// Battery voltage threshold in millivolts for the low-battery balloon warning.
    /// Set to 0 to disable the warning entirely.
    /// Default: 3400 mV (≈ 7 % SoC for a Li-Ion 18650).
    /// </summary>
    public int BatteryWarningVoltageMv { get; set; } = 3400;

    /// <summary>
    /// When true the LED state is not changed while the Windows session is locked.
    /// The current command is re-sent on unlock.
    /// </summary>
    public bool KeepLedsOnScreenLock { get; set; } = true;
}

/// <summary>
/// LED configuration for a single Teams presence status.
/// </summary>
public class PresenceSettings
{
    /// <summary>
    /// When false this status is excluded from the manual override submenu
    /// and treated as "no command" (LEDs off) if received from Teams.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public byte Brightness { get; set; }

    /// <summary>Animation mode: 0=Static, 1=Pulse, 2=Chase, 3=Rainbow, 4=Blink.</summary>
    public byte Mode { get; set; }

    /// <summary>Animation speed 0–255; higher = faster.</summary>
    public byte Speed { get; set; }
}

/// <summary>
/// A known BLE peripheral.
/// Devices are discovered on first run and persisted here so subsequent
/// launches connect directly without a full re-scan.
/// </summary>
public class BleDeviceSettings
{
    /// <summary>Friendly display name shown in the status window (e.g. "Büro", "Studio").</summary>
    public string Name { get; set; } = "BusyLight";

    /// <summary>
    /// Bluetooth address in colon-separated hex notation: "AA:BB:CC:DD:EE:01".
    /// Written by the auto-discovery flow; may be edited manually.
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Parse the address string to a ulong for the Windows BLE API.</summary>
    public ulong GetAddressAsUlong()
        => Convert.ToUInt64(Address.Replace(":", ""), 16);

    /// <summary>Format a raw BLE address ulong as "AA:BB:CC:DD:EE:FF".</summary>
    public static string FormatAddress(ulong address)
    {
        var hex = address.ToString("X12");
        return $"{hex[0..2]}:{hex[2..4]}:{hex[4..6]}:{hex[6..8]}:{hex[8..10]}:{hex[10..12]}";
    }
}
