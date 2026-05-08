// Pure logic — no DOM, no BLE. Importable in both the browser and Node.js tests.

export const SERVICE_UUID  = 'feda0100-51a7-4fb7-a27b-c720bef16ef7';
export const LED_CHAR_UUID = 'feda0101-51a7-4fb7-a27b-c720bef16ef7';

// Brightness cap default: 153 / 255 ≈ 60 %, mirrors PollingSettings.BrightnessCap = 0.6
export const DEFAULT_BRIGHTNESS = 153;

export const PRESETS = [
  { id: 'available', label: 'Verfügbar',    r: 0,   g: 200, b: 0,   brightness: DEFAULT_BRIGHTNESS, mode: 0, speed: 128, bg: '#16a34a' },
  { id: 'busy',      label: 'Besetzt',       r: 200, g: 0,   b: 0,   brightness: DEFAULT_BRIGHTNESS, mode: 0, speed: 128, bg: '#dc2626' },
  { id: 'dnd',       label: 'Nicht stören',  r: 200, g: 0,   b: 0,   brightness: DEFAULT_BRIGHTNESS, mode: 1, speed: 80,  bg: '#9f1239' },
  { id: 'away',      label: 'Abwesend',      r: 255, g: 170, b: 0,   brightness: DEFAULT_BRIGHTNESS, mode: 1, speed: 80,  bg: '#b45309' },
  { id: 'brb',       label: 'Gleich zurück', r: 255, g: 170, b: 0,   brightness: DEFAULT_BRIGHTNESS, mode: 4, speed: 120, bg: '#92400e' },
  { id: 'off',       label: 'Aus',           r: 0,   g: 0,   b: 0,   brightness: 0,                  mode: 0, speed: 0,   bg: '#374151' },
];

export const MODES = [
  { id: 0, label: 'Statisch'   },
  { id: 1, label: 'Pulsieren'  },
  { id: 2, label: 'Chase'      },
  { id: 3, label: 'Regenbogen' },
  { id: 4, label: 'Blinken'    },
  { id: 5, label: 'Füllen'     },
];

/**
 * Parse a CSS hex color string ("#rrggbb") to {r, g, b} byte values.
 * @param {string} hex
 * @returns {{ r: number, g: number, b: number }}
 */
export function hexToRgb(hex) {
  return {
    r: parseInt(hex.slice(1, 3), 16),
    g: parseInt(hex.slice(3, 5), 16),
    b: parseInt(hex.slice(5, 7), 16),
  };
}

/**
 * Build the 6-byte BLE command packet.
 * Byte order: [R, G, B, Brightness, Mode, Speed]  (see config.h CMD_BYTE_* constants)
 * @returns {Uint8Array}
 */
export function buildPacket(r, g, b, brightness, mode, speed) {
  return new Uint8Array([r, g, b, brightness, mode, speed]);
}

/**
 * Format a slider value as a percentage string, e.g. "60 %".
 * @param {number} value  Current slider value
 * @param {number} max    Slider maximum
 * @returns {string}
 */
export function percentLabel(value, max) {
  return Math.round((value / max) * 100) + ' %';
}
