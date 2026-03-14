#pragma once

// ============================================================
// Hardware pin definitions
// ============================================================
#define LED_PIN         5   // WS2812B data pin
#define LED_COUNT       7   // Number of LEDs in the ring
#define STATUS_LED_PIN  8   // Internal blue status LED (active LOW)

// ============================================================
// BLE configuration
// ============================================================
// Device name is generated at runtime from the BLE MAC address:
// "BusyLight-XXYY" where XX:YY = last two bytes of the BLE address (see GattServer.cpp)

// Advertising interval in units of 0.625 ms (1600 * 0.625 ms = 1000 ms)
#define BLE_ADV_INTERVAL_MIN  1600
#define BLE_ADV_INTERVAL_MAX  1600

// BLE service and characteristic UUIDs
#define SERVICE_UUID          "feda0100-51a7-4fb7-a27b-c720bef16ef7"
#define LED_CHAR_UUID         "feda0101-51a7-4fb7-a27b-c720bef16ef7"  // WRITE | WRITE_NO_RESPONSE
#define TELEMETRY_CHAR_UUID   "feda0102-51a7-4fb7-a27b-c720bef16ef7"  // READ | NOTIFY (stub)
#define PROTOCOL_VER_CHAR_UUID "feda0103-51a7-4fb7-a27b-c720bef16ef7" // READ — protocol version byte

// ============================================================
// Protocol versioning
// ============================================================
// Increment PROTOCOL_VERSION only on BREAKING changes to the BLE communication:
//   - Packet size changes (CMD_PACKET_SIZE)
//   - Byte positions are reordered or repurposed
//   - Service or characteristic UUIDs change
//   - New mandatory characteristics are added
//
// Adding new animation modes is BACKWARDS-COMPATIBLE — do NOT increment.
//
// Version history:
//   1  (v0.1.0)  Initial release: 6-byte command packet (R,G,B,Brightness,Mode,Speed)
#define PROTOCOL_VERSION  1

// ============================================================
// LED command packet layout (6 bytes)
// ============================================================
#define CMD_BYTE_R          0
#define CMD_BYTE_G          1
#define CMD_BYTE_B          2
#define CMD_BYTE_BRIGHTNESS 3
#define CMD_BYTE_MODE       4
#define CMD_BYTE_SPEED      5
#define CMD_PACKET_SIZE     6

// ============================================================
// Animation mode identifiers
// ============================================================
#define MODE_STATIC   0   // Solid color
#define MODE_PULSE    1   // Fade in/out
#define MODE_CHASE    2   // Single LED chasing around ring (LED1-6, LED0 skipped)
#define MODE_RAINBOW  3   // Rainbow spectrum rotation
#define MODE_BLINK    4   // All LEDs blinking on/off
#define MODE_FILL     5   // Fill/empty ring (LED1-6 step-by-step); LED0 always on

// ============================================================
// Power / brightness settings
// ============================================================
// Brightness cap is applied on the app side (appsettings.json → Polling.BrightnessCap).
// The firmware trusts the received value without further modification.
#define BRIGHTNESS_CAP_FACTOR  1.0f

// Status LED blink half-period when no client is connected (ms)
#define STATUS_LED_BLINK_INTERVAL_MS  500
