namespace BusyLight.Models;

/// <summary>
/// Represents the 6-byte LED command packet sent over BLE.
/// Byte layout: [R, G, B, Brightness, Mode, Speed]
/// </summary>
public sealed class LedCommand : IEquatable<LedCommand>
{
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }
    public byte Brightness { get; }

    /// <summary>Animation mode: 0=Static, 1=Pulse, 2=Chase, 3=Rainbow, 4=Blink.</summary>
    public byte Mode { get; }

    /// <summary>Animation speed 0–255; higher = faster.</summary>
    public byte Speed { get; }

    public LedCommand(byte r, byte g, byte b, byte brightness, byte mode, byte speed)
    {
        R          = r;
        G          = g;
        B          = b;
        Brightness = brightness;
        Mode       = mode;
        Speed      = speed;
    }

    /// <summary>All-off command: turns every LED off.</summary>
    public static readonly LedCommand Off = new(0, 0, 0, 0, 0, 0);

    /// <summary>Serialize the command to the 6-byte BLE payload.</summary>
    public byte[] ToBytes() => [R, G, B, Brightness, Mode, Speed];

    /// <summary>
    /// Build a LedCommand from a <see cref="PresenceSettings"/> configuration entry.
    /// </summary>
    /// <param name="s">Presence settings entry.</param>
    /// <param name="brightnessCap">
    /// Fraction 0.0–1.0 applied to <see cref="PresenceSettings.Brightness"/> before sending.
    /// Defaults to 1.0 (no cap). Use <see cref="PollingSettings.BrightnessCap"/> from
    /// <c>appsettings.json</c> here.
    /// </param>
    public static LedCommand FromPresenceSettings(Models.PresenceSettings s, float brightnessCap = 1.0f)
    {
        var cap     = Math.Clamp(brightnessCap, 0f, 1f);
        var capped  = (byte)Math.Round(s.Brightness * cap);
        return new(s.R, s.G, s.B, capped, s.Mode, s.Speed);
    }

    // ── Equality (used for BLE debounce) ─────────────────────────────────────

    public bool Equals(LedCommand? other)
        => other is not null
        && R == other.R && G == other.G && B == other.B
        && Brightness == other.Brightness
        && Mode == other.Mode && Speed == other.Speed;

    public override bool Equals(object? obj) => Equals(obj as LedCommand);

    public override int GetHashCode()
        => HashCode.Combine(R, G, B, Brightness, Mode, Speed);
}
