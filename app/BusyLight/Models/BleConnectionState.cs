namespace BusyLight.Models;

/// <summary>The possible BLE connection states reported by <see cref="BusyLight.Services.BleService"/>.</summary>
public enum BleConnectionState
{
    /// <summary>Scanning for the peripheral (initial state or after a disconnect).</summary>
    Searching,

    /// <summary>GATT service is ready — commands can be sent.</summary>
    Connected,

    /// <summary>Connection was lost; a reconnect attempt will follow automatically.</summary>
    Disconnected,
}
