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

    // Returns true while a client is connected.
    bool isConnected() const;

private:
    BLEServer*         _pServer;
    BLECharacteristic* _pLedChar;
    BLECharacteristic* _pTelemetryChar;
    BLECharacteristic* _pProtocolVerChar;

    // Tracks the connection state across two consecutive loop() calls
    // so advertising can be restarted after a disconnect.
    volatile bool _deviceConnected;
    bool          _oldConnected;

    // State for the deferred connection-parameter update (sent one tick after
    // onConnect so the BLE stack has fully settled).
    // Bluedroid builds need the remote BD address; NimBLE builds need the
    // 16-bit connection handle.  Both fields are kept (8 bytes total) to
    // avoid including sdkconfig.h / BLE headers in this header file.
    std::array<uint8_t, 6> _remoteBda{};  // Bluedroid: remote BD address from onConnect
    uint16_t _connHandle              = 0; // NimBLE:    connection handle from onConnect
    bool     _connParamUpdatePending  = false;

    // Reference to the LED controller, set in begin().
    LedController* _ledController = nullptr;

    // Callback class implementations live in GattServer.cpp.
    // Only forward-declared here to keep BLE headers out of this file.
    class ServerCallbacks;
    class LedCharCallbacks;

    // Heap-allocated callback instances (lifetime == BleServer lifetime)
    ServerCallbacks*  _serverCallbacks  = nullptr;
    LedCharCallbacks* _ledCharCallbacks = nullptr;
};
