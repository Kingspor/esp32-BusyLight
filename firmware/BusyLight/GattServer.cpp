#include "GattServer.h"

// BLE library headers are included only here, never in GattServer.h.
// This prevents the Windows case-insensitive filename collision between
// our former BleServer.h and the ESP32 library's BLEServer.h.
#include <Arduino.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLE2902.h>

// ============================================================
// Callback class definitions
// (declared in GattServer.h as forward-declarations only)
// ============================================================

// Handles server-level connect / disconnect events.
class BleServer::ServerCallbacks : public BLEServerCallbacks {
public:
    explicit ServerCallbacks(BleServer& owner) : _owner(owner) {}

    void onConnect(BLEServer* /*pServer*/) override {
        _owner._deviceConnected = true;
    }

    void onDisconnect(BLEServer* /*pServer*/) override {
        _owner._deviceConnected = false;
    }

private:
    BleServer& _owner;
};

// Handles write events on the LED control characteristic.
class BleServer::LedCharCallbacks : public BLECharacteristicCallbacks {
public:
    explicit LedCharCallbacks(BleServer& owner) : _owner(owner) {}

    void onWrite(BLECharacteristic* pCharacteristic) override {
        // Use getData()/getLength() instead of getValue() for compatibility
        // with esp32 Arduino board package v3.x (getValue() now returns String).
        const uint8_t* data = pCharacteristic->getData();
        size_t         len  = pCharacteristic->getLength();

        if (len == CMD_PACKET_SIZE) {
            _owner._ledController->setCommand(data, CMD_PACKET_SIZE);

            Serial.printf("[BLE] LED command: R=%u G=%u B=%u Bri=%u Mode=%u Spd=%u\n",
                          data[0], data[1], data[2], data[3], data[4], data[5]);
        } else {
            Serial.printf("[BLE] Invalid command length: %u (expected %u)\n",
                          len, CMD_PACKET_SIZE);
        }
    }

private:
    BleServer& _owner;
};

// ============================================================
// Constructor
// ============================================================

BleServer::BleServer()
    : _pServer(nullptr),
      _pLedChar(nullptr),
      _pTelemetryChar(nullptr),
      _deviceConnected(false),
      _oldConnected(false),
      _ledController(nullptr),
      _serverCallbacks(nullptr),
      _ledCharCallbacks(nullptr)
{
}

// ============================================================
// Initialisation
// ============================================================

void BleServer::begin(LedController& ledController) {
    _ledController = &ledController;

    // Build a unique device name from the last two bytes of the BLE MAC address.
    // The BLE MAC is derived from the eFuse base MAC: last byte = base + 2.
    // eFuse MAC is stored little-endian in the uint64, so:
    //   byte 4 = bits 32-39, byte 5 = bits 40-47
    // Result: "BusyLight-XXYY" where XX:YY are the last two bytes of the BLE address.
    uint64_t chipId = ESP.getEfuseMac();
    char deviceName[32];
    uint8_t bleByte4 = (uint8_t)(chipId >> 32);       // second-to-last byte (unchanged)
    uint8_t bleByte5 = (uint8_t)(chipId >> 40) + 2;   // last byte = base + 2 (BLE offset)
    snprintf(deviceName, sizeof(deviceName), "BusyLight-%02X%02X", bleByte4, bleByte5);

    BLEDevice::init(deviceName);
    Serial.print("[BLE] Name: ");
    Serial.println(deviceName);
    // Create server and register connection callbacks
    _serverCallbacks = new ServerCallbacks(*this);
    _pServer = BLEDevice::createServer();
    _pServer->setCallbacks(_serverCallbacks);

    // Create the primary GATT service
    BLEService* pService = _pServer->createService(SERVICE_UUID);

    // LED control characteristic: writable with and without response
    _ledCharCallbacks = new LedCharCallbacks(*this);
    _pLedChar = pService->createCharacteristic(
        LED_CHAR_UUID,
        BLECharacteristic::PROPERTY_WRITE | BLECharacteristic::PROPERTY_WRITE_NR
    );
    _pLedChar->setCallbacks(_ledCharCallbacks);

    // Telemetry characteristic: readable and notifiable (stub — not yet implemented)
    _pTelemetryChar = pService->createCharacteristic(
        TELEMETRY_CHAR_UUID,
        BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_NOTIFY
    );
    // Client Characteristic Configuration descriptor required for NOTIFY
    _pTelemetryChar->addDescriptor(new BLE2902());
    // Stub value so a connected client can read something meaningful
    _pTelemetryChar->setValue("BusyLight v1.0");

    // Start the service
    pService->start();

    // Configure and start advertising
    BLEAdvertising* pAdvertising = BLEDevice::getAdvertising();
    pAdvertising->addServiceUUID(SERVICE_UUID);
    pAdvertising->setScanResponse(true);

    // Advertising interval in units of 0.625 ms (1600 * 0.625 ms = 1000 ms)
    pAdvertising->setMinInterval(BLE_ADV_INTERVAL_MIN);
    pAdvertising->setMaxInterval(BLE_ADV_INTERVAL_MAX);

    pAdvertising->start();

    Serial.println("[BLE] Advertising started. Waiting for client...");
}

// ============================================================
// Loop-tick: restart advertising after a disconnect
// ============================================================

void BleServer::update() {
    // A client just disconnected — restart advertising so a new client can connect
    if (_oldConnected && !_deviceConnected) {
        delay(500);  // Brief pause to let the BLE stack settle
        _pServer->startAdvertising();
        Serial.println("[BLE] Client disconnected. Re-advertising...");
    }

    // A new client has just connected
    if (!_oldConnected && _deviceConnected) {
        Serial.println("[BLE] Client connected.");
    }

    _oldConnected = _deviceConnected;
}

// ============================================================
// Status query
// ============================================================

bool BleServer::isConnected() const {
    return _deviceConnected;
}
