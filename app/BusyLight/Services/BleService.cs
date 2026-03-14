using BusyLight.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace BusyLight.Services;

/// <summary>
/// Connects to a specific BusyLight BLE peripheral (identified by Bluetooth address)
/// and writes LED command packets to the LED control characteristic.
///
/// Service UUID  : feda0100-51a7-4fb7-a27b-c720bef16ef7
/// LED char UUID : feda0101-51a7-4fb7-a27b-c720bef16ef7  (WRITE | WRITE_NO_RESPONSE)
///
/// Use <see cref="DiscoverAsync"/> to find devices on first run, then pass
/// the discovered address to the constructor for subsequent launches.
/// </summary>
public sealed class BleService : IDisposable
{
    // ── BLE UUIDs (shared with DiscoverAsync) ─────────────────────────────────

    private static readonly Guid ServiceUuid =
        Guid.Parse("feda0100-51a7-4fb7-a27b-c720bef16ef7");

    private static readonly Guid LedCharUuid =
        Guid.Parse("feda0101-51a7-4fb7-a27b-c720bef16ef7");

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the thread pool when the connection state changes.
    /// </summary>
    public event EventHandler<BleConnectionState>? ConnectionChanged;

    /// <summary>
    /// Raised when a non-fatal error occurs so the tray can show a balloon tip.
    /// </summary>
    public event EventHandler<string>? ErrorOccurred;

    // ── Public properties ─────────────────────────────────────────────────────

    /// <summary>Friendly name for balloon tips and the status window.</summary>
    public string DeviceName { get; }

    /// <summary>
    /// Current connection state. Can be read at any time to initialise UI
    /// that is created after the service has already transitioned to Connected.
    /// </summary>
    public BleConnectionState CurrentState { get; private set; } = BleConnectionState.Searching;

    /// <summary>
    /// Bluetooth address of the connected (or target) device.
    /// Null until the first advertisement is received in discovery mode.
    /// </summary>
    public ulong? DeviceAddress { get; private set; }

    // ── Private state ─────────────────────────────────────────────────────────

    private readonly ulong?  _targetAddress;       // null = discovery mode
    private readonly int     _retryIntervalSeconds;

    private BluetoothLEDevice?  _device;
    private GattCharacteristic? _ledChar;
    private LedCommand?         _lastSentCommand;

    private BluetoothLEAdvertisementWatcher? _watcher;
    private CancellationTokenSource?         _cts;

    private bool _connected;
    private bool _addressKnown;  // true when DeviceAddress has been set

