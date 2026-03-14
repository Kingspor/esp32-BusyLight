using System.Text.Json;
using System.Text.Json.Serialization;
using BusyLight.Models;

namespace BusyLight.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> from/to
/// %AppData%\BusyLight\appsettings.json.
/// Creates the file with sensible defaults on first launch so the user
/// only needs to fill in ClientId and TenantId.
/// </summary>
public sealed class ConfigurationService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private static readonly string ConfigDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "BusyLight");

    private static readonly string ConfigFilePath =
        Path.Combine(ConfigDirectory, "appsettings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented             = true,
        DefaultIgnoreCondition    = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = true,
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Load settings from disk. If the file does not exist the default
    /// configuration is written and returned.
    /// </summary>
    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(ConfigFilePath))
        {
            var defaults = BuildDefaults();
            await SaveAsync(defaults).ConfigureAwait(false);
            return defaults;
        }

        try
        {
            await using var stream = File.OpenRead(ConfigFilePath);
            var settings = await JsonSerializer
                .DeserializeAsync<AppSettings>(stream, JsonOptions)
                .ConfigureAwait(false);

            return settings ?? BuildDefaults();
        }
        catch (Exception ex)
        {
            // Return defaults on any parse error so the app can still start
            Debug.WriteLine($"[Config] Failed to load settings: {ex.Message}");
            return BuildDefaults();
        }
    }

    /// <summary>Persist <paramref name="settings"/> to disk.</summary>
    public async Task SaveAsync(AppSettings settings)
    {
        Directory.CreateDirectory(ConfigDirectory);

        await using var stream = File.Create(ConfigFilePath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions)
                            .ConfigureAwait(false);
    }

    /// <summary>Returns the directory that contains appsettings.json.</summary>
    public static string GetConfigDirectory() => ConfigDirectory;

    // ── Default configuration ─────────────────────────────────────────────────

    private static AppSettings BuildDefaults() => new()
    {
        AzureAd = new AzureAdSettings
        {
            ClientId = string.Empty,
            TenantId = string.Empty,
        },
        Polling = new PollingSettings
        {
            GraphIntervalSeconds    = 30,
            BleRetryIntervalSeconds = 10,
            BrightnessCap           = 0.6f,
        },
        BleDevice = null,   // null → device picker dialog on first launch
        PresenceMap = new Dictionary<string, PresenceSettings>
        {
            ["Available"] = new()
            {
                Enabled    = true,
                R          = 0,
                G          = 255,
                B          = 0,
                Brightness = 180,
                Mode       = 0,
                Speed      = 0,
            },
            ["Busy"] = new()
            {
                Enabled    = true,
                R          = 255,
                G          = 0,
                B          = 0,
                Brightness = 180,
                Mode       = 0,
                Speed      = 0,
            },
            ["DoNotDisturb"] = new()
            {
                Enabled    = true,
                R          = 255,
                G          = 0,
                B          = 0,
                Brightness = 180,
                Mode       = 4,
                Speed      = 80,
            },
            ["Away"] = new()
            {
                Enabled    = true,
                R          = 255,
                G          = 165,
                B          = 0,
                Brightness = 150,
                Mode       = 1,
                Speed      = 60,
            },
            ["BeRightBack"] = new()
            {
                Enabled    = true,
                R          = 255,
                G          = 165,
                B          = 0,
                Brightness = 150,
                Mode       = 4,
                Speed      = 40,
            },
            ["Offline"] = new()
            {
                Enabled    = false,
                R          = 0,
                G          = 0,
                B          = 255,
                Brightness = 100,
                Mode       = 0,
                Speed      = 0,
            },
            ["PresenceUnknown"] = new()
            {
                Enabled    = false,
                R          = 128,
                G          = 128,
                B          = 128,
                Brightness = 100,
                Mode       = 0,
                Speed      = 0,
            },
        },
        // Default presence mapping: every Teams status maps to itself
        PresenceMapping = new Dictionary<string, string>
        {
            ["Available"]       = "Available",
            ["Busy"]            = "Busy",
            ["DoNotDisturb"]    = "DoNotDisturb",
            ["Away"]            = "Away",
            ["BeRightBack"]     = "BeRightBack",
            ["Offline"]         = "Offline",
            ["PresenceUnknown"] = "PresenceUnknown",
        },
    };
}
