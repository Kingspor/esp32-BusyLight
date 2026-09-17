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

    /// <summary>
    /// Parse the 6-byte payload the device reports on its state characteristic
    /// (<c>feda0104-…</c>), which uses the same layout as <see cref="ToBytes"/>.
    /// Returns <c>null</c> for anything that is not exactly six bytes.
    /// </summary>
    public static LedCommand? FromBytes(ReadOnlySpan<byte> bytes)
        => bytes.Length == 6
            ? new(bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5])
            : null;

    /// <summary>Animation mode whose colour bytes the firmware ignores.</summary>
    private const byte RainbowMode = 3;

    /// <summary>
    /// How far two normalised colour channels may differ and still count as the same
    /// colour.  Small on purpose: it absorbs palettes that disagree slightly
    /// (255,170,0 against 255,165,0 for "away") without letting distinct statuses
    /// collide.
    /// </summary>
    private const int ChannelTolerance = 24;

    /// <summary>True when the ring is dark, whatever colour is nominally set.</summary>
    public bool IsDark => Brightness == 0 || (R == 0 && G == 0 && B == 0);

    /// <summary>
    /// True when this command shows the same thing as <paramref name="other"/> to a
    /// person looking at the ring.
    ///
    /// This cannot be a byte comparison.  The two clients do not share a palette — the
    /// phone sends 0,200,0 for "available" where the tray is configured for 0,255,0 —
    /// and brightness is per-client taste anyway: the tray applies
    /// <see cref="PollingSettings.BrightnessCap"/>, the phone its own slider.  Compared
    /// byte for byte, the tray would never once recognise a status the phone set, which
    /// is the whole point of reading the state characteristic.
    /// </summary>
    public bool MatchesAppearance(LedCommand? other)
    {
        if (other is null) return false;

        // A dark ring is a dark ring.  Which colour sits behind brightness 0 makes no
        // difference to anyone looking at it, and the two clients disagree there too:
        // "off" is 0,0,0 on the phone and blue-at-zero-brightness in the tray's config.
        if (IsDark || other.IsDark) return IsDark && other.IsDark;

        if (Mode != other.Mode) return false;

        // Rainbow cycles the spectrum and ignores the colour bytes entirely, so
        // comparing them would reject two rings that look identical.
        if (Mode == RainbowMode) return true;

        var (r1, g1, b1) = NormalisedColour();
        var (r2, g2, b2) = other.NormalisedColour();

        return Math.Abs(r1 - r2) <= ChannelTolerance
            && Math.Abs(g1 - g2) <= ChannelTolerance
            && Math.Abs(b1 - b2) <= ChannelTolerance;
    }

    /// <summary>
    /// The colour scaled so its strongest channel is 255.  That strips intensity and
    /// leaves the hue: 0,200,0 and 0,255,0 both become 0,255,0, which is exactly the
    /// judgement a person makes when they call both of them "green".
    /// </summary>
    private (int R, int G, int B) NormalisedColour()
    {
        int max = Math.Max(R, Math.Max(G, B));
        if (max == 0) return (0, 0, 0);

        return ((int)Math.Round(R * 255.0 / max),
                (int)Math.Round(G * 255.0 / max),
                (int)Math.Round(B * 255.0 / max));
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
