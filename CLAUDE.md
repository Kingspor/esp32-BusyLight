# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

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

## Build & Run

### Windows App (.NET 8)
```bash
cd app/BusyLight
dotnet build                              # Debug
dotnet run                                # Run locally (debug)
dotnet publish -p:PublishProfile=Release-win-x64              # Single-file EXE
dotnet publish -p:PublishProfile=Release-win-x64 -p:Version=0.5.0  # With version
```
Requires Windows 10 build 22621 (22H2) or later. No test projects exist — the app is hardware-dependent and tested manually.

### Firmware (ESP32-C3)
**Arduino IDE:** Board = ESP32C3 Dev Module, enable USB CDC On Boot, library = Adafruit NeoPixel.

**Arduino CLI** (used in CI/CD):
```bash
arduino-cli compile --fqbn esp32:esp32:esp32c3 --output-dir firmware/build firmware/BusyLight
```

### CI/CD
Push a tag matching `v*.*.*` → GitHub Actions builds both app (`BusyLight.exe`) and firmware (`.bin`), creates a GitHub Release.

## Architecture

### Windows App

- **Entry point** (`Program.cs`): Global named mutex enforces single-instance; second launch is silently rejected.
- **`TrayApplication.cs`**: Root `ApplicationContext` — owns tray icon, context menu, all services, presence/BLE history tracking. No main window.
- **Service layer** (`Services/`): Three independent services raise events consumed by `TrayApplication`:
  - `GraphService`: MSAL OAuth2 → Microsoft Graph `Presence.Read` polling (default 30s). Raises `PresenceChanged`.
  - `BleService`: BLE scan/connect/send. Raises `ConnectionChanged` and `ErrorOccurred`. Only restarts when the device address changes (ADR-005).
  - `ConfigurationService`: Reads/writes `%APPDATA%\BusyLight\appsettings.json`. Settings saves are non-disruptive to BLE.
- **Forms layer** (`Forms/`): `StatusForm`, `SettingsForm`, `HistoryForm`, `ColorWheelForm`, `BlePickerForm` — all opened on demand from the tray menu.
- **Configuration-driven LED mapping**: `AppSettings.cs` holds a per-presence-status mapping (color, mode, speed, brightness). Adding a new Teams status only requires a new entry in `appsettings.json`.
- **Threading**: UI thread (WinForms message loop) + ThreadPool (Graph polling, BLE ops). All UI updates marshalled via captured `SynchronizationContext`.

### Firmware (C++/Arduino)

- **Non-blocking animation** (`LedController.cpp`): `update()` advances state each `loop()` iteration using `millis()` — no blocking delays.
- **BLE GATT server** (`GattServer.cpp`): Advertises service, parses 6-byte LED command packets, updates telemetry characteristic.
- **`config.h`**: Single source of truth for BLE UUIDs, pin definitions, protocol version.

### Protocol

6-byte BLE packet `[R, G, B, Brightness, Mode, Speed]` written to characteristic `feda0101-…`.
Protocol version (currently `1`) on read-only characteristic `feda0103-…` — bumped only on breaking changes.

## Key Design Decisions

- **ADR-001/002**: Currently WinForms — **WPF migration planned** (see `docs/wpf-migration.md`). Models & services are UI-agnostic; only the UI layer changes.
- **ADR-004**: Brightness capped on app side (default 0.6) to protect USB power budget.
- Single BLE device per workstation — no multi-device support by design.
- `ClientId` and `TenantId` in `appsettings.json` are app registration values, not secrets.

## Animation Modes

| Mode | Name | Description |
|------|------|-------------|
| 0 | Static | Solid color |
| 1 | Pulse | Brightness fades 0→max→0 |
| 2 | Chase | Single LED orbits LEDs 1–6 |
| 3 | Rainbow | Spectrum rotation (ignores RGB) |
| 4 | Blink | All LEDs toggle on/off |
| 5 | Fill | LEDs 1–6 fill/empty; LED 0 always on |

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
| `app/BusyLight/Models/AppSettings.cs` | Strongly-typed config & presence→LED mapping |
| `docs/arc42.md` | Full architecture documentation |
