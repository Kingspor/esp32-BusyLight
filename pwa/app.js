import {
  SERVICE_UUID, LED_CHAR_UUID, TELEMETRY_CHAR_UUID,
  PRESETS, MODES,
  hexToRgb, rgbToHex, buildPacket, percentLabel, parseTelemetry,
  DEFAULT_BRIGHTNESS,
  loadPresets, savePreset, resetPreset,
} from './busylight-core.js';

// ── State ────────────────────────────────────────────────────────────────────
let bleDevice      = null;
let ledChar        = null;
let telemetryChar  = null;
let selectedMode   = 0;
let toastTimer     = null;
let activePresets  = loadPresets();
let editingId      = null;
let editModeId     = 0;

// ── Boot ─────────────────────────────────────────────────────────────────────
function init() {
  if (!navigator.bluetooth) {
    document.getElementById('bleNotice').style.display = 'block';
    document.getElementById('connectBtn').disabled = true;
  }

  buildPresetGrid();
  buildModeGrid();
  buildEditModeGrid();
  setConnectionState('disconnected');
  wireSliders();
  document.getElementById('connectBtn').addEventListener('click', toggleConnection);
  document.getElementById('sendBtn').addEventListener('click', sendManual);

  document.getElementById('editClose').addEventListener('click', closeEditModal);
  document.getElementById('editBackdrop').addEventListener('click', e => {
    if (e.target === e.currentTarget) closeEditModal();
  });
  document.getElementById('editSave').addEventListener('click', saveEdit);
  document.getElementById('editReset').addEventListener('click', resetEdit);
  document.getElementById('editBrightness').addEventListener('input', () =>
    setLabel('editBrightnessVal', percentLabel(document.getElementById('editBrightness').value, 255))
  );
  document.getElementById('editSpeed').addEventListener('input', () =>
    setLabel('editSpeedVal', percentLabel(document.getElementById('editSpeed').value, 255))
  );

  if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('sw.js').catch(() => {});
  }
}

function buildPresetGrid() {
  const grid = document.getElementById('presetGrid');
  activePresets.forEach(p => {
    const wrap = document.createElement('div');
    wrap.className = 'preset-wrap';

    const btn = document.createElement('button');
    btn.className        = 'preset-btn';
    btn.id               = `preset-${p.id}`;
    btn.disabled         = true;
    btn.style.background = p.bg;
    btn.innerHTML        = `<div class="preset-dot"></div><span class="preset-label">${p.label}</span>`;
    btn.addEventListener('click', () => sendPreset(activePresets.find(x => x.id === p.id)));

    const editBtn = document.createElement('button');
    editBtn.className   = 'preset-edit-btn';
    editBtn.title       = 'Bearbeiten';
    editBtn.textContent = '✎';
    editBtn.addEventListener('click', () => openEditModal(p.id));

    wrap.appendChild(btn);
    wrap.appendChild(editBtn);
    grid.appendChild(wrap);
  });
}

function buildEditModeGrid() {
  const grid = document.getElementById('editModeGrid');
  MODES.forEach(m => {
    const btn = document.createElement('button');
    btn.className   = 'mode-btn';
    btn.textContent = m.label;
    btn.dataset.modeId = m.id;
    btn.addEventListener('click', () => selectEditMode(m.id));
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

    try {
      telemetryChar = await service.getCharacteristic(TELEMETRY_CHAR_UUID);
      const initial = await telemetryChar.readValue();
      updateBattery(parseTelemetry(initial));
      await telemetryChar.startNotifications();
      telemetryChar.addEventListener('characteristicvaluechanged', e =>
        updateBattery(parseTelemetry(e.target.value))
      );
    } catch {
      telemetryChar = null;
    }

    setConnectionState('connected', bleDevice.name);
    showToast(`Verbunden mit ${bleDevice.name}`);
  } catch (err) {
    ledChar       = null;
    telemetryChar = null;
    bleDevice     = null;
    setConnectionState('disconnected');
    // NotFoundError / NotAllowedError = user cancelled picker — no toast needed
    if (err.name !== 'NotFoundError' && err.name !== 'NotAllowedError') {
      showToast(err.message || 'Verbindungsfehler', true);
    }
  }
}

function onDisconnected() {
  ledChar       = null;
  telemetryChar = null;
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

  const batteryText = document.getElementById('batteryText');
  if (!connected) batteryText.hidden = true;

  const btn = document.getElementById('connectBtn');
  btn.textContent = connected ? 'Trennen' : 'Verbinden';
  btn.className   = 'btn ' + (connected ? 'btn-danger' : 'btn-primary');
  btn.disabled    = connecting;

  document.getElementById('sendBtn').disabled = !connected;
  document.querySelectorAll('.preset-btn').forEach(b => { b.disabled = !connected; });
}

function updateBattery({ mv, soc }) {
  const el = document.getElementById('batteryText');
  const icon = soc <= 20 ? '🪫' : '🔋';
  el.textContent = `${icon} ${soc} % · ${(mv / 1000).toFixed(2)} V`;
  el.className   = 'battery-text' + (soc <= 20 ? ' low' : '');
  el.hidden      = false;
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

// ── Preset editing ────────────────────────────────────────────────────────────
function openEditModal(id) {
  editingId = id;
  const preset = activePresets.find(p => p.id === id);

  document.getElementById('editLabel').value       = preset.label;
  document.getElementById('editColor').value       = rgbToHex(preset.r, preset.g, preset.b);
  document.getElementById('editBrightness').value  = preset.brightness;
  document.getElementById('editSpeed').value       = preset.speed;
  setLabel('editBrightnessVal', percentLabel(preset.brightness, 255));
  setLabel('editSpeedVal',      percentLabel(preset.speed,      255));
  selectEditMode(preset.mode);

  document.getElementById('editBackdrop').hidden = false;
}

function closeEditModal() {
  document.getElementById('editBackdrop').hidden = true;
  editingId = null;
}

function selectEditMode(id) {
  editModeId = id;
  document.querySelectorAll('#editModeGrid .mode-btn').forEach(b => {
    b.classList.toggle('selected', Number(b.dataset.modeId) === id);
  });
}

function updatePresetUI(preset) {
  const btn  = document.getElementById(`preset-${preset.id}`);
  btn.style.background = preset.bg;
  btn.querySelector('.preset-label').textContent = preset.label;
}

function saveEdit() {
  if (!editingId) return;
  const hex    = document.getElementById('editColor').value;
  const { r, g, b } = hexToRgb(hex);
  const preset = {
    ...activePresets.find(p => p.id === editingId),
    label:      document.getElementById('editLabel').value.trim() || editingId,
    r, g, b,
    bg:         hex,
    brightness: parseInt(document.getElementById('editBrightness').value, 10),
    mode:       editModeId,
    speed:      parseInt(document.getElementById('editSpeed').value, 10),
  };
  const idx = activePresets.findIndex(p => p.id === editingId);
  activePresets[idx] = preset;
  savePreset(preset);
  updatePresetUI(preset);
  closeEditModal();
  showToast('Preset gespeichert');
}

function resetEdit() {
  if (!editingId) return;
  const preset = resetPreset(editingId);
  const idx    = activePresets.findIndex(p => p.id === editingId);
  activePresets[idx] = preset;
  updatePresetUI(preset);
  closeEditModal();
  showToast('Preset zurückgesetzt');
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
