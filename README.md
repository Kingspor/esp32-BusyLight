# BusyLight

An ESP32-C3 BLE-connected status light that automatically reflects your Microsoft Teams presence — green when available, red when busy, orange when away, and more.

```
┌──────────────────────┐         BLE          ┌──────────────────────┐
│  Windows Tray App    │ ──────────────────▶  │  ESP32-C3 Firmware   │
│  (.NET 8 / WinForms) │    6-byte command    │  (Arduino / C++)     │
│                      │                      │                      │
│  Graph API polling   │                      │  WS2812B 7-LED ring  │
│  MSAL authentication │                      │  BLE GATT server     │
└──────────────────────┘                      └──────────────────────┘
          │
          │ HTTPS
          ▼
  Microsoft Graph API
  (Teams presence)
```

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Arduino](https://img.shields.io/badge/Arduino-2.x-green.svg)
![.NET](https://img.shields.io/badge/.NET-8-purple.svg)

---

## Hardware

| Part | Notes |
|------|-------|
| ESP32-C3 Super Mini | Any ESP32-C3 board works |
| WS2812B LED ring (7 LEDs) | Data pin → GPIO 5 |
| Enclosure / power supply | USB-C power from PC or a USB power bank |

**Wiring:**

```
ESP32-C3           WS2812B ring
─────────          ────────────
5 V (VIN)    ───▶  VDD
GND          ───▶  GND
GPIO 5       ───▶  DIN
```

> The internal status LED on GPIO 8 (active LOW) blinks at 1 Hz when no
> client is connected and stays solid when connected.

---

## Firmware Setup

### Prerequisites
- [Arduino IDE 2.x](https://www.arduino.cc/en/software)
- **ESP32 board support** — install via *Boards Manager*:
  `https://raw.githubusercontent.com/espressif/arduino-esp32/gh-pages/package_esp32_index.json`
  Then install **esp32 by Espressif Systems** (v3.x or later)
- **Adafruit NeoPixel** library — install via *Library Manager*

### Flashing
1. Open `firmware/BusyLight/BusyLight.ino` in Arduino IDE.
2. Select **Tools → Board → ESP32C3 Dev Module** (or the matching Super Mini entry).
3. Set **Tools → USB CDC On Boot → Enabled** for Serial output.
4. Select the correct COM port and click **Upload**.
5. Open the Serial Monitor at **115200 baud** to verify output.

### Flashing from a pre-built binary (release download)

Each [GitHub Release](https://github.com/Kingspor/esp32-BusyLight/releases) includes a pre-compiled `BusyLight-firmware-vX.Y.Z.bin`.
This only contains the application — the bootloader and partition table must already be present on the device (i.e. you flashed via Arduino IDE at least once before).

**Requirements:** Python + esptool

```
pip install esptool
```

**Flash command (update only):**

```
# Windows — <COM-Port> ersetzen, z. B. COM3
esptool --chip esp32c3 --port <COM-Port> --baud 460800 write_flash 0x10000 BusyLight-firmware-<version>.bin

# Linux / macOS — <port> ersetzen, z. B. /dev/ttyUSB0
esptool --chip esp32c3 --port <port> --baud 460800 write_flash 0x10000 BusyLight-firmware-<version>.bin
```

> **Fresh device?** Use the Arduino IDE route above — it flashes bootloader, partition table, and app in one go.
> After that, the `.bin` from releases is sufficient for all future updates.

### Verifying the firmware without the Windows app
Use the **nRF Connect** app (Android / iOS):
1. Scan — the device appears as `BusyLight`.
2. Connect and navigate to service `feda0100-…`.
3. Write 6 bytes to characteristic `feda0101-…`, for example:
   - `00 FF 00 B4 00 00` → solid green, 70 % brightness
   - `FF 00 00 B4 04 50` → red blinking, medium speed
4. The LED ring should respond immediately.

---

## Azure AD App Registration

The Windows application authenticates using OAuth2 Authorization Code Flow
with PKCE — no client secret is required.

1. Go to [portal.azure.com](https://portal.azure.com) and sign in.
2. Navigate to **Microsoft Entra ID → App registrations → New registration**.
3. Fill in:
   - **Name**: `BusyLight` (or any name)
   - **Supported account types**: *Accounts in this organizational directory only*
     (or *Multitenant* if needed)
   - **Redirect URI**: platform = **Public client/native**, URI = `http://localhost`
4. Click **Register**.
5. Note the **Application (client) ID** and **Directory (tenant) ID**.
6. Navigate to **API permissions → Add a permission → Microsoft Graph →
   Delegated permissions** and add:
   - `Presence.Read`
   - `User.Read`
7. Click **Grant admin consent** (or ask your tenant admin to do so).
8. Navigate to **Authentication** and ensure
   **Allow public client flows** is set to **Yes**.

---

## Windows App Setup

### Prerequisites
- Windows 10 version 22H2 (build 22621) or later (required for WinRT BLE APIs)
- Bluetooth adapter that supports BLE

### Configuration

On first launch the application creates a default configuration file at:

```
%AppData%\BusyLight\appsettings.json
```

Open that file and fill in your Azure AD credentials:

```json
{
  "AzureAd": {
    "ClientId": "<your-application-client-id>",
    "TenantId": "<your-directory-tenant-id>"
  },
  "Polling": {
    "GraphIntervalSeconds": 30,
    "BleRetryIntervalSeconds": 10
  },
  "PresenceMap": {
    "Available":       { "Enabled": true,  "R": 0,   "G": 255, "B": 0,   "Brightness": 180, "Mode": 0, "Speed": 0  },
    "Busy":            { "Enabled": true,  "R": 255, "G": 0,   "B": 0,   "Brightness": 180, "Mode": 0, "Speed": 0  },
    "DoNotDisturb":    { "Enabled": true,  "R": 255, "G": 0,   "B": 0,   "Brightness": 180, "Mode": 4, "Speed": 80 },
    "Away":            { "Enabled": true,  "R": 255, "G": 165, "B": 0,   "Brightness": 150, "Mode": 1, "Speed": 60 },
    "BeRightBack":     { "Enabled": true,  "R": 255, "G": 165, "B": 0,   "Brightness": 150, "Mode": 4, "Speed": 40 },
    "Offline":         { "Enabled": false, "R": 0,   "G": 0,   "B": 255, "Brightness": 100, "Mode": 0, "Speed": 0  },
    "PresenceUnknown": { "Enabled": false, "R": 128, "G": 128, "B": 128, "Brightness": 100, "Mode": 0, "Speed": 0  }
  }
}
```

**PresenceMap fields:**

| Field | Description |
|-------|-------------|
| `Enabled` | `false` hides the entry from the manual override menu and ignores it when received from Teams |
| `R` / `G` / `B` | LED colour (0–255 each) |
| `Brightness` | Overall brightness (0–255); capped at 60 % on the device to protect power |
| `Mode` | Animation mode (see table below) |
| `Speed` | Animation speed 0–255 (higher = faster) |

### Option A — Pre-built executable (recommended)

Download `BusyLight.exe` from the [latest GitHub Release](https://github.com/Kingspor/esp32-BusyLight/releases/latest) and run it directly — no SDK or build step required.

### Option B — Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8).

```bash
cd app/BusyLight
dotnet build
dotnet run
```

On first run a browser window opens for Microsoft sign-in.  After granting
consent the token is cached (DPAPI-encrypted) and future starts are silent.

The app appears as a coloured dot in the system tray.  Right-click for the
context menu:

```
Status: Available
─────────────────
Override ▶  Available / Busy / DoNotDisturb / Away / BeRightBack
─────────────────
Start with Windows  ✓
─────────────────
Exit
```

---

## Compatibility

App and firmware communicate over a **versioned BLE protocol**. Each release specifies its protocol version — both components must use the **same version** to work correctly.

| Release | Protocol Version | Compatible firmware | Compatible app |
|---------|:----------------:|---------------------|----------------|
| v0.1.0  | 1                | v0.1.0              | v0.1.0         |

**What triggers a protocol version bump?**

Only **breaking changes** to the BLE communication require a bump:

- Packet size changes (currently 6 bytes: R, G, B, Brightness, Mode, Speed)
- Byte positions are reordered or repurposed
- Service or characteristic UUIDs change
- New mandatory characteristics are added

Adding new animation modes is fully backwards-compatible and does **not** require a version bump.

**What happens on mismatch?**

The app reads the protocol version characteristic (`feda0103-…`) immediately after connecting. If the firmware reports a different version, a balloon notification is shown:

> *"Protokoll-Inkompatibilität auf 'BusyLight-XXYY': Firmware v0 ≠ App erwartet v1. Bitte Firmware oder App aktualisieren."*

The connection stays open, but LED commands may not work as expected until both sides are updated.

---

## Animation Modes

| Mode | Value | Description | Speed effect |
|------|-------|-------------|-------------|
| Static | 0 | Solid colour | Not used |
| Pulse | 1 | Brightness fades in and out | Higher = faster pulse |
| Chase | 2 | Single LED chases around ring | Higher = faster movement |
| Rainbow | 3 | Full spectrum rotates (ignores R/G/B) | Higher = faster rotation |
| Blink | 4 | All LEDs blink on/off | Higher = faster blink |

---

## Project Structure

```
firmware/BusyLight/
├── BusyLight.ino          Main sketch (setup / loop)
├── config.h               Pin definitions, UUIDs, constants
├── LedController.h/.cpp   NeoPixel animation state machine
└── BleServer.h/.cpp       BLE GATT server and callbacks

app/BusyLight/
├── BusyLight.csproj
├── Program.cs             Entry point, single-instance mutex
├── TrayApplication.cs     Tray icon, context menu, orchestration
├── Services/
│   ├── GraphService.cs    MSAL auth + Graph API polling
│   ├── BleService.cs      BLE scan, connect, send commands
│   └── ConfigurationService.cs  Load / save appsettings.json
├── Models/
│   ├── AppSettings.cs     Strongly-typed configuration model
│   └── LedCommand.cs      6-byte command packet
└── Helpers/
    └── AutostartHelper.cs Registry read/write for autostart
```

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Device not found during BLE scan | Ensure Bluetooth is enabled; power-cycle the ESP32 |
| `ClientId is not configured` balloon | Fill in `ClientId` and `TenantId` in `%AppData%\BusyLight\appsettings.json` |
| Auth window does not appear | Check that **Allow public client flows** is enabled in the Azure portal |
| `Presence.Read` permission denied | Verify API permissions are added and admin consent was granted |
| LED ring does not light up | Check wiring (GPIO 5 → DIN) and adequate power supply (5 V) |
| Wrong colours shown | Edit `PresenceMap` in `appsettings.json` to customise colours per status |
| App starts multiple times | Only one instance is allowed — the second one exits silently |

---

## License

MIT — see [LICENSE](LICENSE).
