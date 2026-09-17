// Pure logic — no DOM, no BLE. Importable in both the browser and Node.js tests.

export const SERVICE_UUID       = 'feda0100-51a7-4fb7-a27b-c720bef16ef7';
export const LED_CHAR_UUID      = 'feda0101-51a7-4fb7-a27b-c720bef16ef7';
export const TELEMETRY_CHAR_UUID = 'feda0102-51a7-4fb7-a27b-c720bef16ef7';
export const STATE_CHAR_UUID    = 'feda0104-51a7-4fb7-a27b-c720bef16ef7';

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

/**
 * Convert r, g, b byte values to a CSS hex color string.
 * @param {number} r @param {number} g @param {number} b
 * @returns {string}  e.g. "#00c800"
 */
export function rgbToHex(r, g, b) {
  return '#' + [r, g, b].map(v => v.toString(16).padStart(2, '0')).join('');
}

/**
 * Parse the 3-byte telemetry DataView from the firmware.
 * Byte layout: [voltage_mv_lo, voltage_mv_hi, soc_percent]
 * @param {DataView} dataView
 * @returns {{ mv: number, soc: number }}
 */
export function parseTelemetry(dataView) {
  const mv  = dataView.getUint8(0) | (dataView.getUint8(1) << 8);
  const soc = dataView.getUint8(2);
  return { mv, soc };
}

/**
 * Parse the 6-byte state DataView — the command the ring is currently showing.
 * Same byte layout as the LED command packet, so the device reports its state
 * in exactly the format it accepts.
 * @param {DataView} dataView
 * @returns {{ r: number, g: number, b: number, brightness: number, mode: number, speed: number }}
 */
export function parseState(dataView) {
  return {
    r:          dataView.getUint8(0),
    g:          dataView.getUint8(1),
    b:          dataView.getUint8(2),
    brightness: dataView.getUint8(3),
    mode:       dataView.getUint8(4),
    speed:      dataView.getUint8(5),
  };
}

/** Animation mode whose colour bytes the firmware ignores. */
const RAINBOW_MODE = 3;

/**
 * How far two normalised colour channels may differ and still count as the same
 * colour.  Small on purpose: it absorbs palettes that disagree slightly (255,170,0
 * here against 255,165,0 in the Windows app for "abwesend") without letting two
 * distinct statuses collide.
 */
export const COLOUR_CHANNEL_TOLERANCE = 24;

/** True when the ring is dark, whatever colour is nominally set behind it. */
export function isDark(c) {
  return c.brightness === 0 || (c.r === 0 && c.g === 0 && c.b === 0);
}

/**
 * The colour scaled so its strongest channel is 255.  Strips intensity and leaves
 * the hue: 0,200,0 and 0,255,0 both become 0,255,0 — which is the same judgement a
 * person makes when calling both of them green.
 * @param {object} c Anything with r, g, b
 */
export function normalisedColour(c) {
  const max = Math.max(c.r, c.g, c.b);
  if (max === 0) return { r: 0, g: 0, b: 0 };
  return {
    r: Math.round((c.r * 255) / max),
    g: Math.round((c.g * 255) / max),
    b: Math.round((c.b * 255) / max),
  };
}

/**
 * True when two commands look the same on the ring.
 *
 * Not a byte comparison, deliberately.  The Windows app and this PWA do not share a
 * palette — it sends 0,255,0 for "available" where this app sends 0,200,0 — and
 * brightness is per-client taste: the tray applies its configured cap, the phone its
 * own slider.  Compared byte for byte, neither client would ever recognise a status
 * the other one set, which is the entire purpose of the state characteristic.
 */
export function sameAppearance(a, b) {
  if (!a || !b) return false;

  // A dark ring is a dark ring.  Which colour sits behind brightness 0 makes no
  // difference to anyone looking at it — and here too the clients disagree: "Aus" is
  // 0,0,0 in this app and blue-at-zero-brightness in the tray's configuration.
  if (isDark(a) || isDark(b)) return isDark(a) && isDark(b);

  // Two presets can share a colour and differ only in animation (Besetzt vs. Nicht
  // stören), so the mode still has to agree exactly.
  if (a.mode !== b.mode) return false;

  // Rainbow cycles the spectrum and ignores the colour bytes, so comparing them
  // would reject two rings that look identical.
  if (a.mode === RAINBOW_MODE) return true;

  const na = normalisedColour(a);
  const nb = normalisedColour(b);

  return Math.abs(na.r - nb.r) <= COLOUR_CHANNEL_TOLERANCE
      && Math.abs(na.g - nb.g) <= COLOUR_CHANNEL_TOLERANCE
      && Math.abs(na.b - nb.b) <= COLOUR_CHANNEL_TOLERANCE;
}

