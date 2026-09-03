#include "GattServer.h"

#include <array>

// BLE library headers are included only here, never in GattServer.h.
// This prevents the Windows case-insensitive filename collision between
// our former BleServer.h and the ESP32 library's BLEServer.h.
#include <Arduino.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLE2902.h>
// esp_gap_ble_api.h is already pulled in transitively by BLEDevice.h / BLEAdvertising.h

// ============================================================
// Callback class definitions
// (declared in GattServer.h as forward-declarations only)
// ============================================================

// Handles server-level connect / disconnect events.
class BleServer::ServerCallbacks : public BLEServerCallbacks {
public:
    explicit ServerCallbacks(BleServer& owner) : _owner(owner) {}

    // Two-parameter onConnect: captures the addressing info so update() can
    // call BLEServer::updateConnParams() on the next tick.
    // Arduino-ESP32 v3.x supports both Bluedroid (esp_bd_addr_t / gatts param)
    // and NimBLE (conn_handle / ble_gap_conn_desc), selected at compile time.
#if defined(CONFIG_BLUEDROID_ENABLED)
    void onConnect(BLEServer* /*pServer*/, esp_ble_gatts_cb_param_t* param) override {
        if (auto* slot = _owner.freeConnParamSlot()) {
            memcpy(slot->bda.data(), param->connect.remote_bda, slot->bda.size());
            slot->active = true;
        }
    }
#elif defined(CONFIG_NIMBLE_ENABLED)
    void onConnect(BLEServer* /*pServer*/, ble_gap_conn_desc* desc) override {
        if (auto* slot = _owner.freeConnParamSlot()) {
            slot->handle = desc->conn_handle;
            slot->active = true;
        }
    }
#endif

    // No onDisconnect override: update() notices the change through the BLE
    // library's connection count, which is the single source of truth now that
    // more than one client can be attached.

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
            _owner._statePublishPending = true;

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

BleServer::BleServer() = default;

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

    // Configure ADC pin for battery voltage measurement.
    // Use 12 dB attenuation (0–3.1 V input range) on this pin only.
    analogSetPinAttenuation(BATTERY_ADC_PIN, ADC_11db);

    // Telemetry characteristic: readable and notifiable.
    // Format: 3 bytes — [voltage_mv_lo, voltage_mv_hi, soc_percent]
    _pTelemetryChar = pService->createCharacteristic(
        TELEMETRY_CHAR_UUID,
        BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_NOTIFY
    );
    // Client Characteristic Configuration descriptor required for NOTIFY
    _pTelemetryChar->addDescriptor(new BLE2902());
    // Populate an initial reading so READ-on-demand returns a real value immediately
    {
        uint16_t mv  = readBatteryMillivolts();
        uint8_t  soc = estimateSoc(mv);
        std::array<uint8_t, 3> buf = {
            static_cast<uint8_t>(mv & 0xFF),
            static_cast<uint8_t>(mv >> 8),
            soc
        };
        _pTelemetryChar->setValue(buf.data(), buf.size());
        Serial.printf("[BLE] Battery initial read: %u mV, %u%%\n", mv, soc);
    }

    // Protocol version characteristic: read-only single byte.
    // The Windows app reads this on connect and warns if the version is incompatible.
    _pProtocolVerChar = pService->createCharacteristic(
        PROTOCOL_VER_CHAR_UUID,
        BLECharacteristic::PROPERTY_READ
    );
    uint8_t protocolVersion = PROTOCOL_VERSION;
    _pProtocolVerChar->setValue(&protocolVersion, 1);
    Serial.printf("[BLE] Protocol version: %u\n", protocolVersion);

    // State characteristic: readable and notifiable, carrying the command the
    // ring is currently animating in the same 6-byte layout as LED_CHAR_UUID.
    // A client that (re)connects has no other way to find out what is on screen;
    // without this it can only guess from what it itself last sent, which is
    // wrong as soon as another client — or a device reboot — changed it.
    _pStateChar = pService->createCharacteristic(
        STATE_CHAR_UUID,
        BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_NOTIFY
    );
    _pStateChar->addDescriptor(new BLE2902());
    publishState();  // seed it so a READ before the first write returns the truth

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
    _advertising = true;

    Serial.printf("[BLE] Advertising started. Up to %u clients.\n",
                  (unsigned)BLE_MAX_CLIENTS);
}

// ============================================================
// Loop-tick: restart advertising after a disconnect
// ============================================================

