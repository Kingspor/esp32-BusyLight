import {
  SERVICE_UUID, LED_CHAR_UUID,
  PRESETS, MODES,
  hexToRgb, buildPacket, percentLabel,
  DEFAULT_BRIGHTNESS,
} from './busylight-core.js';

// ── State ────────────────────────────────────────────────────────────────────
let bleDevice    = null;
let ledChar      = null;
let selectedMode = 0;
let toastTimer   = null;

// ── Boot ─────────────────────────────────────────────────────────────────────
function init() {
  if (!navigator.bluetooth) {
    document.getElementById('bleNotice').style.display = 'block';
    document.getElementById('connectBtn').disabled = true;
  }

  buildPresetGrid();
  buildModeGrid();
  setConnectionState('disconnected');
  wireSliders();
  document.getElementById('connectBtn').addEventListener('click', toggleConnection);
  document.getElementById('sendBtn').addEventListener('click', sendManual);

  if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('sw.js').catch(() => {});
  }
}

function buildPresetGrid() {
  const grid = document.getElementById('presetGrid');
  PRESETS.forEach(p => {
    const btn = document.createElement('button');
    btn.className        = 'preset-btn';
    btn.id               = `preset-${p.id}`;
    btn.disabled         = true;
    btn.style.background = p.bg;
    btn.innerHTML        = `<div class="preset-dot"></div>${p.label}`;
    btn.addEventListener('click', () => sendPreset(p));
    grid.appendChild(btn);
  });
}

function buildModeGrid() {
  const grid = document.getElementById('modeGrid');
  MODES.forEach(m => {
    const btn = document.createElement('button');
    btn.className   = 'mode-btn' + (m.id === 0 ? ' selected' : '');
    btn.textContent = m.label;
    btn.addEventListener('click', () => selectMode(m.id));
    grid.appendChild(btn);
  });
}

function wireSliders() {
  const brightness = document.getElementById('brightness');
  const speed      = document.getElementById('speed');

  brightness.value = DEFAULT_BRIGHTNESS;
  brightness.addEventListener('input', () =>
    setLabel('brightnessVal', percentLabel(brightness.value, 255))
  );
  setLabel('brightnessVal', percentLabel(brightness.value, 255));

  speed.addEventListener('input', () =>
    setLabel('speedVal', percentLabel(speed.value, 255))
  );
  setLabel('speedVal', percentLabel(speed.value, 255));
}

// ── Connection ────────────────────────────────────────────────────────────────
async function toggleConnection() {
  if (bleDevice?.gatt?.connected) {
    bleDevice.gatt.disconnect();
  } else {
    await connect();
  }
}

async function connect() {
  try {
    setConnectionState('connecting');

    bleDevice = await navigator.bluetooth.requestDevice({
      filters:          [{ namePrefix: 'BusyLight' }],
      optionalServices: [SERVICE_UUID],
    });

    bleDevice.addEventListener('gattserverdisconnected', onDisconnected);

    const server  = await bleDevice.gatt.connect();
    const service = await server.getPrimaryService(SERVICE_UUID);
    ledChar        = await service.getCharacteristic(LED_CHAR_UUID);

    setConnectionState('connected', bleDevice.name);
    showToast(`Verbunden mit ${bleDevice.name}`);
  } catch (err) {
    ledChar   = null;
    bleDevice = null;
    setConnectionState('disconnected');
    // NotFoundError / NotAllowedError = user cancelled picker — no toast needed
    if (err.name !== 'NotFoundError' && err.name !== 'NotAllowedError') {
      showToast(err.message || 'Verbindungsfehler', true);
    }
  }
}

function onDisconnected() {
  ledChar = null;
  setConnectionState('disconnected');
  clearActivePreset();
  showToast('Verbindung getrennt');
}

// ── UI state helpers ──────────────────────────────────────────────────────────
function setConnectionState(state, deviceName = '') {
  const connected  = state === 'connected';
  const connecting = state === 'connecting';

  document.getElementById('dot').className        = 'dot ' + state;
  const text = document.getElementById('statusText');
  text.className   = connected ? 'connected' : '';
  text.textContent = connecting ? 'Verbinde …'
                   : connected  ? deviceName
                   : 'Nicht verbunden';

  const btn = document.getElementById('connectBtn');
  btn.textContent = connected ? 'Trennen' : 'Verbinden';
  btn.className   = 'btn ' + (connected ? 'btn-danger' : 'btn-primary');
  btn.disabled    = connecting;

  document.getElementById('sendBtn').disabled = !connected;
  document.querySelectorAll('.preset-btn').forEach(b => { b.disabled = !connected; });
}

function clearActivePreset() {
  document.querySelectorAll('.preset-btn').forEach(b => b.classList.remove('active'));
}

function selectMode(id) {
  selectedMode = id;
  document.querySelectorAll('.mode-btn')
    .forEach((b, i) => b.classList.toggle('selected', MODES[i].id === id));
}

function setLabel(id, text) {
  document.getElementById(id).textContent = text;
}

// ── Send commands ─────────────────────────────────────────────────────────────
async function sendCommand(r, g, b, brightness, mode, speed) {
  if (!ledChar) return false;
  try {
    await ledChar.writeValueWithoutResponse(buildPacket(r, g, b, brightness, mode, speed));
    return true;
  } catch {
    showToast('Senden fehlgeschlagen', true);
    return false;
  }
}

async function sendPreset(preset) {
  const ok = await sendCommand(
    preset.r, preset.g, preset.b,
    preset.brightness, preset.mode, preset.speed,
  );
  if (!ok) return;
  clearActivePreset();
  document.getElementById(`preset-${preset.id}`).classList.add('active');
  showToast(preset.label);
}

async function sendManual() {
  const { r, g, b } = hexToRgb(document.getElementById('colorPicker').value);
  const brightness   = parseInt(document.getElementById('brightness').value, 10);
  const speed        = parseInt(document.getElementById('speed').value, 10);
  const ok           = await sendCommand(r, g, b, brightness, selectedMode, speed);
  if (ok) {
    clearActivePreset();
    showToast('Gesendet');
  }
}

// ── Toast ─────────────────────────────────────────────────────────────────────
function showToast(msg, isError = false) {
  const el = document.getElementById('toast');
  el.textContent = msg;
  el.className   = 'show' + (isError ? ' toast-error' : '');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { el.className = ''; }, 2800);
}

init();
