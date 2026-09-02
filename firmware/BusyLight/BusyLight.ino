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

// ── Disconnect hold state ─────────────────────────────────────────────────────
// After the link drops we keep animating the last received command instead of
// blanking the ring, so a dropped connection is never mistaken for "available".
// See LED_HOLD_AFTER_DISCONNECT_MS in config.h for the rationale.

static bool          wasConnected  = false;  // link state on the previous tick
static bool          holdActive    = false;  // showing the last command post-disconnect
static unsigned long holdStartedMs = 0;      // millis() at the moment the link dropped

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

    const bool connected = bleServer.isConnected();

    if (connected) {
        // Client connected: run LED animations, status LED solid ON
        ledController.update();
        bleServer.updateTelemetry();
        digitalWrite(STATUS_LED_PIN, LOW);  // Active LOW = LED on
    } else {
        // Link just dropped — start the hold window.  A hold is only armed if a
        // client was actually connected before, so a fresh boot with no client
        // leaves the ring dark rather than lighting up an all-zero command.
        if (wasConnected) {
            holdActive    = true;
            holdStartedMs = millis();
            Serial.printf("[LED] Link lost — holding last status for %lu s.\n",
                          LED_HOLD_AFTER_DISCONNECT_MS / 1000);
        }

        if (holdActive && millis() - holdStartedMs >= LED_HOLD_AFTER_DISCONNECT_MS) {
            holdActive = false;
            Serial.println("[LED] Hold window expired — ring off to save battery.");
        }

        // Keep showing the last command during the hold window, then go dark.
        if (holdActive) ledController.update();
        else            ledController.off();

        // Status LED blinks at 1 Hz for the whole disconnected period
        unsigned long now = millis();
        if (now - lastStatusBlink >= STATUS_LED_BLINK_INTERVAL_MS) {
            lastStatusBlink = now;
            statusLedOn     = !statusLedOn;
            // Active LOW: LOW = on, HIGH = off
            digitalWrite(STATUS_LED_PIN, statusLedOn ? LOW : HIGH);
        }
    }

    wasConnected = connected;
}