    /// <summary>Prevents concurrent connection attempts.</summary>
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Create a BleService instance.
    /// </summary>
    /// <param name="deviceName">
    /// Friendly name used in notifications and the status window.
    /// </param>
    /// <param name="targetAddress">
    /// Bluetooth address to connect to. Pass <c>null</c> to connect to the first
    /// BusyLight device found during scanning (discovery mode).
    /// </param>
    /// <param name="retryIntervalSeconds">Reconnect retry interval in seconds.</param>
    public BleService(string deviceName, ulong? targetAddress, int retryIntervalSeconds)
    {
        DeviceName            = deviceName;
        _targetAddress        = targetAddress;
        _retryIntervalSeconds = retryIntervalSeconds;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Start scanning / connecting and launch the reconnect loop.
    /// </summary>
    public void StartScanning()
    {
        _cts = new CancellationTokenSource();

        // Immediately signal "Searching" so the UI reflects the initial state.
        CurrentState = BleConnectionState.Searching;
        ConnectionChanged?.Invoke(this, BleConnectionState.Searching);

        // Always start the watcher — even when the target address is known.
        // BluetoothLEDevice.FromBluetoothAddressAsync only succeeds reliably when
        // the device has advertised recently (is in the Windows BLE cache). By waiting
        // for a fresh advertisement we guarantee that precondition is met.
        StartWatcher();

        _ = Task.Run(() => RetryLoopAsync(_cts.Token));
    }

    /// <summary>Stop scanning and the reconnect loop.</summary>
    public void Stop()
    {
        _cts?.Cancel();
        StopWatcher();
    }

    /// <summary>
    /// Write a 6-byte LED command to the peripheral.
    /// No-ops silently when not connected or when the command is unchanged.
    /// </summary>
    public async Task SendCommandAsync(LedCommand command)
    {
        if (!_connected || _ledChar is null) return;

        // Skip redundant writes — the firmware does not need the same command twice.
        if (command.Equals(_lastSentCommand)) return;

        try
        {
            var writer = new DataWriter();
            writer.WriteBytes(command.ToBytes());
            var buffer = writer.DetachBuffer();

            var result = await _ledChar
                .WriteValueWithResultAsync(buffer, GattWriteOption.WriteWithoutResponse)
                .AsTask()
                .ConfigureAwait(false);

            if (result.Status == GattCommunicationStatus.Success)
            {
                _lastSentCommand = command;
            }
            else
            {
                ErrorOccurred?.Invoke(this, $"BLE write failed: {result.Status}");
                HandleDisconnect();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorOccurred?.Invoke(this, $"BLE send error: {ex.Message}");
            HandleDisconnect();
        }
    }

    // ── Static discovery ──────────────────────────────────────────────────────

    /// <summary>
    /// Scan for all BusyLight peripherals for the given duration and return
    /// one <see cref="BleDeviceSettings"/> entry per discovered device.
    /// Called on first run (or via Settings) to let the user pick a device.
    /// </summary>
    public static async Task<IReadOnlyList<BleDeviceSettings>> DiscoverAsync(
        TimeSpan timeout, CancellationToken ct = default)
    {
        var found = new Dictionary<ulong, string>(); // address → local name

        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
        };
        // No service-UUID filter: the ESP32 puts the UUID and LocalName in the
        // scan-response packet, which Windows may not forward if a primary-ad filter
        // is active. We filter by device name in the handler instead.

        watcher.Received += (_, args) =>
        {
            string name = args.Advertisement.LocalName;
            if (!name.StartsWith("BusyLight", StringComparison.OrdinalIgnoreCase)) return;
            found.TryAdd(args.BluetoothAddress, name);
        };

        watcher.Start();

        try
        {
            await Task.Delay(timeout, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException) { /* scan was cut short — still return what we found */ }

        watcher.Stop();

        int index = 0;
        return found
            .Select(kv => new BleDeviceSettings
            {
                Name    = found.Count == 1 ? kv.Value : $"{kv.Value} {++index}",
                Address = BleDeviceSettings.FormatAddress(kv.Key),
            })
            .ToList();
    }

    // ── Advertisement watcher ─────────────────────────────────────────────────

    private void StartWatcher()
    {
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
        };
        // No service-UUID filter — see DiscoverAsync for the reasoning.
        _watcher.Received += OnAdvertisementReceived;
        _watcher.Stopped  += OnWatcherStopped;
        _watcher.Start();

        Debug.WriteLine($"[BLE:{DeviceName}] Watcher started — scanning…");
    }

    private void StopWatcher()
    {
        if (_watcher is null) return;
        _watcher.Received -= OnAdvertisementReceived;
        _watcher.Stopped  -= OnWatcherStopped;
        _watcher.Stop();
        _watcher = null;
    }

    private async void OnAdvertisementReceived(
        BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        if (_connected || _addressKnown) return;

        ulong advAddress = args.BluetoothAddress;

        if (_targetAddress.HasValue)
        {
            // Known-address mode: only react to our specific device.
            // We still use the watcher so that FromBluetoothAddressAsync is called
            // right after a fresh advertisement (device is guaranteed in Windows BLE cache).
            if (advAddress != _targetAddress.Value) return;
        }
        else
        {
            // Discovery mode: connect to the first BusyLight found.
            string localName = args.Advertisement.LocalName;
            if (!localName.StartsWith("BusyLight", StringComparison.OrdinalIgnoreCase)) return;
        }

        _addressKnown = true;
        DeviceAddress = advAddress;

        StopWatcher();

        await ConnectAsync(DeviceAddress.Value).ConfigureAwait(false);
    }

    private void OnWatcherStopped(
        BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        if (_cts is { IsCancellationRequested: false } && !_connected)
            Debug.WriteLine($"[BLE:{DeviceName}] Watcher stopped unexpectedly: {args.Error}");
    }

    // ── Connection logic ──────────────────────────────────────────────────────

    private async Task ConnectAsync(ulong address)
    {
        if (!await _connectLock.WaitAsync(0).ConfigureAwait(false))
            return; // Another connect attempt is already in progress

        try
        {
            if (_connected) return;

            Debug.WriteLine($"[BLE:{DeviceName}] Connecting to {address:X12}…");

            _device = await BluetoothLEDevice.FromBluetoothAddressAsync(address)
                                              .AsTask()
                                              .ConfigureAwait(false);

            if (_device is null)
            {
                Debug.WriteLine($"[BLE:{DeviceName}] FromBluetoothAddressAsync returned null.");
                ResetAddress();
                return;
            }

            _device.ConnectionStatusChanged += OnConnectionStatusChanged;

            var svcResult = await _device
                .GetGattServicesForUuidAsync(ServiceUuid, BluetoothCacheMode.Uncached)
                .AsTask()
                .ConfigureAwait(false);

            if (svcResult.Status != GattCommunicationStatus.Success
                || svcResult.Services.Count == 0)
            {
                Debug.WriteLine($"[BLE:{DeviceName}] Service discovery failed: {svcResult.Status}");
                ResetAddress();
                return;
            }

            var service    = svcResult.Services[0];
            var charResult = await service
                .GetCharacteristicsForUuidAsync(LedCharUuid, BluetoothCacheMode.Uncached)
                .AsTask()
                .ConfigureAwait(false);

            if (charResult.Status != GattCommunicationStatus.Success
                || charResult.Characteristics.Count == 0)
            {
                Debug.WriteLine($"[BLE:{DeviceName}] Characteristic discovery failed: {charResult.Status}");
                service.Dispose();
                ResetAddress();
                return;
            }

            _ledChar         = charResult.Characteristics[0];
            _connected       = true;
            _lastSentCommand = null; // Force re-send after reconnect

            Debug.WriteLine($"[BLE:{DeviceName}] Connected.");
            CurrentState = BleConnectionState.Connected;
            ConnectionChanged?.Invoke(this, BleConnectionState.Connected);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"[BLE:{DeviceName}] Connect error: {ex.Message}");
            ErrorOccurred?.Invoke(this, $"BLE connect error ({DeviceName}): {ex.Message}");
            ResetAddress();
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            Debug.WriteLine($"[BLE:{DeviceName}] Device disconnected.");
            HandleDisconnect();
        }
    }

