#include "LedController.h"

// ============================================================
// Constructor / initialisation
// ============================================================

LedController::LedController()
    : _pixels(LED_COUNT, LED_PIN, NEO_GRB + NEO_KHZ800)
{
}

void LedController::begin() {
    _pixels.begin();
    _pixels.clear();
    _pixels.show();
}

// ============================================================
// Public API
// ============================================================

void LedController::setCommand(const uint8_t* data, size_t len) {
    if (len < CMD_PACKET_SIZE) return;

    _cmd.r          = data[CMD_BYTE_R];
    _cmd.g          = data[CMD_BYTE_G];
    _cmd.b          = data[CMD_BYTE_B];
    _cmd.brightness = data[CMD_BYTE_BRIGHTNESS];
    _cmd.mode       = data[CMD_BYTE_MODE];
    _cmd.speed      = data[CMD_BYTE_SPEED];

    // Reset all animation state so each mode starts cleanly
    _animPos         = 0;
    _blinkState      = false;
    _blinkLastTime   = 0;
    _pulseBrightness = 0;
    _pulseIncreasing = true;
    _pulseLastTime   = 0;
    _stepLastTime    = 0;

    // Apply static mode immediately so the colour appears without delay
    if (_cmd.mode == MODE_STATIC) {
        _applyStatic();
    }
}

void LedController::update() {
    switch (_cmd.mode) {
        case MODE_STATIC:   _applyStatic();  break;
        case MODE_PULSE:    _applyPulse();   break;
        case MODE_CHASE:    _applyChase();   break;
        case MODE_RAINBOW:  _applyRainbow(); break;
        case MODE_BLINK:    _applyBlink();   break;
        case MODE_FILL:     _applyFill();    break;
        default:            _applyStatic();  break;
    }
}

void LedController::off() {
    _pixels.clear();
    _pixels.show();
}

// ============================================================
// Private helpers
// ============================================================

uint8_t LedController::_cappedBrightness() const {
    return static_cast<uint8_t>(_cmd.brightness * BRIGHTNESS_CAP_FACTOR);
}

void LedController::_show() {
    _pixels.setBrightness(_cappedBrightness());
    _pixels.show();
}

// ─── Mode 0: Static ────────────────────────────────────────

void LedController::_applyStatic() {
    for (int i = 0; i < LED_COUNT; i++) {
        _pixels.setPixelColor(i, _pixels.Color(_cmd.r, _cmd.g, _cmd.b));
    }
    _show();
}

// ─── Mode 1: Pulse ─────────────────────────────────────────
// Brightness ramps up from 0 to the capped max, then back down.
// INTERVAL = map(speed, 0, 255, 30, 2) ms — higher speed → shorter interval → faster pulse.

void LedController::_applyPulse() {
    unsigned long now      = millis();
    unsigned long interval = map(_cmd.speed, 0, 255, 30, 2);
    uint8_t       maxBri   = _cappedBrightness();

    if (maxBri == 0) {
        // Nothing to show if brightness is zero
        _pixels.clear();
        _pixels.show();
        return;
    }

    if (now - _pulseLastTime >= interval) {
        _pulseLastTime = now;

        if (_pulseIncreasing) {
            _pulseBrightness++;
            if (_pulseBrightness >= maxBri) {
                _pulseBrightness = maxBri;
                _pulseIncreasing = false;
            }
        } else {
            if (_pulseBrightness > 0) {
                _pulseBrightness--;
            }
            if (_pulseBrightness == 0) {
                _pulseIncreasing = true;
            }
        }

        for (int i = 0; i < LED_COUNT; i++) {
            _pixels.setPixelColor(i, _pixels.Color(_cmd.r, _cmd.g, _cmd.b));
        }
        // Use setBrightness directly for smooth pulse; ignore _show() to avoid
        // applying the cap a second time — _pulseBrightness is already capped.
        _pixels.setBrightness(_pulseBrightness);
        _pixels.show();
    }
}

// ─── Mode 2: Chase ─────────────────────────────────────────
// A single lit LED advances around the ring LEDs (LED1–6).
// LED0 (center) is always skipped.
// _animPos tracks 0–5 → maps to physical LEDs 1–6.
// INTERVAL = map(speed, 0, 255, 500, 20) ms.

