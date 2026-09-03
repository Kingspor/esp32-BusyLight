import { describe, test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import {
  hexToRgb, rgbToHex, buildPacket, percentLabel, parseTelemetry,
  PRESETS, MODES, DEFAULT_BRIGHTNESS,
  SERVICE_UUID, LED_CHAR_UUID, TELEMETRY_CHAR_UUID,
  saveLastDevice, loadLastDevice, clearLastDevice,
  reconnectDelayMs, RECONNECT_DELAYS_MS,
  parseState, matchPreset, STATE_CHAR_UUID,
} from '../busylight-core.js';

// Node has no localStorage without --experimental-webstorage, so the storage
// helpers get a minimal in-memory stand-in.  busylight-core.js only touches
// localStorage inside function bodies, never at import time, so installing the
// stub after the (hoisted) import is safe.
function installStorageStub() {
  const store = new Map();
  globalThis.localStorage = {
    getItem:    key => (store.has(key) ? store.get(key) : null),
    setItem:    (key, value) => { store.set(key, String(value)); },
    removeItem: key => { store.delete(key); },
    clear:      () => { store.clear(); },
  };
  return store;
}

// ── hexToRgb ──────────────────────────────────────────────────────────────────
describe('hexToRgb', () => {
  test('parses available-green', () => {
    assert.deepEqual(hexToRgb('#00c800'), { r: 0, g: 200, b: 0 });
  });
  test('parses busy-red', () => {
    assert.deepEqual(hexToRgb('#c80000'), { r: 200, g: 0, b: 0 });
  });
  test('parses white', () => {
    assert.deepEqual(hexToRgb('#ffffff'), { r: 255, g: 255, b: 255 });
  });
  test('parses black / off', () => {
    assert.deepEqual(hexToRgb('#000000'), { r: 0, g: 0, b: 0 });
  });
  test('parses away-orange', () => {
    assert.deepEqual(hexToRgb('#ffaa00'), { r: 255, g: 170, b: 0 });
  });
});

// ── buildPacket ───────────────────────────────────────────────────────────────
describe('buildPacket', () => {
  test('returns a Uint8Array', () => {
    const pkt = buildPacket(0, 200, 0, 153, 0, 128);
    assert.ok(pkt instanceof Uint8Array);
  });

  test('length is exactly 6 bytes (CMD_PACKET_SIZE)', () => {
    assert.equal(buildPacket(0, 0, 0, 0, 0, 0).length, 6);
  });

  test('byte order matches config.h: R G B Brightness Mode Speed', () => {
    const pkt = buildPacket(1, 2, 3, 4, 5, 6);
    assert.deepEqual([...pkt], [1, 2, 3, 4, 5, 6]);
  });

  test('off command is all zeros', () => {
    assert.deepEqual([...buildPacket(0, 0, 0, 0, 0, 0)], [0, 0, 0, 0, 0, 0]);
  });

  test('available preset packet round-trips correctly', () => {
    const p   = PRESETS.find(x => x.id === 'available');
    const pkt = buildPacket(p.r, p.g, p.b, p.brightness, p.mode, p.speed);
    assert.equal(pkt[0], p.r);
    assert.equal(pkt[1], p.g);
    assert.equal(pkt[2], p.b);
    assert.equal(pkt[3], p.brightness);
    assert.equal(pkt[4], p.mode);
    assert.equal(pkt[5], p.speed);
  });
});

// ── percentLabel ──────────────────────────────────────────────────────────────
describe('percentLabel', () => {
  test('0 / 255 → "0 %"',   () => assert.equal(percentLabel(0,   255), '0 %'));
  test('255/255 → "100 %"', () => assert.equal(percentLabel(255, 255), '100 %'));
  test('153/255 → "60 %"',  () => assert.equal(percentLabel(153, 255), '60 %'));
  test('128/255 → "50 %"',  () => assert.equal(percentLabel(128, 255), '50 %'));
  test('rounds correctly',  () => assert.equal(percentLabel(1,   255), '0 %'));
});

// ── PRESETS ───────────────────────────────────────────────────────────────────
describe('PRESETS', () => {
  test('has exactly 6 entries', () => {
    assert.equal(PRESETS.length, 6);
  });

  test('all byte fields are in range 0–255', () => {
    for (const p of PRESETS) {
      for (const field of ['r', 'g', 'b', 'brightness', 'mode', 'speed']) {
        const v = p[field];
        assert.ok(
          Number.isInteger(v) && v >= 0 && v <= 255,
          `${p.id}.${field} = ${v} is out of byte range`,
        );
      }
    }
  });

  test('off preset has brightness 0 (LEDs dark)', () => {
    const off = PRESETS.find(p => p.id === 'off');
    assert.equal(off.brightness, 0);
    assert.equal(off.r, 0);
    assert.equal(off.g, 0);
    assert.equal(off.b, 0);
  });

  test('default brightness matches DEFAULT_BRIGHTNESS constant', () => {
    const coloured = PRESETS.filter(p => p.id !== 'off');
    for (const p of coloured) {
      assert.equal(p.brightness, DEFAULT_BRIGHTNESS, `${p.id} brightness mismatch`);
    }
  });

  test('DND uses pulse mode (mode 1), not static', () => {
    const dnd = PRESETS.find(p => p.id === 'dnd');
    assert.equal(dnd.mode, 1);
  });

  test('BRB uses blink mode (mode 4)', () => {
    const brb = PRESETS.find(p => p.id === 'brb');
    assert.equal(brb.mode, 4);
  });

  test('each preset has a unique id', () => {
    const ids = PRESETS.map(p => p.id);
    assert.equal(new Set(ids).size, ids.length);
  });
});

// ── MODES ─────────────────────────────────────────────────────────────────────
describe('MODES', () => {
  test('has exactly 6 entries (modes 0–5)', () => {
    assert.equal(MODES.length, 6);
  });

  test('mode IDs are consecutive 0–5', () => {
    assert.deepEqual(MODES.map(m => m.id), [0, 1, 2, 3, 4, 5]);
  });

  test('each mode has a non-empty label', () => {
    for (const m of MODES) {
      assert.ok(m.label.length > 0, `mode ${m.id} has no label`);
    }
  });
});

// ── rgbToHex ──────────────────────────────────────────────────────────────────
describe('rgbToHex', () => {
  test('converts available-green', () => {
    assert.equal(rgbToHex(0, 200, 0), '#00c800');
  });
  test('converts busy-red', () => {
    assert.equal(rgbToHex(200, 0, 0), '#c80000');
  });
  test('converts white', () => {
    assert.equal(rgbToHex(255, 255, 255), '#ffffff');
  });
  test('converts black', () => {
    assert.equal(rgbToHex(0, 0, 0), '#000000');
  });
  test('is the inverse of hexToRgb', () => {
    const original = '#ffaa00';
    const { r, g, b } = hexToRgb(original);
    assert.equal(rgbToHex(r, g, b), original);
  });
});

// ── parseTelemetry ────────────────────────────────────────────────────────────
describe('parseTelemetry', () => {
  function makeDV(bytes) {
    return new DataView(new Uint8Array(bytes).buffer);
  }

  test('parses voltage low byte correctly', () => {
    // 3700 mV = 0x0E74 → lo=0x74, hi=0x0E
    const { mv } = parseTelemetry(makeDV([0x74, 0x0E, 40]));
    assert.equal(mv, 3700);
  });

  test('parses soc percent', () => {
    const { soc } = parseTelemetry(makeDV([0x74, 0x0E, 73]));
    assert.equal(soc, 73);
  });

  test('full charge: 4200 mV, 100 %', () => {
    const mv4200 = 4200; // 0x1068 → lo=0x68, hi=0x10
    const { mv, soc } = parseTelemetry(makeDV([0x68, 0x10, 100]));
    assert.equal(mv, mv4200);
    assert.equal(soc, 100);
  });

  test('empty: 0 mV, 0 %', () => {
    const { mv, soc } = parseTelemetry(makeDV([0, 0, 0]));
    assert.equal(mv, 0);
    assert.equal(soc, 0);
  });
});

// ── Constants ─────────────────────────────────────────────────────────────────
describe('BLE UUIDs', () => {
  const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

  test('SERVICE_UUID is a valid UUID', () => {
    assert.match(SERVICE_UUID, UUID_RE);
  });

  test('LED_CHAR_UUID is a valid UUID', () => {
    assert.match(LED_CHAR_UUID, UUID_RE);
  });

  test('TELEMETRY_CHAR_UUID is a valid UUID', () => {
    assert.match(TELEMETRY_CHAR_UUID, UUID_RE);
  });

  test('all UUIDs share the same base (feda01xx)', () => {
    assert.ok(SERVICE_UUID.startsWith('feda01'));
    assert.ok(LED_CHAR_UUID.startsWith('feda01'));
    assert.ok(TELEMETRY_CHAR_UUID.startsWith('feda01'));
  });

  test('TELEMETRY_CHAR_UUID differs from LED_CHAR_UUID', () => {
    assert.notEqual(TELEMETRY_CHAR_UUID, LED_CHAR_UUID);
  });
});

// ── Last-device persistence ───────────────────────────────────────────────────
describe('last-device persistence', () => {
  let store;
  beforeEach(() => { store = installStorageStub(); });

  test('returns null when nothing was ever saved', () => {
    assert.equal(loadLastDevice(), null);
  });

  test('round-trips id and name', () => {
    saveLastDevice({ id: 'abc123', name: 'BusyLight-4F2A' });
    assert.deepEqual(loadLastDevice(), { id: 'abc123', name: 'BusyLight-4F2A' });
  });

  test('a nameless device still round-trips its id', () => {
    saveLastDevice({ id: 'abc123' });
    assert.deepEqual(loadLastDevice(), { id: 'abc123', name: '' });
  });

  test('ignores a device without an id', () => {
    saveLastDevice({ name: 'BusyLight-4F2A' });
    assert.equal(loadLastDevice(), null);
  });

  test('ignores null / undefined', () => {
    saveLastDevice(null);
    saveLastDevice(undefined);
    assert.equal(loadLastDevice(), null);
  });

  test('saving twice keeps only the newer device', () => {
    saveLastDevice({ id: 'first',  name: 'BusyLight-0001' });
    saveLastDevice({ id: 'second', name: 'BusyLight-0002' });
    assert.equal(loadLastDevice().id, 'second');
  });

  test('clearLastDevice forgets the device', () => {
    saveLastDevice({ id: 'abc123', name: 'BusyLight-4F2A' });
    clearLastDevice();
    assert.equal(loadLastDevice(), null);
  });

  test('corrupt JSON in storage does not throw', () => {
    store.set('busylight-last-device', '{not json');
    assert.equal(loadLastDevice(), null);
  });

  test('a stored entry without an id is treated as absent', () => {
    store.set('busylight-last-device', JSON.stringify({ name: 'BusyLight' }));
    assert.equal(loadLastDevice(), null);
  });

  test('presets and last device use separate storage keys', () => {
    saveLastDevice({ id: 'abc123', name: 'BusyLight-4F2A' });
    assert.ok(store.has('busylight-last-device'));
    assert.ok(!store.has('busylight-presets'));
  });

  test('survives storage that throws (private mode / disabled)', () => {
    globalThis.localStorage = {
      getItem:    () => { throw new Error('denied'); },
      setItem:    () => { throw new Error('denied'); },
      removeItem: () => { throw new Error('denied'); },
    };
    assert.doesNotThrow(() => saveLastDevice({ id: 'x', name: 'y' }));
    assert.doesNotThrow(() => clearLastDevice());
    assert.equal(loadLastDevice(), null);
  });
});

// ── Reconnect backoff ─────────────────────────────────────────────────────────
describe('reconnectDelayMs', () => {
  test('first attempt retries after 1 s', () => {
    assert.equal(reconnectDelayMs(0), 1000);
  });

  test('delays grow monotonically', () => {
    for (let i = 1; i < RECONNECT_DELAYS_MS.length; i++) {
      assert.ok(
        RECONNECT_DELAYS_MS[i] > RECONNECT_DELAYS_MS[i - 1],
        `delay ${i} (${RECONNECT_DELAYS_MS[i]}) must exceed the previous one`,
      );
    }
  });

  test('follows the table for every listed attempt', () => {
    RECONNECT_DELAYS_MS.forEach((expected, i) => {
      assert.equal(reconnectDelayMs(i), expected, `attempt ${i}`);
    });
  });

  test('caps at the longest delay instead of giving up', () => {
    const cap = RECONNECT_DELAYS_MS[RECONNECT_DELAYS_MS.length - 1];
    assert.equal(reconnectDelayMs(RECONNECT_DELAYS_MS.length),      cap);
    assert.equal(reconnectDelayMs(RECONNECT_DELAYS_MS.length + 50), cap);
    assert.equal(reconnectDelayMs(9999),                            cap);
  });

  test('cap stays at 30 s so all-day retrying is cheap', () => {
    assert.equal(reconnectDelayMs(9999), 30000);
  });

  test('a negative attempt falls back to the first delay', () => {
    assert.equal(reconnectDelayMs(-1), 1000);
  });

  test('every delay is a positive finite number', () => {
    for (const d of RECONNECT_DELAYS_MS) {
      assert.ok(Number.isFinite(d) && d > 0, `${d} is not a usable delay`);
    }
  });
});

// ── parseState ────────────────────────────────────────────────────────────────
describe('parseState', () => {
  const dv = bytes => new DataView(new Uint8Array(bytes).buffer);

  test('reads all six bytes in packet order', () => {
    assert.deepEqual(parseState(dv([1, 2, 3, 4, 5, 6])), {
      r: 1, g: 2, b: 3, brightness: 4, mode: 5, speed: 6,
    });
  });

  test('round-trips a packet built by buildPacket', () => {
    const p   = PRESETS.find(x => x.id === 'busy');
    const pkt = buildPacket(p.r, p.g, p.b, p.brightness, p.mode, p.speed);
    const st  = parseState(new DataView(pkt.buffer));
    assert.equal(st.r, p.r);
    assert.equal(st.g, p.g);
    assert.equal(st.b, p.b);
    assert.equal(st.brightness, p.brightness);
    assert.equal(st.mode, p.mode);
    assert.equal(st.speed, p.speed);
  });

  test('an all-zero state parses to zeros, not to undefined', () => {
    assert.deepEqual(parseState(dv([0, 0, 0, 0, 0, 0])), {
      r: 0, g: 0, b: 0, brightness: 0, mode: 0, speed: 0,
    });
  });

  test('accepts full-byte values', () => {
    const st = parseState(dv([255, 255, 255, 255, 255, 255]));
    assert.equal(st.r, 255);
    assert.equal(st.speed, 255);
  });
});

// ── matchPreset ───────────────────────────────────────────────────────────────
describe('matchPreset', () => {
  const stateOf = p => ({
    r: p.r, g: p.g, b: p.b, brightness: p.brightness, mode: p.mode, speed: p.speed,
  });

  test('finds every built-in preset from its own state', () => {
    for (const p of PRESETS) {
      assert.equal(matchPreset(stateOf(p), PRESETS), p.id, `preset ${p.id}`);
    }
  });

  test('distinguishes presets that share a colour but differ in mode', () => {
    // busy and dnd are both (200,0,0) — only mode and speed separate them
    const busy = PRESETS.find(p => p.id === 'busy');
    const dnd  = PRESETS.find(p => p.id === 'dnd');
    assert.equal(busy.r, dnd.r);
    assert.equal(busy.g, dnd.g);
    assert.equal(busy.b, dnd.b);
    assert.equal(matchPreset(stateOf(busy), PRESETS), 'busy');
    assert.equal(matchPreset(stateOf(dnd),  PRESETS), 'dnd');
  });

  test('returns null for a manual colour that is no preset', () => {
    const manual = { r: 12, g: 34, b: 56, brightness: 100, mode: 0, speed: 50 };
    assert.equal(matchPreset(manual, PRESETS), null);
  });

  test('a single differing byte prevents a match', () => {
    const p = PRESETS.find(x => x.id === 'available');
    for (const field of ['r', 'g', 'b', 'brightness', 'mode', 'speed']) {
      const off = { ...stateOf(p), [field]: stateOf(p)[field] === 7 ? 8 : 7 };
      assert.equal(matchPreset(off, PRESETS), null, `differing ${field} still matched`);
    }
  });

  test('matches against edited presets, not the built-in defaults', () => {
    const edited = PRESETS.map(p =>
      p.id === 'busy' ? { ...p, r: 111, g: 22, b: 33 } : { ...p }
    );
    const editedBusy = edited.find(p => p.id === 'busy');
    assert.equal(matchPreset(stateOf(editedBusy), edited), 'busy');
    // The old default must no longer match the edited set
    assert.equal(matchPreset(stateOf(PRESETS.find(p => p.id === 'busy')), edited), null);
  });

  test('an all-zero state matches the off preset', () => {
    const zero = { r: 0, g: 0, b: 0, brightness: 0, mode: 0, speed: 0 };
    assert.equal(matchPreset(zero, PRESETS), 'off');
  });

  test('survives null / malformed input', () => {
    assert.equal(matchPreset(null, PRESETS), null);
    assert.equal(matchPreset(undefined, PRESETS), null);
    assert.equal(matchPreset({ r: 0 }, null), null);
    assert.equal(matchPreset({ r: 0 }, undefined), null);
  });
});

// ── STATE_CHAR_UUID ───────────────────────────────────────────────────────────
describe('STATE_CHAR_UUID', () => {
  test('is a valid UUID on the shared base', () => {
    assert.match(STATE_CHAR_UUID, /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
    assert.ok(STATE_CHAR_UUID.startsWith('feda01'));
  });

  test('does not collide with the other characteristics', () => {
    const all = [SERVICE_UUID, LED_CHAR_UUID, TELEMETRY_CHAR_UUID, STATE_CHAR_UUID];
    assert.equal(new Set(all).size, all.length);
  });
});
