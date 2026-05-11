import { describe, test } from 'node:test';
import assert from 'node:assert/strict';
import {
  hexToRgb, rgbToHex, buildPacket, percentLabel, parseTelemetry,
  PRESETS, MODES, DEFAULT_BRIGHTNESS,
  SERVICE_UUID, LED_CHAR_UUID, TELEMETRY_CHAR_UUID,
} from '../busylight-core.js';

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