void LedController::_applyChase() {
    unsigned long now      = millis();
    unsigned long interval = map(_cmd.speed, 0, 255, 500, 20);

    if (now - _stepLastTime >= interval) {
        _stepLastTime = now;
        _animPos = (_animPos + 1) % (LED_COUNT - 1);  // 0–5 (skips LED0)

        _pixels.clear();
        _pixels.setPixelColor(_animPos + 1, _pixels.Color(_cmd.r, _cmd.g, _cmd.b));  // LEDs 1–6
        _show();
    }
}

// ─── Mode 3: Rainbow ───────────────────────────────────────
// All LEDs display a hue-spread that rotates around the ring.
// Uses the Wheel() colour mapping algorithm (0–255 → RGB).
// INTERVAL = map(speed, 0, 255, 30, 2) ms.

void LedController::_applyRainbow() {
    unsigned long now      = millis();
    unsigned long interval = map(_cmd.speed, 0, 255, 30, 2);

    if (now - _stepLastTime >= interval) {
        _stepLastTime = now;
        _animPos = (_animPos + 1) & 0xFF;  // wrap 0–255

        for (int i = 0; i < LED_COUNT; i++) {
            auto hue = static_cast<uint8_t>((i * 256 / LED_COUNT + _animPos) & 0xFF);
            _pixels.setPixelColor(i, _wheel(hue));
        }
        _show();
    }
}

// ─── Mode 4: Blink ─────────────────────────────────────────
// All LEDs toggle on/off each interval.
// INTERVAL = map(speed, 0, 255, 1000, 100) ms.

void LedController::_applyBlink() {
    unsigned long now      = millis();
    unsigned long interval = map(_cmd.speed, 0, 255, 1000, 100);

    if (now - _blinkLastTime >= interval) {
        _blinkLastTime = now;
        _blinkState    = !_blinkState;

        if (_blinkState) {
            for (int i = 0; i < LED_COUNT; i++) {
                _pixels.setPixelColor(i, _pixels.Color(_cmd.r, _cmd.g, _cmd.b));
            }
            _show();
        } else {
            _pixels.clear();
            _pixels.show();
        }
    }
}

// ─── Mode 5: Fill ──────────────────────────────────────────
// LED0 (center) stays lit at all times.
// A lit block sweeps through the ring (LED1–6) like a wipe:
//   Fill phase  (pos 0–6):  right edge advances  → block grows  from left
//   Empty phase (pos 7–11): left  edge advances  → block shrinks from left
// _animPos cycles 0–11 (12 steps):
//   head = min(pos, 6)      — rightmost lit LED index (1-based)
//   tail = max(pos - 6, 0)  — first lit LED index minus 1
//   LEDs tail+1 .. head are lit, all others off
// Example: pos=7 → head=6, tail=1 → LEDs 2–6 lit (○●●●●●)
// INTERVAL = map(speed, 0, 255, 500, 20) ms.

void LedController::_applyFill() {
    unsigned long now      = millis();
    unsigned long interval = map(_cmd.speed, 0, 255, 500, 20);

    if (now - _stepLastTime >= interval) {
        _stepLastTime = now;

        _pixels.clear();

        // LED0 (center) always on
        _pixels.setPixelColor(0, _pixels.Color(_cmd.r, _cmd.g, _cmd.b));

        // Sliding window: grow right edge first, then advance left edge
        int head = (_animPos < 6) ? _animPos : 6;
        int tail = (_animPos > 6) ? (_animPos - 6) : 0;

        for (int i = tail + 1; i <= head; i++) {
            _pixels.setPixelColor(i, _pixels.Color(_cmd.r, _cmd.g, _cmd.b));
        }

        _show();

        _animPos = (_animPos + 1) % 12;
    }
}

// ─── Rainbow colour wheel ──────────────────────────────────
// Maps an 8-bit position (0–255) to a colour spanning the full
// RGB spectrum using three linear segments.

uint32_t LedController::_wheel(uint8_t pos) {
    if (pos < 85) {
        return _pixels.Color(pos * 3, 255 - pos * 3, 0);
    } else if (pos < 170) {
        pos -= 85;
        return _pixels.Color(255 - pos * 3, 0, pos * 3);
    } else {
        pos -= 170;
        return _pixels.Color(0, pos * 3, 255 - pos * 3);
    }
}
