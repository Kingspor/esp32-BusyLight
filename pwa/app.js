import {
  SERVICE_UUID, LED_CHAR_UUID, TELEMETRY_CHAR_UUID, STATE_CHAR_UUID,
  PRESETS, MODES,
  hexToRgb, rgbToHex, buildPacket, percentLabel, parseTelemetry,
  parseState, matchPreset,
  DEFAULT_BRIGHTNESS,
  loadPresets, savePreset, resetPreset,
  saveLastDevice, loadLastDevice, clearLastDevice, reconnectDelayMs,
} from './busylight-core.js';

// ── State ────────────────────────────────────────────────────────────────────
let bleDevice      = null;
let ledChar        = null;
let telemetryChar  = null;
let stateChar      = null;
let selectedMode   = 0;
let toastTimer     = null;
let activePresets  = loadPresets();
let editingId      = null;
let editModeId     = 0;

// ── Reconnect state ──────────────────────────────────────────────────────────
// The link drops for all sorts of everyday reasons — a pocketed phone, a locked
// screen, someone walking past.  Retries run until the device is back or the
// user stops them, so a drop no longer means walking over to the light.
let reconnectTimer   = null;
let reconnectAttempt = 0;
let reconnecting     = false;
let userDisconnected = false;  // true only after the user pressed "Trennen"/"Stopp"

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

  // iOS suspends background JS, so a pending retry can be badly overdue by the
  // time the app is reopened — retry on foreground instead of waiting for it.
  document.addEventListener('visibilitychange', onVisibilityChange);

  if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('sw.js').catch(() => {});
  }

  // Try to pick the previous session's device back up without the picker.
  void restoreLastDevice();
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
    // Deliberate disconnect: suppress the retry that onDisconnected would start.
    userDisconnected = true;
    cancelReconnect();
    bleDevice.gatt.disconnect();
    return;
  }

  if (reconnecting) {
    // Automatic retries are running — the button stops them.
    userDisconnected = true;
    cancelReconnect();
    setConnectionState('disconnected');
    showToast('Automatische Verbindung gestoppt');
    return;
  }

  await connect();
}

/** Show the browser's device picker and connect to the chosen device. */
async function connect() {
  try {
    setConnectionState('connecting');

    const device = await navigator.bluetooth.requestDevice({
      filters:          [{ namePrefix: 'BusyLight' }],
      optionalServices: [SERVICE_UUID],
    });

    await openDevice(device);
    showToast(`Verbunden mit ${deviceLabel(device)}`);
  } catch (err) {
    handleConnectFailure(err);
  }
}

/**
 * Connect to an already-known BluetoothDevice and wire up its characteristics.
 * Shared by the picker flow, the start-up restore and every reconnect attempt.
 */
async function openDevice(device) {
  bleDevice = device;
  // Re-adding an identical listener reference is a no-op per the DOM spec, so
  // this stays safe across repeated reconnects to the same device object.
  device.addEventListener('gattserverdisconnected', onDisconnected);

  const server  = await device.gatt.connect();
  const service = await server.getPrimaryService(SERVICE_UUID);
  ledChar       = await service.getCharacteristic(LED_CHAR_UUID);

  await subscribeTelemetry(service);
  await subscribeState(service);

  userDisconnected = false;
  reconnectAttempt = 0;
  cancelReconnect();
  saveLastDevice(device);
  setConnectionState('connected', deviceLabel(device));
}

/** Telemetry is optional — older firmware has no battery characteristic. */
async function subscribeTelemetry(service) {
  try {
    telemetryChar = await service.getCharacteristic(TELEMETRY_CHAR_UUID);
    updateBattery(parseTelemetry(await telemetryChar.readValue()));
    await telemetryChar.startNotifications();
    telemetryChar.addEventListener('characteristicvaluechanged', e =>
      updateBattery(parseTelemetry(e.target.value))
    );
  } catch {
    telemetryChar = null;
  }
}

/**
 * Ask the device what it is currently showing and keep following it.
 * Without this the UI can only reflect what this phone last sent, which goes
 * stale the moment the link drops, the device reboots, or the Windows app
 * changes the status.  Firmware without the characteristic simply leaves the
 * highlight cleared rather than showing something invented.
 */
async function subscribeState(service) {
  try {
    stateChar = await service.getCharacteristic(STATE_CHAR_UUID);
    applyDeviceState(parseState(await stateChar.readValue()));
    await stateChar.startNotifications();
    stateChar.addEventListener('characteristicvaluechanged', e =>
      applyDeviceState(parseState(e.target.value))
    );
  } catch {
    stateChar = null;
    clearActivePreset();
  }
}

/** Highlight whichever preset the device's current command corresponds to. */
function applyDeviceState(state) {
  highlightPreset(matchPreset(state, activePresets));
}

function handleConnectFailure(err) {
  ledChar       = null;
  telemetryChar = null;
  stateChar     = null;
  bleDevice     = null;
  setConnectionState('disconnected');
  // NotFoundError / NotAllowedError = user cancelled picker — no toast needed
  if (err.name !== 'NotFoundError' && err.name !== 'NotAllowedError') {
    showToast(err.message || 'Verbindungsfehler', true);
  }
}

