using BusyLight.Forms;
using BusyLight.Helpers;
using BusyLight.Models;
using BusyLight.Services;

namespace BusyLight;

/// <summary>
/// Root application context.  Owns the system-tray icon, context menu,
/// and all background services (Graph polling + BLE clients).
/// No main window is created — the application lives entirely in the tray.
/// </summary>
public sealed class TrayApplication : ApplicationContext
{
    // ── Services ──────────────────────────────────────────────────────────────

    private readonly ConfigurationService _configService = new();
    private GraphService? _graphService;
    private BleService?   _bleService;
    private AppSettings   _settings = new();

    // ── UI thread synchronisation ─────────────────────────────────────────────

    // Captured in the constructor which always runs on the UI thread.
    private readonly SynchronizationContext _uiContext;

    // ── Combined status/settings window ──────────────────────────────────────

    private SettingsForm? _settingsForm;

    // ── Tray UI ───────────────────────────────────────────────────────────────

    private readonly NotifyIcon       _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;

    // Items that need to be updated at runtime
    private ToolStripMenuItem? _statusItem;
    private ToolStripMenuItem? _overrideMenu;
    private ToolStripMenuItem? _clearOverrideItem;
    private ToolStripMenuItem? _autostartItem;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private string  _currentPresence = "PresenceUnknown";
    private string? _activeOverride;   // null = follow Teams; non-null = manual override key

    // ── Constructor ───────────────────────────────────────────────────────────

    public TrayApplication()
    {
        // Must be captured here — the constructor runs on the UI message-loop thread.
        _uiContext = SynchronizationContext.Current
                     ?? new WindowsFormsSynchronizationContext();

        _contextMenu = new ContextMenuStrip();
        _notifyIcon  = new NotifyIcon
        {
            Visible          = true,
            Text             = "BusyLight — starting…",
            ContextMenuStrip = _contextMenu,
            Icon             = CreatePresenceIcon(Color.Gray),
        };

        // Double-click opens the combined status/settings window
        _notifyIcon.DoubleClick += (_, _) => ShowSettingsForm();

        // Kick off async initialisation (load config, authenticate, start services)
        _ = Task.Run(InitialiseAsync);
    }

    // ── Async initialisation ──────────────────────────────────────────────────

    private async Task InitialiseAsync()
    {
        try
        {
            _settings = await _configService.LoadAsync().ConfigureAwait(false);

            // Restore persisted override (only when user opted in)
            if (_settings.KeepOverrideOnRestart && _settings.ActiveOverride is not null)
                _activeOverride = _settings.ActiveOverride;

            // Validate Azure AD configuration
            if (string.IsNullOrWhiteSpace(_settings.AzureAd.ClientId))
            {
                InvokeOnUiThread(() =>
                {
                    _notifyIcon.ShowBalloonTip(6000, "BusyLight — Setup required",
                        $"Please fill in ClientId and TenantId in:\n" +
                        $"{ConfigurationService.GetConfigDirectory()}\\appsettings.json",
                        ToolTipIcon.Warning);
                });
            }

            // Build context menu now that settings are loaded
            InvokeOnUiThread(BuildContextMenu);

            // ── BLE device setup ──────────────────────────────────────────────

            if (_settings.BleDevice is null)
            {
                // No device selected yet → show picker dialog on the UI thread
                var picked = await ShowBlePickerAsync().ConfigureAwait(false);

                if (picked is null)
                {
                    InvokeOnUiThread(() =>
                        _notifyIcon.ShowBalloonTip(5000, "BusyLight",
                            "Kein Gerät ausgewählt. Scan kann über Einstellungen erneut gestartet werden.",
                            ToolTipIcon.Info));
                }
                else
                {
                    _settings.BleDevice = picked;
                    await _configService.SaveAsync(_settings).ConfigureAwait(false);
                    Debug.WriteLine($"[TrayApp] Device selected: {picked.Name} ({picked.Address})");
                }
            }

            if (_settings.BleDevice is not null)
                StartBleService();

            // ── Graph / Teams setup ───────────────────────────────────────────

            if (!string.IsNullOrWhiteSpace(_settings.AzureAd.ClientId))
            {
                _graphService = new GraphService(_settings);
                _graphService.PresenceChanged += OnPresenceChanged;
                _graphService.ErrorOccurred   += OnServiceError;

                await _graphService.AuthenticateAsync().ConfigureAwait(false);
                _graphService.StartPolling();
            }
        }
        catch (Exception ex)
        {
            ShowError($"Startup error: {ex.Message}");
        }
    }

    // ── BLE service management ────────────────────────────────────────────────

