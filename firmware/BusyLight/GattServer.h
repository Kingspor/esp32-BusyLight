#pragma once

// Only project headers here — all BLE library headers are included in GattServer.cpp.
// This avoids the Windows case-insensitive filename collision between our
// GattServer.h and the ESP32 library's BLEServer.h.
#include <array>
#include "config.h"
#include "LedController.h"

// Forward declarations for BLE types used as pointer members.
// The full definitions are provided by the BLE library includes in GattServer.cpp.
class BLEServer;
class BLECharacteristic;

// Manages the BLE GATT server, advertising, and connection lifecycle.
// Call begin() once from setup() and update() on every loop() iteration.
class BleServer {
public:
    BleServer();

    // Initialise the BLE stack, create GATT services/characteristics,
    // and start advertising.
    void begin(LedController& ledController);

    // Handle deferred advertising restart after a client disconnects.
    // Must be called from loop().
    void update();

    // Read battery voltage via ADC and notify the connected client if the
    // notify interval has elapsed.  Must be called from loop().
    void updateTelemetry();

    // Returns true while a client is connected.
    bool isConnected() const;

private:
    BLEServer*         _pServer          = nullptr;
    BLECharacteristic* _pLedChar         = nullptr;
    BLECharacteristic* _pTelemetryChar   = nullptr;
    BLECharacteristic* _pProtocolVerChar = nullptr;
    BLECharacteristic* _pStateChar        = nullptr;

    // How many clients were connected on the previous loop() call.  The count
    // itself is owned by the BLE library (getConnectedCount()); we only keep the
    // previous value so update() can spot a change.
    //
    // Never read the library's count from inside a connect callback — it is
    // incremented *after* the callback returns, so it would be one short.
    uint32_t _oldCount = 0;

    // Deferred, non-blocking advertising restart (see BLE_ADV_RESTART_DELAY_MS).
    // _advertising mirrors what we believe the radio is doing: BLE stops
    // advertising on every established connection, so it is cleared whenever a
    // client arrives and set again when we restart.  Without it, a client
    // leaving while we already advertise would trigger a redundant start.
    bool          _advertisePending = false;
    unsigned long _advertiseSetAtMs = 0;
    bool          _advertising      = false;

    // Pending connection-parameter updates, one slot per client (sent one tick
    // after onConnect so the BLE stack has settled).
    //
    // A single slot would silently lose an update when two clients connect
    // within the same tick, and the client that lost it would stay on the
    // central's default interval — precisely the Windows service-discovery
    // failure (ERROR_BAD_COMMAND) this mechanism exists to prevent.
    //
    // Bluedroid needs the remote BD address, NimBLE the 16-bit handle; both are
    // kept so this header stays free of sdkconfig.h and the BLE headers.
    struct PendingConnParam {
        bool                   active = false;
        std::array<uint8_t, 6> bda{};      // Bluedroid: remote BD address
        uint16_t               handle = 0;  // NimBLE:    connection handle
    };
    std::array<PendingConnParam, BLE_MAX_CLIENTS> _pendingConnParams{};

    // First unused connection-parameter slot, or nullptr when all are taken.
    PendingConnParam* freeConnParamSlot();

    // Reference to the LED controller, set in begin().
    LedController* _ledController = nullptr;

    // Callback class implementations live in GattServer.cpp.
    // Only forward-declared here to keep BLE headers out of this file.
    class ServerCallbacks;
    class LedCharCallbacks;

    // Heap-allocated callback instances (lifetime == BleServer lifetime)
    ServerCallbacks*  _serverCallbacks  = nullptr;
    LedCharCallbacks* _ledCharCallbacks = nullptr;

    // Battery telemetry state
    unsigned long _lastTelemetryNotifyMs = 0;

    // Set from the write callback, acted on in update().  Notifying from inside
    // a GATT callback would re-enter the BLE stack; the connection-parameter
    // update above defers for the same reason.
    volatile bool _statePublishPending = false;

    // Copy the LED controller's current command into the state characteristic
    // and notify subscribers.
    void publishState();

    // Read the battery voltage from the ADC and apply the voltage-divider
    // correction.  Returns the result in millivolts.
    uint16_t readBatteryMillivolts();

    // Estimate state-of-charge (0–100 %) from a Li-Ion cell voltage in mV.
    uint8_t estimateSoc(uint16_t mv);
};
