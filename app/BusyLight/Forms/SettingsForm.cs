using BusyLight.Models;
using BusyLight.Services;

namespace BusyLight.Forms;

/// <summary>
/// Combined status monitor and settings editor.
/// Replaces the former StatusForm — double-clicking the tray icon opens this window.
/// Tabs: Allgemein · Präsenz · BLE-Gerät · Zuordnung
/// </summary>
public sealed partial class SettingsForm : Form
{
    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly ConfigurationService _configService;

    // ── Working copy of settings ──────────────────────────────────────────────

    private AppSettings _settings;

    // ── Presence row controls ─────────────────────────────────────────────────

    private readonly record struct PresenceRow(
        Panel         ColorSwatch,
        CheckBox      ChkEnabled,
        NumericUpDown NudBrightness,   // 0–100 % (stored as byte 0–255 in settings)
        ComboBox      CmbMode,
        NumericUpDown NudSpeed,
        Label         LblHz,           // computed animation frequency
        NumericUpDown NudR,
        NumericUpDown NudG,
        NumericUpDown NudB);

    private readonly Dictionary<string, PresenceRow> _presenceRows = new();

    // ── Mapping comboboxes (Teams status → PresenceMap key) ───────────────────

    private readonly Dictionary<string, ComboBox> _mappingCombos = new();

    // ── Dirty tracking & loading flag ────────────────────────────────────────

    private bool _isDirty;
    private bool _loading;
    private bool _syncingColor;

    // ── Masked ID storage (ToDo 1) ────────────────────────────────────────────

    /// <summary>Full (unmasked) ClientId / TenantId kept in memory.</summary>
    private string _clientIdFull = "";
    private string _tenantIdFull = "";

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised on the UI thread after the user clicks "Speichern".</summary>
    public event EventHandler<AppSettings>? SettingsSaved;

    /// <summary>Raised immediately on any presence-parameter change for live preview.</summary>
    public event EventHandler<LedCommand>? LivePreviewCommand;

    /// <summary>Raised when the user clicks "Jetzt abrufen" to trigger an immediate presence poll.</summary>
    public event EventHandler? FetchNowRequested;

    /// <summary>Raised when the user selects a manual override. Arg is the presence key, or null to clear.</summary>
    public event EventHandler<string?>? OverrideRequested;

    // ── Animation mode names (index = Mode byte) ──────────────────────────────

    private static readonly string[] ModeNames =
        ["Static", "Pulse", "Chase", "Rainbow", "Blink", "Fill"];

    // ── Constructor ───────────────────────────────────────────────────────────

