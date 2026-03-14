# BusyLight — Claude Project Context

## Project Overview

BusyLight is an ESP32-based status light that shows Microsoft Teams presence via a WS2812B RGB LED ring. A .NET 8 Windows tray app polls the Graph API and sends LED commands over BLE.

## Repository Structure

```
firmware/BusyLight/       # ESP32-C3 Arduino sketch
app/BusyLight/            # .NET 8 WinForms Windows app
docs/                     # arc42 architecture docs, user guide, WPF migration notes
.github/workflows/        # CI/CD (release.yml) triggered by version tags
```

## Architecture in Brief

- **Firmware** (C++/Arduino): BLE GATT server, non-blocking animation state machine
- **Windows App** (.NET 8): Tray app, MSAL OAuth2 → Graph API → BLE commands
- **Protocol**: 6-byte BLE packet `[R, G, B, Brightness, Mode, Speed]` to characteristic `feda0101-…`
- **Protocol versioning**: Read-only characteristic `feda0103-…` (current version: 1)

## Key Design Decisions

- **ADR-001/002**: Currently WinForms — **WPF migration planned** (see `docs/wpf-migration.md`). Models & services are UI-agnostic; only the UI layer changes.
- **ADR-004**: Brightness capped on app side (default 0.6) to protect USB power budget.
- **ADR-005**: BleService only restarts when device address changes; settings saves are non-disruptive.
- Single BLE device per workstation (no multi-device support by design).

## Build Instructions

### Firmware (ESP32-C3)
1. Arduino IDE 2.x, board: ESP32C3 Dev Module, enable USB CDC On Boot
2. Library: Adafruit NeoPixel
3. Upload `firmware/BusyLight/BusyLight.ino`

### Windows App (.NET 8)
```bash
cd app/BusyLight
dotnet build                              # Debug
dotnet publish -p:PublishProfile=Release-win-x64  # Single-file EXE
```
Requires Windows 10 build 22621 (22H2) or later.

### CI/CD
Push a tag matching `v*.*.*` → GitHub Actions builds both app and firmware, creates a GitHub Release.

## Important Files

| File | Purpose |
|------|---------|
| `firmware/BusyLight/LedController.cpp` | Animation engine (modes 0–5) |
| `firmware/BusyLight/GattServer.cpp` | BLE GATT server & protocol |
| `firmware/BusyLight/config.h` | Pin defs, UUIDs, protocol version |
| `app/BusyLight/TrayApplication.cs` | Root orchestration, tray icon |
| `app/BusyLight/Services/BleService.cs` | BLE scan, connect, send |
| `app/BusyLight/Services/GraphService.cs` | MSAL auth, Graph API polling |
| `app/BusyLight/Models/LedCommand.cs` | 6-byte packet model |
| `docs/arc42.md` | Full architecture documentation |

## Animation Modes

| Mode | Name | Description |
|------|------|-------------|
| 0 | Static | Solid color |
| 1 | Pulse | Brightness fades 0→max→0 |
| 2 | Chase | Single LED orbits LEDs 1–6 |
| 3 | Rainbow | Spectrum rotation (ignores RGB) |
| 4 | Blink | All LEDs toggle on/off |
| 5 | Fill | LEDs 1–6 fill/empty; LED 0 always on |

## Threading Model

- UI thread: WinForms message loop
- ThreadPool: Graph polling, BLE scanning, GATT operations
- UI updates marshalled via captured `SynchronizationContext`

## Configuration

App settings stored in `%APPDATA%\BusyLight\appsettings.json`.
OAuth tokens DPAPI-encrypted in `%APPDATA%\BusyLight\msal_token_cache.bin`.
`ClientId` and `TenantId` are app registration values (not secrets).
