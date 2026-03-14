# ARC42 Architecture Documentation — BusyLight

> Version 0.1 · March 2026

---

## 1. Introduction and Goals

**BusyLight** is a Windows tray application that reads the Microsoft Teams presence status of the signed-in user via the Microsoft Graph API and displays it on a WS2812B LED ring (ESP32-based hardware) over Bluetooth Low Energy.

### Quality Goals

| Priority | Quality Goal | Scenario |
|---|---|---|
| 1 | **Reliability** | LEDs reconnect automatically after BLE dropout |
| 2 | **Privacy** | Azure AD tokens stored in DPAPI-encrypted MSAL cache |
| 3 | **Usability** | Tray-only: no persistent window, starts with Windows |
| 4 | **Maintainability** | Clean separation of services; settings persisted as plain JSON |

### Stakeholders

| Role | Interest |
|---|---|
| End user | LED reflects Teams status with no manual intervention |
| Developer | Easy to extend presence map, add new LED modes |

---

## 2. Architecture Constraints

- **Windows only** — uses WinRT BLE APIs (`Windows.Devices.Bluetooth`) requiring Windows 10 22H2+
- **Single device per laptop** — one BusyLight per machine (no multi-device support)
- **No cloud backend** — all data flows locally; Graph calls go directly from client to Microsoft
- **.NET 8 WinForms** — current UI framework (WPF migration planned, see section 9)

---

## 3. System Scope and Context

```
┌─────────────────────────────────────────────────────┐
│                   User's Laptop                      │
│                                                      │
│  ┌──────────────┐     MSAL/OAuth2     ┌───────────┐ │
│  │ BusyLight.exe│ ──────────────────► │ Microsoft │ │
│  │  (Tray App)  │ ◄──── Presence ──── │   Graph   │ │
│  └──────┬───────┘                     └───────────┘ │
│         │ BLE (GATT Write)                           │
└─────────┼───────────────────────────────────────────┘
          │
    ┌─────▼──────┐
    │  ESP32     │
    │  BusyLight │  ── WS2812B LED ring (7 LEDs)
    │  Firmware  │
    └────────────┘
```

### External Interfaces

| Interface | Direction | Protocol |
|---|---|---|
| Microsoft Graph `/me/presence` | App → Cloud | HTTPS / OAuth2 |
| BLE GATT LED characteristic | App → Device | BLE Write-Without-Response |
| `appsettings.json` | App ↔ Filesystem | Local JSON file |
| MSAL token cache | App ↔ Filesystem | DPAPI-encrypted binary |

---

## 4. Solution Strategy

| Challenge | Decision |
|---|---|
| BLE reconnect reliability | Advertisement watcher — connect only after fresh advertisement (guarantees Windows BLE cache is warm) |
| Token lifecycle | MSAL with persistent DPAPI cache; silent acquisition first, interactive fallback |
| Settings persistence | Plain `appsettings.json` in `%APPDATA%\BusyLight`; human-readable, no database |
| LED color math | Brightness capped on app side (`BrightnessCap × row.Brightness`); firmware receives final RGB directly |
| Live preview | `LivePreviewCommand` event from SettingsForm → BleService without saving |

---

## 5. Building Block View

### Level 1 — Top-Level Components

```
BusyLight.exe
├── TrayApplication          Root ApplicationContext; owns tray icon, context menu, all services
├── SettingsForm             Combined status monitor + settings editor (single window)
├── BlePickerForm            First-run BLE device discovery dialog
├── ColorWheelForm           HSV colour picker with live preview event
├── BleService               BLE connect/reconnect/send (one instance)
├── GraphService             MSAL auth + Graph presence polling
└── ConfigurationService     Load/save appsettings.json
```

### Level 2 — BleService internals

```
BleService
├── BluetoothLEAdvertisementWatcher   scans for advertisements
├── ConnectAsync()                    GATT service + characteristic discovery
├── RetryLoopAsync()                  PeriodicTimer restarts watcher after disconnect
└── SendCommandAsync()                WriteWithoutResponse on LedCharacteristic
```

### Level 2 — GraphService internals

