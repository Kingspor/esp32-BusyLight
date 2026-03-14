using BusyLight.Models;
using BusyLight.Services;

namespace BusyLight.Forms;

/// <summary>
/// Modal dialog for selecting a BusyLight BLE peripheral.
/// Scans for nearby devices, shows them in a list, and lets the user pick one.
/// Can be retried and cancelled.
/// </summary>
public sealed partial class BlePickerForm : Form
{
    // ── Result ────────────────────────────────────────────────────────────────

    /// <summary>The device the user selected, or null if cancelled.</summary>
    public BleDeviceSettings? SelectedDevice { get; private set; }

    // ── Private state ─────────────────────────────────────────────────────────

    private CancellationTokenSource? _cts;
    private readonly List<BleDeviceSettings> _devices = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    public BlePickerForm()
    {
        InitializeComponent();
        Load += BlePickerForm_Load;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void BlePickerForm_Load(object? sender, EventArgs e)
    {
        StartScan();
    }

    // ── Scan logic ────────────────────────────────────────────────────────────

    private void StartScan()
    {
        _devices.Clear();
        lstDevices.Items.Clear();
        btnSelect.Enabled = false;
        btnRetry.Enabled  = false;
        lblStatus.Text    = "Suche nach BusyLight-Geräten…";
        progressBar.Visible = true;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        _ = Task.Run(() => RunScanAsync(_cts.Token));
    }

    private async Task RunScanAsync(CancellationToken ct)
    {
        try
        {
            var found = await BleService
                .DiscoverAsync(TimeSpan.FromSeconds(8), ct)
                .ConfigureAwait(false);

            Invoke(() =>
            {
                progressBar.Visible = false;
                btnRetry.Enabled    = true;
                _devices.Clear();
                _devices.AddRange(found);
                lstDevices.Items.Clear();

                if (found.Count == 0)
                {
                    lblStatus.Text = "Keine BusyLight-Geräte gefunden.";
                }
                else
                {
                    lblStatus.Text = $"{found.Count} Gerät(e) gefunden — bitte auswählen:";
                    foreach (var dev in found)
                        lstDevices.Items.Add($"{dev.Name}  ({dev.Address})");
                    lstDevices.SelectedIndex = 0;
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Cancelled via Retry or Cancel button — handled there
            if (!IsDisposed)
            {
                Invoke(() =>
                {
                    progressBar.Visible = false;
                    btnRetry.Enabled    = true;
                });
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                Invoke(() =>
                {
                    progressBar.Visible = false;
                    btnRetry.Enabled    = true;
                    lblStatus.Text      = $"Fehler bei der Suche: {ex.Message}";
                });
            }
        }
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void btnRetry_Click(object? sender, EventArgs e)
    {
        StartScan();
    }

    private void btnSelect_Click(object? sender, EventArgs e)
    {
        int idx = lstDevices.SelectedIndex;
        if (idx < 0 || idx >= _devices.Count) return;

        SelectedDevice = _devices[idx];
        DialogResult   = DialogResult.OK;
        Close();
    }

    private void btnCancel_Click(object? sender, EventArgs e)
    {
        _cts?.Cancel();
        SelectedDevice = null;
        DialogResult   = DialogResult.Cancel;
        Close();
    }

    private void lstDevices_SelectedIndexChanged(object? sender, EventArgs e)
    {
        btnSelect.Enabled = lstDevices.SelectedIndex >= 0;
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts?.Dispose();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }
}