/**
 * Find which preset a device state corresponds to, so the UI can highlight it.
 * Returns null for a colour that is no preset — a manual one from the wheel, or one
 * the Windows app set from a status this app does not know.
 * @param {object} state    Result of parseState()
 * @param {object[]} presets Presets to match against — pass the user's edited set
 * @returns {string|null} The matching preset id, or null
 */
export function matchPreset(state, presets) {
  if (!state || !Array.isArray(presets)) return null;
  const found = presets.find(p => sameAppearance(p, state));
  return found ? found.id : null;
}

// ── Preset persistence ────────────────────────────────────────────────────────

const STORAGE_KEY = 'busylight-presets';

/** Load presets from localStorage, merged over the built-in defaults. */
export function loadPresets() {
  try {
    const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}');
    return PRESETS.map(p => saved[p.id] ? { ...p, ...saved[p.id] } : { ...p });
  } catch {
    return PRESETS.map(p => ({ ...p }));
  }
}

/** Persist a single edited preset to localStorage. */
export function savePreset(preset) {
  try {
    const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}');
    saved[preset.id] = preset;
    localStorage.setItem(STORAGE_KEY, JSON.stringify(saved));
  } catch {}
}

/**
 * Remove a preset override from localStorage, returning the original default.
 * @param {string} id
 * @returns {object} The default preset object
 */
export function resetPreset(id) {
  try {
    const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}');
    delete saved[id];
    localStorage.setItem(STORAGE_KEY, JSON.stringify(saved));
  } catch {}
  return { ...PRESETS.find(p => p.id === id) };
}

// ── Last-device persistence ───────────────────────────────────────────────────
// Web Bluetooth device IDs are opaque, origin-scoped handles.  Storing one lets
// us find the same device again in navigator.bluetooth.getDevices() and connect
// without showing the picker — the browser remembers the granted permission,
// we only have to remember which of the permitted devices was ours.

const LAST_DEVICE_KEY = 'busylight-last-device';

/**
 * Remember the device we are connected to, so the next app start can reconnect
 * to it silently.
 * @param {{ id: string, name?: string }} device  A BluetoothDevice (or a stub)
 */
export function saveLastDevice(device) {
  if (!device?.id) return;
  try {
    localStorage.setItem(LAST_DEVICE_KEY, JSON.stringify({
      id:   device.id,
      name: device.name || '',
    }));
  } catch {}
}

/**
 * Read the last successfully connected device.
 * @returns {{ id: string, name: string } | null}
 */
export function loadLastDevice() {
  try {
    const raw = localStorage.getItem(LAST_DEVICE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    return parsed?.id ? { id: parsed.id, name: parsed.name || '' } : null;
  } catch {
    return null;
  }
}

/** Forget the remembered device (used when the user disconnects deliberately). */
export function clearLastDevice() {
  try {
    localStorage.removeItem(LAST_DEVICE_KEY);
  } catch {}
}

// ── Reconnect backoff ─────────────────────────────────────────────────────────

// Delays between reconnect attempts, in ms.  Short at first so a brief radio
// glitch recovers almost unnoticed, then backing off to a 30 s poll that can
// run all day without draining the phone.
export const RECONNECT_DELAYS_MS = [1000, 2000, 4000, 8000, 15000, 30000];

/**
 * Delay before reconnect attempt number `attempt` (0-based).  Attempts beyond
 * the table stay at the last (longest) delay, so retrying never gives up.
 * @param {number} attempt
 * @returns {number} delay in ms
 */
export function reconnectDelayMs(attempt) {
  const i = Math.min(Math.max(attempt, 0), RECONNECT_DELAYS_MS.length - 1);
  return RECONNECT_DELAYS_MS[i];
}

// ── Connect watchdog ──────────────────────────────────────────────────────────

// How long a single connection attempt may run before it is given up on.
//
// gatt.connect() has no timeout of its own: against a device that is switched
// off, out of range or simply not advertising, the promise can stay pending
// indefinitely.  The reconnect chain schedules its next attempt from that
// promise settling, so one hung call is enough to stop the retries altogether —
// the phone then sits there looking like it is reconnecting and never does.
export const CONNECT_TIMEOUT_MS = 15000;

/**
 * Resolve/reject with `promise`, but reject after `ms` at the latest.
 *
 * The original promise is not cancellable — callers are expected to tear the
 * GATT connection down themselves so an attempt that arrives late cannot
 * collide with the next one.
 *
 * @template T
 * @param {Promise<T>} promise
 * @param {number} ms
 * @param {string} [message]
 * @returns {Promise<T>}
 */
export function withTimeout(promise, ms, message = 'Zeitüberschreitung') {
  let timer = null;
  const timeout = new Promise((_, reject) => {
    timer = setTimeout(() => reject(new Error(message)), ms);
  });
  return Promise.race([promise, timeout]).finally(() => clearTimeout(timer));
}