```
GraphService
├── AuthenticateAsync()     MSAL PublicClientApplication, DPAPI cache
├── StartPolling()          PeriodicTimer → PollPresenceAsync()
├── FetchNowAsync()         On-demand poll (clears override from tray)
└── MsalAccessTokenProvider Adapts MSAL to Graph SDK v5 IAccessTokenProvider
```

---

## 6. Runtime View

### Scenario 1 — Normal startup

```
TrayApplication.ctor
  └─► InitialiseAsync()
        ├─ LoadAsync() → appsettings.json
        ├─ Restore persisted override (if KeepOverrideOnRestart = true)
        ├─ [BleDevice == null] → ShowBlePickerAsync() → save device
        ├─ StartBleService()
        │    └─ BleService.StartScanning()
        │         ├─ ConnectionChanged(Searching)
        │         └─ watcher.Start()
        └─ GraphService.AuthenticateAsync() → StartPolling()
              └─ PollPresenceAsync() → PresenceChanged("Available")
                    ├─ Update tray icon
                    ├─ SettingsForm.UpdatePresence("Available")
                    └─ SendLedCommand("Available") → BleService.SendCommandAsync()
```

### Scenario 2 — BLE disconnect and reconnect

```
Device powers off
  └─ OnConnectionStatusChanged(Disconnected)
       ├─ HandleDisconnect() → ConnectionChanged(Disconnected)
       └─ _addressKnown = false

RetryLoopAsync tick (after BleRetryIntervalSeconds)
  └─ _watcher == null → StartWatcher()
       └─ ConnectionChanged(Searching)

Device powers on → advertisement received
  └─ OnAdvertisementReceived()
       ├─ address matches → _addressKnown = true
       ├─ StopWatcher()
       └─ ConnectAsync()
            └─ ConnectionChanged(Connected)
                 └─ SendLedCommandTo() → re-sync current presence
```

### Scenario 3 — Settings open → live preview → close

```
User opens SettingsForm
  ├─ SendCommandAsync(LedCommand.Off)   ← LEDs off immediately
  ├─ _settingsForm.Show()
  │    └─ Load event → SettingsForm_Load → LoadToUi   (sets lblBleDevice to "Suche…")
  └─ UpdatePresence(currentPresence)        ⎫ called AFTER Show() so they
     UpdateBleStatus(name, CurrentState)    ⎭ overwrite LoadToUi's placeholder text

User drags color wheel
  └─ ColorWheelForm.ColorChanged
       └─ SettingsForm.FireLivePreview()   [guarded: _loading check prevents fire during LoadToUi]
            └─ LivePreviewCommand → BleService.SendCommandAsync()

User saves settings
  └─ ApplyNewSettings()
       ├─ [device address unchanged] → BleService kept alive; SendLedCommand() re-syncs LEDs
       └─ [device address changed]   → old BleService disposed; new one started

User closes SettingsForm
  └─ VisibleChanged → SendLedCommand(currentPresence)   ← restore
```

---

## 7. Deployment View

```
%APPDATA%\BusyLight\
├── appsettings.json          User configuration (presence map, BLE device, etc.)
└── msal_token_cache.bin      DPAPI-encrypted OAuth2 token cache

Registry (optional):
  HKCU\Software\Microsoft\Windows\CurrentVersion\Run
  └── BusyLight = "C:\...\BusyLight.exe"   (autostart)
```

The published app is a **single self-contained `.exe`** (~80 MB, no .NET install required).
CI publishes via `dotnet publish -p:PublishProfile=Release-win-x64` on git tag push.

---

## 8. Cross-Cutting Concepts

### Error Handling

- `GraphService` catches `ODataError` and generic exceptions, raises `ErrorOccurred` event
- `BleService` catches all BLE exceptions, raises `ErrorOccurred`, triggers `HandleDisconnect`
- `TrayApplication` shows balloon tips for errors; never crashes the process

### Threading

- `TrayApplication` runs on the WinForms UI thread (message loop)
- `GraphService` polling runs on a `ThreadPool` thread
- `BleService` GATT callbacks are on WinRT thread pool threads
- All UI updates are marshalled via `SynchronizationContext` (captured in `TrayApplication.ctor`)
- `BleService.SendCommandAsync` is called from `Task.Run` to avoid blocking the UI

### Security

