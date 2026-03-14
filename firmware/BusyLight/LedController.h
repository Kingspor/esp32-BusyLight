#pragma once

#include <Adafruit_NeoPixel.h>
#include "config.h"

// 6-byte LED command received via BLE
struct LedCommand {
    uint8_t r;
    uint8_t g;
    uint8_t b;
    uint8_t brightness;
    uint8_t mode;
    uint8_t speed;
};

// Controls the WS2812B LED ring with non-blocking animations.
// All animation state is managed internally using millis().
class LedController {
public:
    LedController();

    // Initialise the NeoPixel library and turn all LEDs off.
    void begin();

    // Parse a 6-byte BLE command packet and apply the new settings.
    // Resets animation state whenever the command changes.
    void setCommand(const uint8_t* data, size_t len);

    // Advance the current animation by one step (call from loop()).
    void update();

    // Turn all LEDs off immediately.
    void off();

private:
    Adafruit_NeoPixel _pixels;
    LedCommand        _cmd;

    // Animation position used by Chase and Rainbow modes
    int           _animPos;

    // Blink mode state
    bool          _blinkState;
    unsigned long _blinkLastTime;

    // Pulse mode state
    uint8_t       _pulseBrightness;
    bool          _pulseIncreasing;
    unsigned long _pulseLastTime;

    // General-purpose timestamp for Chase / Rainbow step timing
    unsigned long _stepLastTime;

    // Return the brightness value capped at BRIGHTNESS_CAP_FACTOR.
    uint8_t _cappedBrightness() const;

    // Set brightness and push pixel data to the ring.
    void _show();

    // Animation helpers
    void _applyStatic();
    void _applyPulse();
    void _applyChase();    // LED1-6 only; LED0 (centre) is skipped
    void _applyRainbow();
    void _applyBlink();
    void _applyFill();     // Fill/empty LED1-6 step-by-step; LED0 always on

    // Rainbow colour wheel: maps 0–255 to a full RGB spectrum.
    uint32_t _wheel(uint8_t pos);
};
