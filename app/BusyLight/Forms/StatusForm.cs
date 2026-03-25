using BusyLight.Models;

namespace BusyLight.Forms;

/// <summary>
/// Status window showing all connected BLE devices and the current Teams presence.
/// Hides instead of closing so it can be re-opened from the tray at any time.
///
/// Public update methods are thread-safe — they can be called from any thread.
/// Visual layout is done in the VS WinForms Designer (StatusForm.Designer.cs).
/// </summary>
public partial class StatusForm : Form
{
    // ── Per-device label tracking ─────────────────────────────────────────────

    // Key = deviceName; Value = the status label for that device row
    private readonly Dictionary<string, Label> _deviceStatusLabels = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    public StatusForm()
    {
        InitializeComponent();
    }

    // ── Public update API (thread-safe) ───────────────────────────────────────

    /// <summary>
    /// Create or update the status row for a single BLE device.
    /// A new row is added to <c>pnlDevices</c> the first time a device key is seen.
    /// Colours: Connected = green, Disconnected = red, Searching = blue.
    /// </summary>
    /// <param name="deviceName">
    /// Unique key that identifies the device (typically its friendly name).
    /// </param>
    /// <param name="state">New connection state.</param>
    public void UpdateBleStatus(string deviceName, BleConnectionState state)
    {
        if (InvokeRequired) { Invoke(() => UpdateBleStatus(deviceName, state)); return; }

        if (!_deviceStatusLabels.TryGetValue(deviceName, out var statusLabel))
            statusLabel = AddDeviceRow(deviceName);

        (statusLabel.Text, statusLabel.ForeColor) = state switch
        {
            BleConnectionState.Connected => ("Connected", Color.Green),
            BleConnectionState.Disconnected => ("Disconnected", Color.Red),
            _ => ("Searching…", Color.Blue),
        };
    }

    /// <summary>Update the Teams presence indicator.</summary>
    public void UpdatePresence(string presenceKey)
    {
        if (InvokeRequired) { Invoke(() => UpdatePresence(presenceKey)); return; }

        lblPresenceValue.Text = presenceKey;
    }

    /// <summary>Update the battery reading label. Pass null to show "—".</summary>
    public void UpdateBattery(BatteryReading? reading)
    {
        if (InvokeRequired) { Invoke(() => UpdateBattery(reading)); return; }

        lblBattery.Text = reading is null ? "Akku: —" : $"Akku: {reading}";
    }

    // ── Dynamic device rows ───────────────────────────────────────────────────

    /// <summary>
    /// Add a new two-label row (name + status) to <c>pnlDevices</c> and
    /// return the status label so the caller can update its text/colour.
    /// </summary>
    private Label AddDeviceRow(string deviceName)
    {
        var lblName = new Label
        {
            Text = deviceName,
            AutoSize = false,
            Width = 100,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var lblStatus = new Label
        {
            Text = "Searching…",
            ForeColor = Color.Blue,
            AutoSize = false,
            Width = 120,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        pnlDevices.Controls.Add(lblName);
        pnlDevices.Controls.Add(lblStatus);

        _deviceStatusLabels[deviceName] = lblStatus;
        return lblStatus;
    }

    // ── Settings button ───────────────────────────────────────────────────────

    /// <summary>
    /// Raised when the user clicks the Settings button.
    /// TrayApplication subscribes and opens the SettingsForm.
    /// </summary>
    public event EventHandler? SettingsRequested;

    private void btnSettings_Click(object? sender, EventArgs e)
        => SettingsRequested?.Invoke(this, EventArgs.Empty);

    // ── Hide instead of close ─────────────────────────────────────────────────

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}