    public SettingsForm(AppSettings settings, ConfigurationService configService)
    {
        _settings      = settings;
        _configService = configService;

        InitializeComponent();
        Load += SettingsForm_Load;
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    private void SettingsForm_Load(object? sender, EventArgs e)
    {
        // Size dynamic panels to fill their respective tabs
        // pnlPresence starts at y=72 (toolbar rows above it)
        pnlPresence.Size  = new Size(tabPraesenz.ClientSize.Width  - 12,
                                     tabPraesenz.ClientSize.Height - 72 - 6);
        pnlZuordnung.Size = new Size(tabZuordnung.ClientSize.Width - 12,
                                     tabZuordnung.ClientSize.Height - 34);

        // Brightness cap: keep per-row maximums in sync when the cap NUD changes
        nudBrightnessCap.ValueChanged += (_, _) => UpdateBrightnessMaxima();

        // Live-preview checkbox: turn LEDs off when preview is disabled
        chkLivePreview.CheckedChanged += (_, _) =>
        {
            if (!chkLivePreview.Checked)
                LivePreviewCommand?.Invoke(this, LedCommand.Off);
        };

        // Mask sensitive IDs: full value on focus, masked on blur
        txtClientId.GotFocus  += (_, _) => txtClientId.Text = _clientIdFull;
        txtClientId.LostFocus += (_, _) =>
        {
            _clientIdFull    = txtClientId.Text.Trim();
            txtClientId.Text = MaskId(_clientIdFull);
        };
        txtTenantId.GotFocus  += (_, _) => txtTenantId.Text = _tenantIdFull;
        txtTenantId.LostFocus += (_, _) =>
        {
            _tenantIdFull    = txtTenantId.Text.Trim();
            txtTenantId.Text = MaskId(_tenantIdFull);
        };

        BuildPresenceRows();
        BuildMappingRows();
        LoadToUi(_settings);

        // Wire dirty-tracking for general controls AFTER initial load
        // so loading values doesn't immediately flag the form as dirty.
        nudGraphInterval.ValueChanged += (_, _) => MarkDirty();
        nudBleRetry.ValueChanged      += (_, _) => MarkDirty();
        nudBrightnessCap.ValueChanged += (_, _) => MarkDirty();
        txtClientId.TextChanged       += (_, _) => MarkDirty();
        txtTenantId.TextChanged       += (_, _) => MarkDirty();

        // Präsenz tab toolbar
        btnFetchNow.Click += (_, _) => FetchNowRequested?.Invoke(this, EventArgs.Empty);

        btnApplyOverride.Click += (_, _) =>
        {
            var key = cmbOverride.SelectedItem as string;
            OverrideRequested?.Invoke(this, key == "(keiner)" ? null : key);
        };

        chkKeepOverride.CheckedChanged += (_, _) =>
        {
            _settings.KeepOverrideOnRestart = chkKeepOverride.Checked;
            MarkDirty();
        };
    }

    // ── Public runtime update API (called from TrayApplication) ──────────────

    /// <summary>Update the Teams presence display. Thread-safe.</summary>
    public void UpdatePresence(string presenceKey)
    {
        if (InvokeRequired) { Invoke(() => UpdatePresence(presenceKey)); return; }
        sslTeams.Text        = $"Teams: {presenceKey}";
        lblCurrPresence.Text = presenceKey;
    }

    /// <summary>Update the BLE connection status display. Thread-safe.</summary>
    public void UpdateBleStatus(string deviceName, BleConnectionState state)
    {
        if (InvokeRequired) { Invoke(() => UpdateBleStatus(deviceName, state)); return; }

        var (stateText, stateColor) = state switch
        {
            BleConnectionState.Connected    => ("Verbunden",  Color.Green),
            BleConnectionState.Disconnected => ("Getrennt",   Color.Red),
            _                               => ("Suche…",     Color.Blue),
        };

        sslBle.Text      = $"{deviceName} — {stateText}";
        sslBle.ForeColor = stateColor;

        // BLE tab: show status inline with device name
        var addr = _settings.BleDevice?.Address ?? "";
        lblBleDevice.Text = string.IsNullOrEmpty(addr)
            ? $"{deviceName}  —  {stateText}"
            : $"{deviceName}  ({addr})  —  {stateText}";
    }

    /// <summary>
    /// Populate the override combo and reflect the current active override.
    /// Called by TrayApplication whenever the presence map or active override changes.
    /// </summary>
    public void SyncOverrideUi(IEnumerable<string> enabledKeys, string? activeOverride, bool keepOnRestart)
    {
        if (InvokeRequired) { Invoke(() => SyncOverrideUi(enabledKeys, activeOverride, keepOnRestart)); return; }

        cmbOverride.Items.Clear();
        cmbOverride.Items.Add("(keiner)");
        foreach (var k in enabledKeys)
            cmbOverride.Items.Add(k);

        var idx = activeOverride is not null ? cmbOverride.Items.IndexOf(activeOverride) : 0;
        cmbOverride.SelectedIndex = Math.Max(idx, 0);

        chkKeepOverride.Checked = keepOnRestart;

        // Show active override in label
        lblActiveOverride.Text = activeOverride is null
            ? "Kein Override aktiv"
            : $"Override: {activeOverride}";
        lblActiveOverride.ForeColor = activeOverride is null
            ? SystemColors.GrayText
            : Color.DarkOrange;
    }

    // ── ToDo 1 helper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Show only the first 7 characters of an ID, followed by "****".
    /// Empty or short strings are returned as-is.
    /// </summary>
    private static string MaskId(string value)
        => value.Length > 7 ? value[..7] + "****" : value;

    // ── Menu handlers ─────────────────────────────────────────────────────────

    private void mnuLaden_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title            = "appsettings.json laden",
            Filter           = "JSON-Dateien (*.json)|*.json|Alle Dateien (*.*)|*.*",
            InitialDirectory = ConfigurationService.GetConfigDirectory(),
            FileName         = "appsettings.json",
        };

        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var json   = File.ReadAllText(ofd.FileName);
            var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
                             json,
                             new System.Text.Json.JsonSerializerOptions
                             { PropertyNameCaseInsensitive = true }) ?? new AppSettings();
            _settings = loaded;
            RebuildPresenceRows();
            RebuildMappingRows();
            LoadToUi(_settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Fehler beim Laden:\n{ex.Message}",
                            "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void mnuSpeichern_Click(object? sender, EventArgs e)
    {
        ReadFromUi();

        _ = Task.Run(async () =>
        {
            try
            {
                await _configService.SaveAsync(_settings).ConfigureAwait(false);

                Invoke(() =>
                {
                    ClearDirty();
                    SettingsSaved?.Invoke(this, _settings);
                    MessageBox.Show(this, "Einstellungen gespeichert.",
                        "Gespeichert", MessageBoxButtons.OK, MessageBoxIcon.Information);
                });
            }
            catch (Exception ex)
            {
                Invoke(() => MessageBox.Show(this, $"Fehler beim Speichern:\n{ex.Message}",
                    "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error));
            }
        });
    }

    private void mnuSchliessen_Click(object? sender, EventArgs e) => Close();

    // ── BLE tab ───────────────────────────────────────────────────────────────

    private void btnRescan_Click(object? sender, EventArgs e)
    {
        using var dlg = new BlePickerForm();
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedDevice is { } dev)
        {
            _settings.BleDevice = dev;
            // Status will update via UpdateBleStatus once the new service connects
            lblBleDevice.Text = $"{dev.Name}  ({dev.Address})  —  Suche…";
            MarkDirty();
        }
    }

    // ── Load settings → UI ────────────────────────────────────────────────────

    private void LoadToUi(AppSettings s)
    {
        _loading = true;
        try
        {
            // store full values, display masked
            _clientIdFull    = s.AzureAd.ClientId;
            _tenantIdFull    = s.AzureAd.TenantId;
            txtClientId.Text = MaskId(_clientIdFull);
            txtTenantId.Text = MaskId(_tenantIdFull);

            // Polling
            nudGraphInterval.Value = Math.Clamp(s.Polling.GraphIntervalSeconds,    5,    3600);
            nudBleRetry.Value      = Math.Clamp(s.Polling.BleRetryIntervalSeconds, 2,     300);
            nudBrightnessCap.Value = Math.Clamp((int)(s.Polling.BrightnessCap * 100f), 0, 100);

            // Presence rows — byte 0–255 → percent 0–100
            foreach (var (key, ps) in s.PresenceMap)
            {
                if (!_presenceRows.TryGetValue(key, out var row)) continue;

                row.ColorSwatch.BackColor = Color.FromArgb(ps.R, ps.G, ps.B);
                row.NudR.Value            = ps.R;
                row.NudG.Value            = ps.G;
                row.NudB.Value            = ps.B;
                row.ChkEnabled.Checked    = ps.Enabled;
                row.NudBrightness.Value   = ByteToPercent(ps.Brightness);
                row.CmbMode.SelectedIndex = Math.Clamp(ps.Mode, 0, ModeNames.Length - 1);
                row.NudSpeed.Value        = ps.Speed;
                row.NudSpeed.Enabled      = ps.Mode != 0;
            }

            // BLE device (connection status will be updated live via UpdateBleStatus)
            lblBleDevice.Text = s.BleDevice is { } dev
                ? $"{dev.Name}  ({dev.Address})  —  Suche…"
                : "Kein Gerät ausgewählt";

            // Override keep-on-restart
            chkKeepOverride.Checked = s.KeepOverrideOnRestart;

            // Presence mapping
            LoadMappingToUi(s);

            // Apply brightness cap and compute initial Hz labels
            UpdateBrightnessMaxima();
        }
        finally
        {
            _loading = false;
        }
    }

    private void LoadMappingToUi(AppSettings s)
    {
        foreach (var (teamsKey, cmb) in _mappingCombos)
        {
            var target = s.PresenceMapping.TryGetValue(teamsKey, out var mapped)
                ? mapped
                : teamsKey;

            var idx = cmb.Items.IndexOf(target);
            cmb.SelectedIndex = idx >= 0 ? idx : 0;
        }
    }

    // ── Read UI → settings ────────────────────────────────────────────────────

    private void ReadFromUi()
    {
        // ToDo 1 — read from stored full values, not the (possibly masked) TextBox
        _settings.AzureAd.ClientId = _clientIdFull;
        _settings.AzureAd.TenantId = _tenantIdFull;

        _settings.Polling.GraphIntervalSeconds    = (int)nudGraphInterval.Value;
        _settings.Polling.BleRetryIntervalSeconds = (int)nudBleRetry.Value;
        _settings.Polling.BrightnessCap           = (float)nudBrightnessCap.Value / 100f;

        // Presence map — ToDo 3: percent 0–100 → byte 0–255
        foreach (var (key, row) in _presenceRows)
        {
            if (!_settings.PresenceMap.TryGetValue(key, out var ps))
            {
                ps = new PresenceSettings();
                _settings.PresenceMap[key] = ps;
            }

            var c     = row.ColorSwatch.BackColor;
            ps.R      = c.R;
            ps.G      = c.G;
            ps.B      = c.B;
            ps.Enabled    = row.ChkEnabled.Checked;
            ps.Brightness = PercentToByte(row.NudBrightness.Value);
            ps.Mode       = (byte)row.CmbMode.SelectedIndex;
            ps.Speed      = (byte)row.NudSpeed.Value;
        }

        // BleDevice is updated in-place when the user picks via btnRescan — nothing to read here.

        // Presence mapping
        _settings.PresenceMapping.Clear();
        foreach (var (teamsKey, cmb) in _mappingCombos)
        {
            if (cmb.SelectedItem is string target)
                _settings.PresenceMapping[teamsKey] = target;
        }
    }

    // ── Brightness conversion helpers (ToDo 3) ────────────────────────────────

    /// <summary>Convert LED byte value (0–255) → display percent (0–100).</summary>
    private static decimal ByteToPercent(byte value)
        => (decimal)Math.Round(value / 255.0 * 100.0);

    /// <summary>Convert display percent (0–100) → LED byte value (0–255).</summary>
    private static byte PercentToByte(decimal percent)
        => (byte)Math.Round((double)percent / 100.0 * 255.0);

    // ── Brightness cap (ToDo 12) ───────────────────────────────────────────────

    /// <summary>
    /// Cap each row's NudBrightness.Maximum to the current BrightnessCap %.
    /// Also recalculates all Hz labels (Pulse Hz depends on capped brightness).
    /// </summary>
    private void UpdateBrightnessMaxima()
    {
        var capPercent = nudBrightnessCap.Value;   // 0–100
        foreach (var (_, row) in _presenceRows)
        {
            row.NudBrightness.Maximum = capPercent;
            if (row.NudBrightness.Value > capPercent)
                row.NudBrightness.Value = capPercent;
        }
        UpdateAllHzLabels();
    }

    // ── Hz display (ToDo 11) ──────────────────────────────────────────────────

    private void UpdateAllHzLabels()
    {
        foreach (var key in _presenceRows.Keys)
            UpdateHzLabel(key);
    }

    private void UpdateHzLabel(string key)
    {
        if (!_presenceRows.TryGetValue(key, out var row)) return;
        row.LblHz.Text = ComputeHz(
            row.CmbMode.SelectedIndex,
            (int)row.NudSpeed.Value,
            (double)nudBrightnessCap.Value,
            (double)row.NudBrightness.Value);
    }

    /// <summary>
    /// Compute human-readable cycle frequency for each animation mode.
    /// Mirrors the firmware's map() timing formulas.
    /// </summary>
    private static string ComputeHz(int mode, int speed, double capPercent, double brightPercent)
    {
        // Equivalent to Arduino map() but in double precision
        static double ArdMap(double v, double f1, double f2, double t1, double t2)
            => t1 + (v - f1) * (t2 - t1) / (f2 - f1);

        double periodMs;
        switch (mode)
        {
            case 1: // Pulse: ramp up + ramp down = 2 × cappedSteps × stepMs
            {
                double cappedSteps = Math.Round(brightPercent / 100.0 * 255.0 * capPercent / 100.0);
                if (cappedSteps < 1.0) return "–";
                periodMs = 2.0 * cappedSteps * ArdMap(speed, 0, 255, 30, 2);
                break;
            }
            case 2: // Chase: 6 ring LEDs × stepMs
                periodMs = 6.0 * ArdMap(speed, 0, 255, 500, 20);
                break;
            case 3: // Rainbow: 256 hue steps × stepMs
                periodMs = 256.0 * ArdMap(speed, 0, 255, 30, 2);
                break;
            case 4: // Blink: 2 half-periods
                periodMs = 2.0 * ArdMap(speed, 0, 255, 1000, 100);
                break;
            case 5: // Fill: 12 steps (6 fill + 6 empty) × stepMs
                periodMs = 12.0 * ArdMap(speed, 0, 255, 500, 20);
                break;
            default: // Static: no animation
                return "–";
        }

        double hz = 1000.0 / periodMs;
        return hz >= 10.0 ? $"{hz:F0} Hz" : $"{hz:F2} Hz";
    }

    // ── Presence rows ─────────────────────────────────────────────────────────

    private void BuildPresenceRows()
    {
        pnlPresence.SuspendLayout();
        pnlPresence.Controls.Clear();
        _presenceRows.Clear();

        AddPresenceHeader();
        int y = 28;
        foreach (var key in _settings.PresenceMap.Keys)
        {
            AddPresenceRow(key, y);
            y += 34;
        }

        pnlPresence.ResumeLayout();
    }

    private void RebuildPresenceRows()
    {
        pnlPresence.Controls.Clear();
        _presenceRows.Clear();
        AddPresenceHeader();

        int y = 28;
        foreach (var key in _settings.PresenceMap.Keys)
        {
            AddPresenceRow(key, y);
            y += 34;
        }
    }

    // Column X positions
    private const int ColStatus     =   4;   // w=144
    private const int ColEnabled    = 150;   // w=24
    private const int ColColor      = 178;   // swatch w=28
    private const int ColPickBtn    = 210;   // btn w=20
    private const int ColR          = 234;   // NUD w=44
    private const int ColG          = 282;   // NUD w=44
    private const int ColB          = 330;   // NUD w=44
    private const int ColBrightness = 378;   // NUD w=64
    private const int ColMode       = 446;   // ComboBox w=90
    private const int ColSpeed      = 540;   // NUD w=64
    private const int ColHz         = 608;   // Label w=65

    private void AddPresenceHeader()
    {
        void Hdr(string text, int x, int w)
        {
            pnlPresence.Controls.Add(new Label
            {
                Text      = text,
                AutoSize  = false,
                Width     = w,
                Height    = 20,
                Location  = new Point(x, 4),
                Font      = new Font(Font, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
            });
        }

        Hdr("Status",   ColStatus,     144);
        Hdr("Aktiv",    ColEnabled,     43);
        Hdr("Farbe",    ColColor,       48);
        Hdr("R",        ColR,           44);
        Hdr("G",        ColG,           44);
        Hdr("B",        ColB,           44);
        Hdr("Hell.%",   ColBrightness,  64);
        Hdr("Modus",    ColMode,        90);
        Hdr("Geschw.",  ColSpeed,       64);
        Hdr("~Hz",      ColHz,          65);
    }

    private void AddPresenceRow(string key, int y)
    {
        var lblKey = new Label
        {
            Text      = key,
            AutoSize  = false,
            Width     = 144,
            Height    = 28,
            Location  = new Point(ColStatus, y),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var chk = new CheckBox
        {
            Width    = 24,
            Height   = 28,
            Location = new Point(ColEnabled + 10, y + 2),
        };

        var swatch = new Panel
        {
            Width       = 28,
            Height      = 24,
            Location    = new Point(ColColor, y + 2),
            BorderStyle = BorderStyle.FixedSingle,
            Cursor      = Cursors.Hand,
            BackColor   = Color.Gray,
        };
        swatch.Click += (_, _) => OpenColorPicker(key, swatch);

        var btnPick = new Button
        {
            Text     = "…",
            Width    = 20,
            Height   = 24,
            Location = new Point(ColPickBtn, y + 2),
        };
        btnPick.Click += (_, _) => OpenColorPicker(key, swatch);

        // R / G / B manual input (0–255)
        var nudR = new NumericUpDown { Minimum = 0, Maximum = 255, Width = 44, Height = 24, Location = new Point(ColR, y + 3) };
        var nudG = new NumericUpDown { Minimum = 0, Maximum = 255, Width = 44, Height = 24, Location = new Point(ColG, y + 3) };
        var nudB = new NumericUpDown { Minimum = 0, Maximum = 255, Width = 44, Height = 24, Location = new Point(ColB, y + 3) };

        // Brightness (0–100 %)
        var nudBrt = new NumericUpDown
        {
            Minimum  = 0,
            Maximum  = 100,
            Width    = 64,
            Height   = 24,
            Location = new Point(ColBrightness, y + 3),
        };

        var cmb = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width         = 90,
            Height        = 24,
            Location      = new Point(ColMode, y + 3),
        };
        cmb.Items.AddRange(ModeNames);
        cmb.SelectedIndex = 0;

        var nudS = new NumericUpDown
        {
            Minimum  = 0,
            Maximum  = 255,
            Width    = 64,
            Height   = 24,
            Location = new Point(ColSpeed, y + 3),
        };
        nudS.Enabled = false; // Static is default

        var lblHz = new Label
        {
            AutoSize  = false,
            Width     = 65,
            Height    = 24,
            Location  = new Point(ColHz, y + 3),
            TextAlign = ContentAlignment.MiddleLeft,
            Text      = "–",
        };

        // ── R/G/B ↔ swatch synchronisation ───────────────────────────────────
        void SyncSwatchFromNuds()
        {
            if (_syncingColor || _loading) return;
            swatch.BackColor = Color.FromArgb((int)nudR.Value, (int)nudG.Value, (int)nudB.Value);
            MarkDirty();
            FireLivePreview(key);
        }

        nudR.ValueChanged += (_, _) => SyncSwatchFromNuds();
        nudG.ValueChanged += (_, _) => SyncSwatchFromNuds();
        nudB.ValueChanged += (_, _) => SyncSwatchFromNuds();

        // ── Live preview + Hz + dirty on every change ─────────────────────────
        nudBrt.ValueChanged += (_, _) => { MarkDirty(); UpdateHzLabel(key); FireLivePreview(key); };
        nudS.ValueChanged   += (_, _) => { MarkDirty(); UpdateHzLabel(key); FireLivePreview(key); };
        chk.CheckedChanged  += (_, _) => MarkDirty();

        cmb.SelectedIndexChanged += (_, _) =>
        {
            nudS.Enabled = cmb.SelectedIndex != 0;
            MarkDirty();
            UpdateHzLabel(key);
            FireLivePreview(key);
        };

        pnlPresence.Controls.AddRange(new Control[]
        {
            lblKey, chk, swatch, btnPick, nudR, nudG, nudB, nudBrt, cmb, nudS, lblHz
        });

        _presenceRows[key] = new PresenceRow(swatch, chk, nudBrt, cmb, nudS, lblHz, nudR, nudG, nudB);
    }

    // ── Colour picker ─────────────────────────────────────────────────────────

    private void OpenColorPicker(string key, Panel swatch)
    {
        using var dlg = new ColorWheelForm(swatch.BackColor);
        var originalColor = swatch.BackColor;

        // Live preview while dragging in the color wheel (before clicking OK)
        dlg.ColorChanged += (_, color) =>
        {
            swatch.BackColor = color;
            SyncNudsFromSwatch(key);
            FireLivePreview(key);
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            // Revert swatch and LED to the original colour on cancel
            swatch.BackColor = originalColor;
            SyncNudsFromSwatch(key);
            FireLivePreview(key);
            return;
        }

        swatch.BackColor = dlg.SelectedColor;
        SyncNudsFromSwatch(key);
        MarkDirty();

        if (_settings.PresenceMap.TryGetValue(key, out var ps))
        {
            ps.R = dlg.SelectedColor.R;
            ps.G = dlg.SelectedColor.G;
            ps.B = dlg.SelectedColor.B;
        }

        FireLivePreview(key);
    }

    // ── Live preview ──────────────────────────────────────────────────────────

    private void FireLivePreview(string key)
    {
        if (_loading) return;
        if (!chkLivePreview.Checked) return;
        if (!_presenceRows.TryGetValue(key, out var row)) return;
        if (LivePreviewCommand is null) return;

        var c  = row.ColorSwatch.BackColor;
        var ps = new PresenceSettings
        {
            R          = c.R,
            G          = c.G,
            B          = c.B,
            Brightness = PercentToByte(row.NudBrightness.Value), // ToDo 3
            Mode       = (byte)row.CmbMode.SelectedIndex,
            Speed      = (byte)row.NudSpeed.Value,
            Enabled    = true,
        };

        var cap = (float)nudBrightnessCap.Value / 100f;
        LivePreviewCommand.Invoke(this, LedCommand.FromPresenceSettings(ps, cap));
    }

    // ── Mapping rows ──────────────────────────────────────────────────────────

    private void BuildMappingRows()
    {
        pnlZuordnung.SuspendLayout();
        pnlZuordnung.Controls.Clear();
        _mappingCombos.Clear();

        AddMappingHeader();
        int y = 28;
        foreach (var key in _settings.PresenceMap.Keys)
        {
            AddMappingRow(key, y);
            y += 34;
        }

        pnlZuordnung.ResumeLayout();
    }

    private void RebuildMappingRows()
    {
        pnlZuordnung.Controls.Clear();
        _mappingCombos.Clear();
        AddMappingHeader();

        int y = 28;
        foreach (var key in _settings.PresenceMap.Keys)
        {
            AddMappingRow(key, y);
            y += 34;
        }
    }

    private void AddMappingHeader()
    {
        void Hdr(string text, int x, int w)
        {
            pnlZuordnung.Controls.Add(new Label
            {
                Text      = text,
                AutoSize  = false,
                Width     = w,
                Height    = 20,
                Location  = new Point(x, 4),
                Font      = new Font(Font, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
            });
        }

        Hdr("Teams-Status",     4, 180);
        Hdr("BusyLight-Profil", 192, 200);
    }

    private void AddMappingRow(string teamsKey, int y)
    {
        pnlZuordnung.Controls.Add(new Label
        {
            Text      = teamsKey,
            AutoSize  = false,
            Width     = 180,
            Height    = 28,
            Location  = new Point(4, y),
            TextAlign = ContentAlignment.MiddleLeft,
        });

        var cmb = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width         = 200,
            Height        = 24,
            Location      = new Point(192, y + 3),
        };

        foreach (var profileKey in _settings.PresenceMap.Keys)
            cmb.Items.Add(profileKey);

        cmb.SelectedIndex = 0;
        cmb.SelectedIndexChanged += (_, _) => MarkDirty();
        pnlZuordnung.Controls.Add(cmb);
        _mappingCombos[teamsKey] = cmb;
    }

    // ── Dirty tracking helpers ────────────────────────────────────────────────

    private void MarkDirty()
    {
        if (_loading || _isDirty) return;
        _isDirty = true;
        Text = "BusyLight — Einstellungen *";
    }

    private void ClearDirty()
    {
        _isDirty = false;
        Text = "BusyLight — Einstellungen";
    }

    // ── RGB ↔ swatch sync (called from OpenColorPicker) ───────────────────────

    /// <summary>Copy swatch colour into the row's R/G/B NUDs without triggering a loop.</summary>
    private void SyncNudsFromSwatch(string key)
    {
        if (!_presenceRows.TryGetValue(key, out var row)) return;
        _syncingColor = true;
        var c = row.ColorSwatch.BackColor;
        row.NudR.Value = c.R;
        row.NudG.Value = c.G;
        row.NudB.Value = c.B;
        _syncingColor = false;
    }

    // ── Hide instead of dispose ───────────────────────────────────────────────

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            if (_isDirty)
            {
                var result = MessageBox.Show(this,
                    "Es gibt ungespeicherte Änderungen. Jetzt speichern?",
                    "Ungespeicherte Änderungen",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    base.OnFormClosing(e);
                    return;
                }

                if (result == DialogResult.Yes)
                {
                    ReadFromUi();
                    _ = Task.Run(async () =>
                    {
                        try { await _configService.SaveAsync(_settings).ConfigureAwait(false); }
                        catch { /* ignore save errors on close */ }
                    });
                    ClearDirty();
                    SettingsSaved?.Invoke(this, _settings);
                }
                // DialogResult.No → discard changes, just hide
            }

            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}