    private void HandleDisconnect()
    {
        if (!_connected) return;

        _connected = false;
        _ledChar   = null;
        _device?.Dispose();
        _device = null;

        CurrentState = BleConnectionState.Disconnected;
        ConnectionChanged?.Invoke(this, BleConnectionState.Disconnected);

        // Clear _addressKnown so the watcher restarts and waits for a fresh
        // advertisement before attempting reconnect (ensures Windows BLE cache is warm).
        _addressKnown = false;
    }

    /// <summary>
    /// Clear transient address state after a failed connect attempt so the
    /// watcher can find the device again (both discovery and known-address modes).
    /// </summary>
    private void ResetAddress()
    {
        _ledChar   = null;
        _connected = false;
        _device?.Dispose();
        _device = null;
        _addressKnown = false;
    }

    // ── Reconnect loop ────────────────────────────────────────────────────────

    private async Task RetryLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_retryIntervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (_connected) continue;

                if (_watcher is null)
                {
                    // Resume scanning — fire Searching so the UI updates
                    CurrentState = BleConnectionState.Searching;
                    ConnectionChanged?.Invoke(this, BleConnectionState.Searching);
                    StartWatcher();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _connectLock.Dispose();
        _ledChar = null;
        _device?.Dispose();
        _device = null;
    }
}
