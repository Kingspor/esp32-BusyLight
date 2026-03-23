using BusyLight.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
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

    private static readonly Guid TelemetryCharUuid =
        Guid.Parse("feda0102-51a7-4fb7-a27b-c720bef16ef7");

    private static readonly Guid ProtocolVerCharUuid =
        Guid.Parse("feda0103-51a7-4fb7-a27b-c720bef16ef7");

    /// <summary>
    /// Protocol version this app build expects from the firmware.
    /// Must match <c>PROTOCOL_VERSION</c> in <c>firmware/BusyLight/config.h</c>.
    /// </summary>
    private const byte ExpectedProtocolVersion = 1;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the thread pool when the connection state changes.
    /// </summary>
    public event EventHandler<BleConnectionState>? ConnectionChanged;

    /// <summary>
    /// Raised when a non-fatal error occurs so the tray can show a balloon tip.
    /// </summary>
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// Raised when a new battery telemetry packet is received (notify or initial read).
    /// </summary>
    public event EventHandler<BatteryReading>? BatteryChanged;

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

    /// <summary>
    /// Protocol version reported by the connected firmware.
    /// Null until a device connects and the version characteristic is read.
    /// </summary>
    public byte? FirmwareProtocolVersion { get; private set; }

    /// <summary>
    /// Most recent battery reading received from the device.
    /// Null until the first telemetry packet is received after connecting.
    /// </summary>
    public BatteryReading? LastBatteryReading { get; private set; }

    // ── Private state ─────────────────────────────────────────────────────────

    private readonly ulong?  _targetAddress;       // null = discovery mode
    private readonly int     _retryIntervalSeconds;

    private BluetoothLEDevice?  _device;
    private GattCharacteristic? _ledChar;
    private GattCharacteristic? _telemetryChar;
    private LedCommand?         _lastSentCommand;

    private BluetoothLEAdvertisementWatcher? _watcher;
    private CancellationTokenSource?         _cts;

    private bool _connected;
    private bool _addressKnown;  // true when DeviceAddress has been set

    /// <summary>
    /// Set to true after the first connection-failure error is surfaced via
    /// <see cref="ErrorOccurred"/>.  Reset to false on successful connect or
    /// disconnect so the next failure will be reported again.
    /// </summary>
    private bool _connectDiagnosticRaised;

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

        // Notify UI — without this the status label stays at "Suche…" forever
        if (!_connected)
        {
            CurrentState = BleConnectionState.Disconnected;
            ConnectionChanged?.Invoke(this, BleConnectionState.Disconnected);
        }
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

        LogService.Log($"[BLE:{DeviceName}] Watcher started — scanning…");
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
        {
            LogService.Log($"[BLE:{DeviceName}] Watcher stopped unexpectedly: {args.Error}");
            if (args.Error != Windows.Devices.Bluetooth.BluetoothError.Success)
            {
                ErrorOccurred?.Invoke(this,
                    $"BLE-Scanner unerwartet beendet ({DeviceName}): {args.Error}. " +
                    $"Bluetooth aktiviert?");
            }
        }
    }

    // ── Connection logic ──────────────────────────────────────────────────────

    private async Task ConnectAsync(ulong address)
    {
        if (!await _connectLock.WaitAsync(0).ConfigureAwait(false))
            return; // Another connect attempt is already in progress

        try
        {
            if (_connected) return;

            LogService.Log($"[BLE:{DeviceName}] Connecting to {address:X12}…");

            _device = await BluetoothLEDevice.FromBluetoothAddressAsync(address)
                                              .AsTask()
                                              .ConfigureAwait(false);

            if (_device is null)
            {
                LogService.Log($"[BLE:{DeviceName}] FromBluetoothAddressAsync returned null — device not in Windows BLE cache.");
                ResetAddress();
                return;
            }

            _device.ConnectionStatusChanged += OnConnectionStatusChanged;

            // Service discovery strategy:
            //
            //  1. Try Cached first — Windows stores the GATT structure after the first
            //     connection and returns it instantly without any ATT communication.
            //     This bypasses the long initial connection interval entirely.
            //
            //  2. If the cache is cold (first-ever connection), fall back to Uncached
            //     with retries.  See DiscoverGattServiceAsync for details.
            var (svcResult, foundInCache) = await DiscoverGattServiceAsync(_device!).ConfigureAwait(false);

            if (svcResult is null
                || svcResult.Status != GattCommunicationStatus.Success
                || svcResult.Services.Count == 0)
            {
                var status = svcResult?.Status.ToString() ?? "COMException";
                LogService.Log($"[BLE:{DeviceName}] Service discovery failed (Cached + Uncached): {status}");
                RaiseDiagnosticError(
                    $"GATT-Service nicht gefunden auf '{DeviceName}' (Status: {status}). " +
                    $"Falsche Firmware? Details im Log.");
                ResetAddress();
                return;
            }

            var service    = svcResult.Services[0];
            // Use Cached when service was found in cache — avoids requiring an active
            // connection when Windows already has the full GATT structure locally.
            var charCacheMode = foundInCache ? BluetoothCacheMode.Cached : BluetoothCacheMode.Uncached;
            var charResult = await service
                .GetCharacteristicsForUuidAsync(LedCharUuid, charCacheMode)
                .AsTask()
                .ConfigureAwait(false);

            if (charResult.Status != GattCommunicationStatus.Success
                || charResult.Characteristics.Count == 0)
            {
                LogService.Log($"[BLE:{DeviceName}] Characteristic discovery failed: {charResult.Status}");
                RaiseDiagnosticError(
                    $"LED-Charakteristik nicht gefunden auf '{DeviceName}' (Status: {charResult.Status}). " +
                    $"Falsche Firmware? Details im Log.");
                service.Dispose();
                ResetAddress();
                return;
            }

            _ledChar         = charResult.Characteristics[0];
            _connected       = true;
            _lastSentCommand = null; // Force re-send after reconnect

            // Read the protocol version characteristic (feda0103-…).
            // Old firmware without this characteristic is treated as version 0 (incompatible).
            await CheckProtocolVersionAsync(service, charCacheMode).ConfigureAwait(false);

            // Subscribe to battery telemetry notifications and read initial value.
            await SubscribeTelemetryAsync(service, charCacheMode).ConfigureAwait(false);

            LogService.Log($"[BLE:{DeviceName}] Connected.");
            _connectDiagnosticRaised = false;
            CurrentState = BleConnectionState.Connected;
            ConnectionChanged?.Invoke(this, BleConnectionState.Connected);
        }
        catch (Exception ex)
        {
            LogService.Log($"[BLE:{DeviceName}] Connect error: {ex}");
            ErrorOccurred?.Invoke(this, $"BLE-Verbindungsfehler ({DeviceName}): {ex.Message}");
            ResetAddress();
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>
    /// Two-pass GATT service discovery.
    /// Pass 1: Cached — instant, no BLE ATT communication required.
    /// Pass 2: Uncached with up to 4 retries, waiting ≥2 s between attempts so
    ///         the Windows GATT stack (default interval 698–2500 ms) has time to
    ///         settle.  After each Uncached attempt the cache is re-checked because
    ///         Windows populates it asynchronously in the background.
    /// </summary>
    private async Task<(GattDeviceServicesResult? Result, bool FoundInCache)>
        DiscoverGattServiceAsync(BluetoothLEDevice device)
    {
        // ── Pass 1: Cached ────────────────────────────────────────────────────
        var cached = await TryGetServiceAsync(device, ServiceUuid, BluetoothCacheMode.Cached).ConfigureAwait(false);
        if (cached is not null)
        {
            LogService.Log($"[BLE:{DeviceName}] Service found in Windows GATT cache.");
            return (cached, true);
        }
        LogService.Log($"[BLE:{DeviceName}] Cache miss — will try Uncached.");

        // ── Pass 2: Uncached with retries ─────────────────────────────────────
        const int maxAttempts = 4;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                // Windows reports Connected (link layer) before the GATT ATT layer is
                // ready.  Wait > one full Windows connection interval (up to 2500 ms).
                int delayMs = 2000 + 500 * (attempt - 1); // 2000, 2500, 3000 ms
                LogService.Log($"[BLE:{DeviceName}] Uncached retry {attempt + 1}/{maxAttempts} — waiting {delayMs} ms for GATT stack to settle…");
                await Task.Delay(delayMs).ConfigureAwait(false);

                if (!await WaitForConnectedAsync(device, TimeSpan.FromSeconds(4)).ConfigureAwait(false))
                {
                    LogService.Log($"[BLE:{DeviceName}] BLE link lost before retry {attempt + 1} — aborting.");
                    break;
                }

                // Windows may have populated the cache during the previous Uncached attempt.
                var cachedRetry = await TryGetServiceAsync(device, ServiceUuid, BluetoothCacheMode.Cached).ConfigureAwait(false);
                if (cachedRetry is not null)
                {
                    LogService.Log($"[BLE:{DeviceName}] Service found in GATT cache on retry {attempt + 1}.");
                    return (cachedRetry, true);
                }
            }

            var uncached = await TryGetServiceAsync(device, ServiceUuid, BluetoothCacheMode.Uncached).ConfigureAwait(false);
            if (uncached is not null)
                return (uncached, false);

            LogService.Log($"[BLE:{DeviceName}] ERROR_BAD_COMMAND on Uncached attempt {attempt + 1} — BLE link not ready yet.");
        }

        return (null, false);
    }

    /// <summary>
    /// Single attempt to retrieve a GATT service by UUID, using the specified cache mode.
    /// Returns null on ERROR_BAD_COMMAND (0x80070016) or a non-Success status.
    /// </summary>
    private static async Task<GattDeviceServicesResult?> TryGetServiceAsync(
        BluetoothLEDevice device, Guid serviceUuid, BluetoothCacheMode cacheMode)
    {
        try
        {
            var result = await device
                .GetGattServicesForUuidAsync(serviceUuid, cacheMode)
                .AsTask()
                .ConfigureAwait(false);

            return result.Status == GattCommunicationStatus.Success && result.Services.Count > 0
                ? result
                : null;
        }
        catch (System.Runtime.InteropServices.COMException ex)
            when ((uint)ex.HResult == 0x80070016)
        {
            // ERROR_BAD_COMMAND — Windows GATT stack not ready yet.
            return null;
        }
    }

    /// <summary>
    /// Waits until <paramref name="device"/>'s ConnectionStatus transitions to
    /// <see cref="BluetoothConnectionStatus.Connected"/>, or until the timeout elapses.
    /// Returns <c>true</c> if connected, <c>false</c> if disconnected or timed out.
    /// </summary>
    private static async Task<bool> WaitForConnectedAsync(BluetoothLEDevice device, TimeSpan timeout)
    {
        if (device.ConnectionStatus == BluetoothConnectionStatus.Connected)
            return true;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        TypedEventHandler<BluetoothLEDevice, object> handler = (d, _) =>
        {
            if      (d.ConnectionStatus == BluetoothConnectionStatus.Connected)    tcs.TrySetResult(true);
            else if (d.ConnectionStatus == BluetoothConnectionStatus.Disconnected) tcs.TrySetResult(false);
        };

        device.ConnectionStatusChanged += handler;
        try
        {
            // Re-check after subscribing to avoid losing an event that fired between
            // the first check and the subscribe.
            if (device.ConnectionStatus == BluetoothConnectionStatus.Connected) return true;
            if (device.ConnectionStatus == BluetoothConnectionStatus.Disconnected) return false;

            return await tcs.Task.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
        finally
        {
            device.ConnectionStatusChanged -= handler;
        }
    }

    private async Task CheckProtocolVersionAsync(GattDeviceService service, BluetoothCacheMode cacheMode)
    {
        var verResult = await service
            .GetCharacteristicsForUuidAsync(ProtocolVerCharUuid, cacheMode)
            .AsTask()
            .ConfigureAwait(false);

        if (verResult.Status != GattCommunicationStatus.Success
            || verResult.Characteristics.Count == 0)
        {
            // Firmware predates protocol versioning — treat as version 0.
            ErrorOccurred?.Invoke(this,
                $"Firmware auf '{DeviceName}' ist veraltet (kein Protokoll-Versions-Merkmal). " +
                $"Bitte Firmware auf v{ExpectedProtocolVersion} aktualisieren.");
            return;
        }

        var readResult = await verResult.Characteristics[0]
            .ReadValueAsync(cacheMode)
            .AsTask()
            .ConfigureAwait(false);

        if (readResult.Status != GattCommunicationStatus.Success)
        {
            LogService.Log($"[BLE:{DeviceName}] Could not read protocol version: {readResult.Status}");
            return;
        }

        var reader = DataReader.FromBuffer(readResult.Value);
        byte firmwareVersion = reader.ReadByte();
        FirmwareProtocolVersion = firmwareVersion;

        LogService.Log($"[BLE:{DeviceName}] Protocol version: firmware={firmwareVersion}, expected={ExpectedProtocolVersion}");

        if (firmwareVersion != ExpectedProtocolVersion)
        {
            ErrorOccurred?.Invoke(this,
                $"Protokoll-Inkompatibilität auf '{DeviceName}': " +
                $"Firmware v{firmwareVersion} ≠ App erwartet v{ExpectedProtocolVersion}. " +
                $"Bitte Firmware oder App aktualisieren.");
        }
    }

    private async Task SubscribeTelemetryAsync(GattDeviceService service, BluetoothCacheMode cacheMode)
    {
        var result = await service
            .GetCharacteristicsForUuidAsync(TelemetryCharUuid, cacheMode)
            .AsTask()
            .ConfigureAwait(false);

        if (result.Status != GattCommunicationStatus.Success
            || result.Characteristics.Count == 0)
        {
            LogService.Log($"[BLE:{DeviceName}] Telemetry characteristic not found — battery monitoring unavailable.");
            return;
        }

        _telemetryChar = result.Characteristics[0];

        // Enable notifications on the firmware side via the CCCD descriptor
        var cccdResult = await _telemetryChar
            .WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify)
            .AsTask()
            .ConfigureAwait(false);

        if (cccdResult == GattCommunicationStatus.Success)
        {
            _telemetryChar.ValueChanged += OnTelemetryValueChanged;
            LogService.Log($"[BLE:{DeviceName}] Telemetry notifications enabled.");
        }
        else
        {
            LogService.Log($"[BLE:{DeviceName}] Could not enable telemetry notifications: {cccdResult}");
        }

        // Read initial value so the UI shows something before the first notify fires
        var readResult = await _telemetryChar
            .ReadValueAsync(cacheMode)
            .AsTask()
            .ConfigureAwait(false);

        if (readResult.Status == GattCommunicationStatus.Success)
            ParseTelemetryPacket(readResult.Value);
    }

    /// <summary>
    /// Trigger an immediate on-demand read of the battery telemetry characteristic.
    /// Returns the parsed reading, or null when not connected or on error.
    /// </summary>
    public async Task<BatteryReading?> ReadBatteryAsync()
    {
        if (_telemetryChar is null) return null;

        try
        {
            var result = await _telemetryChar
                .ReadValueAsync(BluetoothCacheMode.Uncached)
                .AsTask()
                .ConfigureAwait(false);

            if (result.Status != GattCommunicationStatus.Success) return null;

            ParseTelemetryPacket(result.Value);
            return LastBatteryReading;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogService.Log($"[BLE:{DeviceName}] Battery read error: {ex.Message}");
            return null;
        }
    }

    private void OnTelemetryValueChanged(
        GattCharacteristic sender,
        GattValueChangedEventArgs args)
        => ParseTelemetryPacket(args.CharacteristicValue);

    private void ParseTelemetryPacket(Windows.Storage.Streams.IBuffer buffer)
    {
        if (buffer.Length < 3) return;

        var reader  = DataReader.FromBuffer(buffer);
        reader.ByteOrder = Windows.Storage.Streams.ByteOrder.LittleEndian;
        int  mv  = reader.ReadUInt16();
        int  soc = reader.ReadByte();

        LastBatteryReading = new BatteryReading(mv, soc);
        LogService.Log($"[BLE:{DeviceName}] Battery: {LastBatteryReading}");
        BatteryChanged?.Invoke(this, LastBatteryReading);
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            LogService.Log($"[BLE:{DeviceName}] Device disconnected.");
            HandleDisconnect();
        }
    }

    private void HandleDisconnect()
    {
        if (!_connected) return;

        _connected               = false;
        _ledChar                 = null;
        if (_telemetryChar is not null)
        {
            _telemetryChar.ValueChanged -= OnTelemetryValueChanged;
            _telemetryChar = null;
        }
        LastBatteryReading       = null;
        FirmwareProtocolVersion  = null;
        _connectDiagnosticRaised = false;
        _device?.Dispose();
        _device = null;

        CurrentState = BleConnectionState.Disconnected;
        ConnectionChanged?.Invoke(this, BleConnectionState.Disconnected);

        // Clear _addressKnown so the watcher restarts and waits for a fresh
        // advertisement before attempting reconnect (ensures Windows BLE cache is warm).
        _addressKnown = false;
    }

    /// <summary>
    /// Raise <see cref="ErrorOccurred"/> with a diagnostic message, but only
    /// once per connection attempt to avoid balloon-tip spam on repeated retries.
    /// </summary>
    private void RaiseDiagnosticError(string message)
    {
        if (_connectDiagnosticRaised) return;
        _connectDiagnosticRaised = true;
        ErrorOccurred?.Invoke(this, message);
    }

    /// <summary>
    /// Clear transient address state after a failed connect attempt so the
    /// watcher can find the device again (both discovery and known-address modes).
    /// </summary>
    private void ResetAddress()
    {
        _ledChar   = null;
        _connected = false;
        if (_device is not null)
        {
            _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            _device.Dispose();
            _device = null;
        }
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

                // Only restart the watcher when no connection attempt is in progress.
                // _addressKnown is true between "advertisement received" and
                // "ConnectAsync completed or failed", so we skip the restart
                // during that window to avoid a second watcher interfering.
                if (_watcher is null && !_addressKnown)
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