function onDisconnected() {
  ledChar       = null;
  telemetryChar = null;
  stateChar     = null;
  clearActivePreset();

  if (userDisconnected) {
    setConnectionState('disconnected');
    showToast('Verbindung getrennt');
    return;
  }

  // Unexpected drop.  The ring keeps showing the last status (the firmware holds
  // it for LED_HOLD_AFTER_DISCONNECT_MS), so we only need the link back.
  showToast('Verbindung verloren – verbinde neu …', true);
  scheduleReconnect();
}

/** Queue the next reconnect attempt with a growing delay, capped at 30 s. */
function scheduleReconnect() {
  if (!bleDevice || userDisconnected) return;

  clearTimeout(reconnectTimer);
  const delay = reconnectDelayMs(reconnectAttempt);
  reconnectAttempt++;
  reconnecting = true;
  setConnectionState('reconnecting', `Verbinde neu … (${reconnectAttempt})`);

  reconnectTimer = setTimeout(async () => {
    reconnectTimer = null;
    if (!bleDevice || userDisconnected) return;
    try {
      await openDevice(bleDevice);
      showToast(`Wieder verbunden mit ${deviceLabel(bleDevice)}`);
    } catch {
      scheduleReconnect();  // never gives up; the delay just stops growing
    }
  }, delay);
}

function cancelReconnect() {
  clearTimeout(reconnectTimer);
  reconnectTimer = null;
  reconnecting   = false;
}

function onVisibilityChange() {
  if (document.visibilityState !== 'visible') return;
  if (userDisconnected || !bleDevice || bleDevice.gatt?.connected) return;

  // Restart the backoff so returning to the app retries almost immediately.
  reconnectAttempt = 0;
  cancelReconnect();
  scheduleReconnect();
}

/**
 * Reconnect to the previous session's device without showing the picker.
 * Needs navigator.bluetooth.getDevices(), which lists devices the user has
 * already granted this origin — available in Chrome behind
 * chrome://flags/#enable-web-bluetooth-new-permissions-backend and not in every
 * Web Bluetooth implementation.  Where it is missing the user taps "Verbinden"
 * once per session; drops during a session still recover on their own.
 */
async function restoreLastDevice() {
  const last = loadLastDevice();
  if (!last || typeof navigator.bluetooth?.getDevices !== 'function') return;

  let permitted;
  try {
    permitted = await navigator.bluetooth.getDevices();
  } catch {
    return;  // API present but unusable — fall back to the manual picker
  }

  const device = permitted.find(d => d.id === last.id);
  if (!device) {
    // Permission for that device is gone (revoked, or a different profile).
    clearLastDevice();
    return;
  }

  bleDevice = device;
  setConnectionState('connecting');
  try {
    await openDevice(device);
    showToast(`Automatisch verbunden mit ${deviceLabel(device)}`);
  } catch {
    // Device is known but not in range yet — keep trying in the background.
    scheduleReconnect();
  }
}

/** getDevices() may hand back a device without a name — keep labels sane. */
function deviceLabel(device) {
  return device?.name || 'BusyLight';
}

// ── UI state helpers ──────────────────────────────────────────────────────────
function setConnectionState(state, detail = '') {
  const connected    = state === 'connected';
  const connecting   = state === 'connecting';
  const retrying     = state === 'reconnecting';

  document.getElementById('dot').className        = 'dot ' + state;
  const text = document.getElementById('statusText');
  text.className   = connected ? 'connected' : '';
  text.textContent = connecting ? 'Verbinde …'
                   : retrying   ? (detail || 'Verbinde neu …')
                   : connected  ? detail
                   : 'Nicht verbunden';

  const batteryText = document.getElementById('batteryText');
  if (!connected) batteryText.hidden = true;

  const btn = document.getElementById('connectBtn');
  // While retrying, the button's job is to call off the automatic attempts.
  btn.textContent = connected ? 'Trennen' : retrying ? 'Stopp' : 'Verbinden';
  btn.className   = 'btn ' + (connected || retrying ? 'btn-danger' : 'btn-primary');
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

/** Mark exactly one preset as active, or none when id is null. */
function highlightPreset(id) {
  clearActivePreset();
  if (!id) return;
  document.getElementById(`preset-${id}`)?.classList.add('active');
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
  // With the state characteristic present its notification highlights this
  // anyway; doing it here too keeps older firmware responsive.
  highlightPreset(preset.id);
  showToast(preset.label);
}

async function sendManual() {
  const { r, g, b } = hexToRgb(document.getElementById('colorPicker').value);
  const brightness   = parseInt(document.getElementById('brightness').value, 10);
  const speed        = parseInt(document.getElementById('speed').value, 10);
  const ok           = await sendCommand(r, g, b, brightness, selectedMode, speed);
  if (ok) {
    highlightPreset(null);  // a manual colour is no preset
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
