// BusyLight Firmware
// ESP32-C3 Super Mini — BLE GATT server controlling a WS2812B LED ring
// via 6-byte LED commands from the Windows tray application.

#include <Arduino.h>
#include "config.h"
#include "LedController.h"
#include "GattServer.h"

// ── Global objects ────────────────────────────────────────────────────────────

LedController ledController;
BleServer     bleServer;

// ── Status LED state (blinking when no client is connected) ───────────────────

static unsigned long lastStatusBlink = 0;
static bool          statusLedOn     = false;

// ============================================================
// setup
// ============================================================

void setup() {
    Serial.begin(115200);
    Serial.println("[BusyLight] Booting...");

    // Status LED: active LOW (HIGH = off, LOW = on)
    pinMode(STATUS_LED_PIN, OUTPUT);
    digitalWrite(STATUS_LED_PIN, HIGH);  // Start with LED off

    // Initialise LED ring
    ledController.begin();
    ledController.off();

    // Initialise BLE server and start advertising
    bleServer.begin(ledController);

    Serial.println("[BusyLight] Ready.");
}

// ============================================================
// loop
// ============================================================

void loop() {
    // Let the BLE server manage connect / disconnect events
    bleServer.update();

    if (bleServer.isConnected()) {
        // Client connected: run LED animations, status LED solid ON
        ledController.update();
        digitalWrite(STATUS_LED_PIN, LOW);  // Active LOW = LED on
    } else {
        // No client: LED ring off, status LED blinks at 1 Hz
        ledController.off();

        unsigned long now = millis();
        if (now - lastStatusBlink >= STATUS_LED_BLINK_INTERVAL_MS) {
            lastStatusBlink = now;
            statusLedOn     = !statusLedOn;
            // Active LOW: LOW = on, HIGH = off
            digitalWrite(STATUS_LED_PIN, statusLedOn ? LOW : HIGH);
        }
    }
}