    private void StartBleService()
    {
        if (_settings.BleDevice is null) return;

        ulong? address = string.IsNullOrWhiteSpace(_settings.BleDevice.Address)
            ? null
            : _settings.BleDevice.GetAddressAsUlong();

        _bleService = new BleService(
            _settings.BleDevice.Name, address,
            _settings.Polling.BleRetryIntervalSeconds);
        _bleService.ConnectionChanged += OnBleConnectionChanged;
        _bleService.ErrorOccurred     += OnServiceError;
        _bleService.StartScanning();
    }

    /// <summary>
    /// Show the BlePickerForm on the UI thread and return the chosen device,
    /// or null if the user cancelled.
    /// </summary>
    private Task<BleDeviceSettings?> ShowBlePickerAsync()
    {
        var tcs = new TaskCompletionSource<BleDeviceSettings?>();
        _uiContext.Post(_ =>
        {
            using var dlg = new BlePickerForm();
            var ok = dlg.ShowDialog();
            tcs.SetResult(ok == DialogResult.OK ? dlg.SelectedDevice : null);
        }, null);
        return tcs.Task;
    }

    // ── Context menu ──────────────────────────────────────────────────────────

    private void BuildContextMenu()
    {
        _contextMenu.Items.Clear();

        // Status label (read-only)
        _statusItem = new ToolStripMenuItem($"Status: {_currentPresence}")
        {
            Enabled = false,
        };
        _contextMenu.Items.Add(_statusItem);

        // Open the status/settings window
        var showItem = new ToolStripMenuItem("BusyLight öffnen");
        showItem.Click += (_, _) => ShowSettingsForm();
        _contextMenu.Items.Add(showItem);

        // Fetch Teams presence immediately (clears any active override)
        var fetchNowItem = new ToolStripMenuItem("Status jetzt abrufen");
        fetchNowItem.Click += (_, _) =>
        {
            _activeOverride = null;
            PersistOverrideIfNeeded();
            InvokeOnUiThread(RefreshOverrideUi);
            _ = Task.Run(() => _graphService?.FetchNowAsync());
        };
        _contextMenu.Items.Add(fetchNowItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Manual override submenu — only shows presence entries where Enabled = true
        _overrideMenu = new ToolStripMenuItem("Override");
        foreach (var (key, presence) in _settings.PresenceMap)
        {
            if (!presence.Enabled) continue;

            var label = key;
            var item  = new ToolStripMenuItem(label)
            {
                Checked = _activeOverride == label,
            };
            item.Click += (_, _) => ApplyOverride(label);
            _overrideMenu.DropDownItems.Add(item);
        }
        _contextMenu.Items.Add(_overrideMenu);

        // Clear override (hidden when no override is active)
        _clearOverrideItem = new ToolStripMenuItem("Clear Override");
        _clearOverrideItem.Click   += (_, _) => ClearOverride();
        _clearOverrideItem.Visible  = _activeOverride is not null;
        _contextMenu.Items.Add(_clearOverrideItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Autostart toggle
        _autostartItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = AutostartHelper.IsAutoStartEnabled(),
        };
        _autostartItem.Click += (_, _) =>
        {
            bool newState = !AutostartHelper.IsAutoStartEnabled();
            AutostartHelper.SetAutoStart(newState);
            _autostartItem.Checked = newState;
        };
        _contextMenu.Items.Add(_autostartItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Exit
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();
        _contextMenu.Items.Add(exitItem);
    }

    // ── Override logic ────────────────────────────────────────────────────────

    private void ApplyOverride(string presenceKey)
    {
        _activeOverride = presenceKey;
        PersistOverrideIfNeeded();
        UpdateTrayIcon(presenceKey);
        InvokeOnUiThread(RefreshOverrideUi);
        SendLedCommand(presenceKey);
    }

    private void ClearOverride()
    {
        _activeOverride = null;
        PersistOverrideIfNeeded();
        InvokeOnUiThread(RefreshOverrideUi);
        SendLedCommand(_currentPresence);
        UpdateTrayIcon(_currentPresence);
    }

    private void PersistOverrideIfNeeded()
    {
        if (!_settings.KeepOverrideOnRestart) return;
        _settings.ActiveOverride = _activeOverride;
        _ = Task.Run(() => _configService.SaveAsync(_settings));
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnPresenceChanged(object? sender, string availability)
    {
        _currentPresence = availability;

        InvokeOnUiThread(() =>
        {
            if (_statusItem is not null)
                _statusItem.Text = $"Status: {availability}";

            _settingsForm?.UpdatePresence(availability);
        });

        // Do not change LEDs while an override is active
        if (_activeOverride is null)
        {
            UpdateTrayIcon(availability);
            SendLedCommand(availability);
        }
    }

    private void OnBleConnectionChanged(object? sender, BleConnectionState state)
    {
        var  svc     = sender as BleService;
        string name  = svc?.DeviceName ?? "BusyLight";

        // Show balloon only for connect/disconnect transitions, not for Searching
        string? message = state switch
        {
            BleConnectionState.Connected    => $"{name} connected.",
            BleConnectionState.Disconnected => $"{name} disconnected. Reconnecting…",
            _                               => null,
        };

        InvokeOnUiThread(() =>
        {
            if (message is not null)
                _notifyIcon.ShowBalloonTip(3000, "BusyLight", message,
                    state == BleConnectionState.Connected ? ToolTipIcon.Info : ToolTipIcon.Warning);

            _settingsForm?.UpdateBleStatus(name, state);
        });

        // Re-send the current command after reconnect so the device is in sync
        if (state == BleConnectionState.Connected && svc is not null)
        {
            string key = _activeOverride ?? _currentPresence;
            SendLedCommandTo(svc, key);
        }
    }

    private void OnServiceError(object? sender, string message)
        => ShowError(message);

    // ── LED command dispatch ──────────────────────────────────────────────────

    private void SendLedCommand(string presenceKey)
    {
        if (_bleService is null) return;
        var cmd = BuildCommand(presenceKey);
        _ = Task.Run(() => _bleService.SendCommandAsync(cmd));
    }

    /// <summary>Send a command to a single service (used on reconnect).</summary>
    private void SendLedCommandTo(BleService svc, string presenceKey)
    {
        var cmd = BuildCommand(presenceKey);
        _ = Task.Run(() => svc.SendCommandAsync(cmd));
    }

    private LedCommand BuildCommand(string presenceKey)
    {
        // Apply presence mapping (e.g. DoNotDisturb → Busy)
        var effectiveKey = _settings.PresenceMapping.TryGetValue(presenceKey, out var mapped)
            ? mapped
            : presenceKey;

        return _settings.PresenceMap.TryGetValue(effectiveKey, out var ps) && ps.Enabled
            ? LedCommand.FromPresenceSettings(ps, _settings.Polling.BrightnessCap)
            : LedCommand.Off;
    }

    // ── Tray icon ─────────────────────────────────────────────────────────────

    private void UpdateTrayIcon(string presenceKey)
    {
        var color = PresenceColor(presenceKey);
        var icon  = CreatePresenceIcon(color);
        var tip   = $"BusyLight — {presenceKey}";

        InvokeOnUiThread(() =>
        {
            _notifyIcon.Icon = icon;
            _notifyIcon.Text = tip.Length > 63 ? tip[..63] : tip;  // NotifyIcon tooltip limit
        });
    }

    /// <summary>Create a simple 16×16 filled-circle icon in the given colour.</summary>
    private static Icon CreatePresenceIcon(Color color)
    {
        using var bitmap   = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        graphics.FillEllipse(brush, 1, 1, 13, 13);

        // Thin dark border for legibility on both light and dark taskbars
        using var pen = new Pen(Color.FromArgb(80, 0, 0, 0), 1f);
        graphics.DrawEllipse(pen, 1, 1, 13, 13);

        return Icon.FromHandle(bitmap.GetHicon());
    }

    /// <summary>Map a presence key to a display colour.</summary>
    private static Color PresenceColor(string presenceKey) => presenceKey switch
    {
        "Available"       => Color.FromArgb(0x10, 0xA3, 0x19),  // Teams green
        "Busy"            => Color.FromArgb(0xC4, 0x27, 0x2F),  // Teams red
        "DoNotDisturb"    => Color.FromArgb(0xC4, 0x27, 0x2F),  // Teams red
        "Away"            => Color.FromArgb(0xFF, 0xAA, 0x44),  // Orange
        "BeRightBack"     => Color.FromArgb(0xFF, 0xAA, 0x44),  // Orange
        "Offline"         => Color.FromArgb(0x74, 0x74, 0x74),  // Gray
        "PresenceUnknown" => Color.FromArgb(0x74, 0x74, 0x74),  // Gray
        _                 => Color.Gray,
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Show the combined status/settings window (created lazily on first use).
    /// Must be called on the UI thread.
    /// </summary>
    private void ShowSettingsForm()
    {
        if (_settingsForm is null)
        {
            _settingsForm = new SettingsForm(_settings, _configService);
            _settingsForm.SettingsSaved += (_, newSettings) => ApplyNewSettings(newSettings);

            // Live preview: forward the command to the connected BLE device.
            _settingsForm.LivePreviewCommand += (_, cmd) =>
            {
                if (_bleService is not null)
                    _ = Task.Run(() => _bleService.SendCommandAsync(cmd));
            };

            // Restore the current presence LED state when the window is hidden.
            _settingsForm.VisibleChanged += (_, _) =>
            {
                if (_settingsForm is { Visible: false })
                {
                    string key = _activeOverride ?? _currentPresence;
                    SendLedCommand(key);
                }
            };

            // "Jetzt abrufen" button in Präsenz tab
            _settingsForm.FetchNowRequested += (_, _) =>
            {
                _activeOverride = null;
                InvokeOnUiThread(RefreshOverrideUi);
                _ = Task.Run(() => _graphService?.FetchNowAsync());
            };

            // Override selector in Präsenz tab
            _settingsForm.OverrideRequested += (_, key) =>
            {
                if (key is null)
                    ClearOverride();
                else
                    ApplyOverride(key);
            };

        }

        // Sync override UI every time the window is opened
        SyncSettingsFormOverrideUi();

        // Turn LEDs off while settings are open (live preview takes over).
        if (_bleService is not null)
            _ = Task.Run(() => _bleService.SendCommandAsync(LedCommand.Off));

        _settingsForm.Show();
        _settingsForm.BringToFront();

        // Push live state AFTER Show() so it always runs after LoadToUi().
        // (LoadToUi is called from the Load event which fires inside Show()
        // and would otherwise overwrite whatever was set before Show().)
        _settingsForm.UpdatePresence(_currentPresence);
        if (_bleService is not null)
            _settingsForm.UpdateBleStatus(_bleService.DeviceName, _bleService.CurrentState);
    }

    private void SyncSettingsFormOverrideUi()
    {
        if (_settingsForm is null) return;
        var enabledKeys = _settings.PresenceMap
            .Where(kv => kv.Value.Enabled)
            .Select(kv => kv.Key);
        _settingsForm.SyncOverrideUi(enabledKeys, _activeOverride, _settings.KeepOverrideOnRestart);
    }

    private void RefreshOverrideUi()
    {
        // Update tray menu checkmarks
        if (_overrideMenu is not null)
        {
            foreach (ToolStripMenuItem item in _overrideMenu.DropDownItems)
                item.Checked = item.Text == _activeOverride;
        }
        if (_clearOverrideItem is not null)
            _clearOverrideItem.Visible = _activeOverride is not null;

        SyncSettingsFormOverrideUi();
    }

    /// <summary>
    /// Apply settings saved from the SettingsForm.
    /// Rebuilds the tray context menu so override entries reflect PresenceMap changes.
    /// Only restarts the BLE service when the target device address actually changed —
    /// otherwise the existing connection is kept alive and the current command is re-sent.
    /// </summary>
    private void ApplyNewSettings(AppSettings newSettings)
    {
        var oldAddress = _settings.BleDevice?.Address;
        _settings = newSettings;
        BuildContextMenu();

        bool deviceChanged = _settings.BleDevice?.Address != oldAddress;
        if (deviceChanged)
        {
            if (_bleService is not null)
            {
                _bleService.ConnectionChanged -= OnBleConnectionChanged;
                _bleService.ErrorOccurred     -= OnServiceError;
                _bleService.Stop();
                _bleService.Dispose();
                _bleService = null;
            }
            if (_settings.BleDevice is not null)
                StartBleService();
        }

        // Re-send the current command so LEDs reflect any colour/brightness changes
        string key = _activeOverride ?? _currentPresence;
        SendLedCommand(key);
    }

    private void ShowError(string message)
    {
        Debug.WriteLine($"[TrayApp] Error: {message}");
        InvokeOnUiThread(() =>
        {
            _notifyIcon.ShowBalloonTip(5000, "BusyLight — Error", message, ToolTipIcon.Error);
        });
    }

    /// <summary>
    /// Marshal an action back to the UI thread.
    /// Uses the SynchronizationContext captured on construction — works even
    /// when no form is currently visible.
    /// </summary>
    private void InvokeOnUiThread(Action action)
        => _uiContext.Post(_ => action(), null);

    private void ExitApplication()
    {
        _graphService?.StopPolling();
        _graphService?.Dispose();
        _graphService = null;

        _bleService?.Stop();
        _bleService?.Dispose();
        _bleService = null;

        _settingsForm?.Dispose();
        _settingsForm = null;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        Application.Exit();
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _graphService?.Dispose();
            _bleService?.Dispose();
            _settingsForm?.Dispose();
            _notifyIcon.Dispose();
            _contextMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}