void BleServer::update() {
    const uint32_t count = _pServer->getConnectedCount();

    if (count != _oldCount) {
        Serial.printf("[BLE] Clients connected: %u/%u\n",
                      (unsigned)count, (unsigned)BLE_MAX_CLIENTS);

        // BLE stops advertising the moment a connection is established.  The
        // previous version only restarted it after a disconnect, which is why
        // a second client could never even discover the device.
        if (count > _oldCount) _advertising = false;
        _oldCount = count;

        if (count < BLE_MAX_CLIENTS && !_advertising) {
            _advertisePending = true;
            _advertiseSetAtMs = millis();
        } else if (count >= BLE_MAX_CLIENTS) {
            _advertisePending = false;  // full — stop offering a slot
            Serial.println("[BLE] All client slots taken.");
        }
    }

    if (_advertisePending && millis() - _advertiseSetAtMs >= BLE_ADV_RESTART_DELAY_MS) {
        _advertisePending = false;
        // Re-check: a slot may have filled while we waited out the settle time.
        if (_pServer->getConnectedCount() < BLE_MAX_CLIENTS) {
            _pServer->startAdvertising();
            _advertising = true;
            Serial.println("[BLE] Advertising — a client slot is free.");
        }
    }

    // Request a short connection interval for each freshly connected client so
    // the Windows GATT stack can complete service discovery without timing out.
    // Windows defaults to 698–2500 ms, which triggers ERROR_BAD_COMMAND
    // (0x80070016) on the app side.  Sent a tick after onConnect so the BLE
    // stack has settled — and never from inside the callback itself.
    for (auto& slot : _pendingConnParams) {
        if (!slot.active) continue;
        slot.active = false;
#if defined(CONFIG_BLUEDROID_ENABLED)
        _pServer->updateConnParams(slot.bda.data(),
            BLE_CONN_INTERVAL_MIN, BLE_CONN_INTERVAL_MAX,
            BLE_CONN_LATENCY, BLE_CONN_TIMEOUT);
#elif defined(CONFIG_NIMBLE_ENABLED)
        _pServer->updateConnParams(slot.handle,
            BLE_CONN_INTERVAL_MIN, BLE_CONN_INTERVAL_MAX,
            BLE_CONN_LATENCY, BLE_CONN_TIMEOUT);
#endif
        Serial.printf("[BLE] Requested conn interval %u-%u ms\n",
                      (unsigned)(BLE_CONN_INTERVAL_MIN * 5 / 4),
                      (unsigned)(BLE_CONN_INTERVAL_MAX * 5 / 4));
    }

    // A new LED command arrived — tell every subscriber what the ring now shows.
    if (_statePublishPending) {
        _statePublishPending = false;
        publishState();
    }
}

BleServer::PendingConnParam* BleServer::freeConnParamSlot() {
    for (auto& slot : _pendingConnParams) {
        if (!slot.active) return &slot;
    }
    return nullptr;  // every slot pending — the connection limit prevents this
}

// ============================================================
// Status query
// ============================================================

bool BleServer::isConnected() const {
    // True while ANY client holds the link.  loop() uses this for the LED hold
    // and the status LED, so both must only react when the LAST client leaves.
    return _pServer != nullptr && _pServer->getConnectedCount() > 0;
}

// ============================================================
// Current-state publication
// ============================================================

void BleServer::publishState() {
    if (_pStateChar == nullptr || _ledController == nullptr) return;

    const LedCommand& c = _ledController->command();
    std::array<uint8_t, CMD_PACKET_SIZE> buf = {
        c.r, c.g, c.b, c.brightness, c.mode, c.speed
    };
    _pStateChar->setValue(buf.data(), buf.size());

    // A READ always works; NOTIFY only means anything with a client attached.
    // notify() reaches every subscriber, so both clients stay in sync.
    if (isConnected()) _pStateChar->notify();
}

// ============================================================
// Battery telemetry
// ============================================================

void BleServer::updateTelemetry() {
    if (!isConnected()) return;

    unsigned long now = millis();
    if (now - _lastTelemetryNotifyMs < BATTERY_NOTIFY_INTERVAL_MS) return;
    _lastTelemetryNotifyMs = now;

    uint16_t mv  = readBatteryMillivolts();
    uint8_t  soc = estimateSoc(mv);

    std::array<uint8_t, 3> buf = {
        static_cast<uint8_t>(mv & 0xFF),
        static_cast<uint8_t>(mv >> 8),
        soc
    };
    _pTelemetryChar->setValue(buf.data(), buf.size());
    _pTelemetryChar->notify();

    Serial.printf("[BLE] Telemetry notify: %u mV, %u%%\n", mv, soc);
}

uint16_t BleServer::readBatteryMillivolts() {
    // Average BATTERY_SAMPLES readings to reduce ADC noise.
    // analogReadMilliVolts() uses the ESP32-C3's internal ADC calibration.
    uint32_t sum = 0;
    for (int i = 0; i < BATTERY_SAMPLES; i++) {
        sum += analogReadMilliVolts(BATTERY_ADC_PIN);
    }
    uint32_t v_adc_mv = sum / BATTERY_SAMPLES;

    // Apply voltage-divider correction: V_bat = V_adc * (R1 + R2) / R2
    uint32_t v_bat_mv = v_adc_mv
                        * (BATTERY_DIVIDER_R1_OHM + BATTERY_DIVIDER_R2_OHM)
                        / BATTERY_DIVIDER_R2_OHM;

    return (uint16_t)v_bat_mv;
}

uint8_t BleServer::estimateSoc(uint16_t mv) {
    // Li-Ion 18650 discharge curve lookup table (voltage_mv, soc_percent).
    // Values based on a typical discharge at moderate load.
    static constexpr std::array<uint16_t, 11> voltages = {
        3200, 3300, 3400, 3500, 3600, 3700, 3800, 3900, 4000, 4100, 4200
    };
    static constexpr std::array<uint8_t, 11> socs = {
           0,    3,    7,   15,   25,   40,   54,   67,   79,   90,  100
    };
    constexpr auto count = voltages.size();

    if (mv <= voltages[0])        return socs[0];
    if (mv >= voltages[count - 1]) return socs[count - 1];

    // Linear interpolation between bracketing table entries
    for (int i = 1; i < count; i++) {
        if (mv <= voltages[i]) {
            uint16_t v0 = voltages[i - 1];
            uint16_t v1 = voltages[i];
            uint8_t  s0 = socs[i - 1];
            uint8_t  s1 = socs[i];
            return (uint8_t)(s0 + (uint32_t)(s1 - s0) * (mv - v0) / (v1 - v0));
        }
    }
    return socs[count - 1];
}