- Client ID and Tenant ID stored in plaintext `appsettings.json` (app registration, not secret)
- OAuth2 tokens in DPAPI-encrypted MSAL cache — not readable by other Windows users
- BLE writes use `WriteWithoutResponse` — no sensitive data transmitted

### LED Command Protocol

6-byte packet over GATT `feda0101-…`:

| Byte | Field | Values |
|---|---|---|
| 0 | R | 0–255 |
| 1 | G | 0–255 |
| 2 | B | 0–255 |
| 3 | Brightness | 0–255 (pre-capped by app) |
| 4 | Mode | 0=Static, 1=Pulse, 2=Chase, 3=Rainbow, 4=Blink, 5=Fill |
| 5 | Speed | 0–255 (higher = faster) |

---

## 9. Architecture Decisions

### ADR-001 — WinForms over WPF (current)

**Status:** Under review
**Context:** App started as a quick prototype; WinForms was faster to scaffold.
**Consequence:** Dynamic presence rows are built in code, not XAML. A WPF migration is planned to improve data binding, theming, and overall UX (see ADR-002).

### ADR-002 — WPF Migration (planned)

**Status:** Planned
**Context:** WPF offers MVVM data binding, better DPI scaling, modern theming, and richer controls.
**Approach:** Port models and services unchanged; rebuild UI layer with MVVM pattern.
See `docs/wpf-migration.md` for detailed analysis.

### ADR-003 — Single BLE device per laptop

**Status:** Accepted
**Context:** A single BusyLight per workstation is the practical use case; supporting multiple adds complexity without user value.
**Consequence:** `AppSettings.BleDevice` is a single nullable `BleDeviceSettings`, not a list.

### ADR-004 — App-side brightness cap

**Status:** Accepted
**Context:** Firmware previously had a hardcoded `BRIGHTNESS_CAP_FACTOR`. Moving the cap to the app gives users live control without firmware updates.
**Consequence:** `LedCommand.FromPresenceSettings` applies `BrightnessCap` before sending.

### ADR-005 — BLE service restart only on device change

**Status:** Accepted
**Context:** Early implementation always restarted `BleService` after saving settings. This disposed the GATT handles, leaving the ESP32 in a ghost-connected state until the BLE supervision timeout (5–10 s). During that window all commands were silently dropped.
**Decision:** `ApplyNewSettings` compares old and new `BleDevice.Address`. The service is only torn down and restarted when the address differs. When only colors/brightness/mode change, the existing connection is kept and a re-send command is issued immediately.
**Consequence:** Settings save is non-disruptive; LED color changes take effect within one write cycle.

---

## 10. Quality Requirements

| ID | Requirement | Measure |
|---|---|---|
| Q-1 | Reconnects within `BleRetryIntervalSeconds` after BLE dropout | PeriodicTimer retry loop |
| Q-2 | LEDs always reflect current Teams presence after reconnect | `_lastSentCommand = null` forces re-send |
| Q-3 | No token prompts after initial login | DPAPI-cached MSAL silent acquisition |
| Q-4 | Settings changes never apply until "Speichern" is clicked | `_isDirty` flag; `ReadFromUi()` only called on explicit save |
| Q-5 | No LED flash when opening settings | `_loading` guard in `FireLivePreview` |
| Q-6 | Saving settings does not interrupt BLE communication | `ApplyNewSettings` restarts BLE only when device address changes (ADR-005) |
| Q-7 | BLE status always correct when settings window opens | `UpdateBleStatus(CurrentState)` called after `Show()`, overwriting `LoadToUi` placeholder |

---

## 11. Risks and Technical Debt

| Risk | Impact | Mitigation |
|---|---|---|
| WinForms dynamic row layout breaks on high-DPI | Medium | Migrate to WPF (ADR-002) |
| Windows BLE stack caching causes stale device handles | Medium | Watcher + fresh advertisement before every `FromBluetoothAddressAsync` call |
| Saving settings drops BLE connection (ghost-connected state) | ~~High~~ Resolved | `ApplyNewSettings` keeps existing `BleService` when device address is unchanged (ADR-005) |
| Graph API rate limiting | Low | Configurable poll interval (default 30 s) |
| MSAL token cache corruption | Low | Cache is in `%APPDATA%`; user can delete it to force re-auth |
