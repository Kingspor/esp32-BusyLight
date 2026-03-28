# BusyLight — User Guide

## What is BusyLight?

BusyLight connects your Microsoft Teams presence status to a physical LED ring on your desk.
When you're **Available** the ring glows green; **Busy** glows red; **Away** glows orange — automatically, without any manual steps.

---

## Requirements

- Windows 10 (build 22621 / 22H2) or later
- Bluetooth adapter (built-in or USB dongle)
- BusyLight hardware (ESP32 + WS2812B LED ring)
- Microsoft 365 account with Teams

---

## First-Time Setup

### 1. Azure AD App Registration

BusyLight needs permission to read your Teams presence. A one-time setup in the Azure portal is required:

1. Go to [portal.azure.com](https://portal.azure.com) → **Azure Active Directory** → **App registrations** → **New registration**
2. Name: `BusyLight` · Account type: *Accounts in this organizational directory only*
3. Redirect URI: `http://localhost`
4. After creation, note the **Application (client) ID** and **Directory (tenant) ID**
5. Go to **API permissions** → **Add permission** → Microsoft Graph → Delegated → add `Presence.Read` and `User.Read`
6. Click **Grant admin consent** (if required by your organization)

### 2. Configure the App

Open `%APPDATA%\BusyLight\appsettings.json` and fill in:

```json
{
  "AzureAd": {
    "ClientId": "paste-your-application-id-here",
    "TenantId": "paste-your-tenant-id-here"
  }
}
```

Or enter the values in the app under **Einstellungen → Allgemein → Azure AD / Entra ID**.

### 3. Power on the BusyLight Hardware

Turn on or plug in the ESP32 BusyLight device.

### 4. Start BusyLight

Double-click `BusyLight.exe`. It appears in the system tray (taskbar, bottom-right area).

**First start:** A device picker dialog appears automatically.
- Wait 5–10 seconds for scanning to complete
- Select your BusyLight from the list and click **Auswählen**
- The device is saved and will reconnect automatically on future starts

### 5. Sign In to Microsoft

A browser window opens for Microsoft authentication. Sign in with your Microsoft 365 account and grant the requested permissions. This only happens once — the token is cached securely.

---

## Daily Use

### Tray Icon

The tray icon shows your current Teams presence as a coloured dot:

| Color | Status |
|---|---|
| 🟢 Green | Available |
| 🔴 Red | Busy / Do Not Disturb |
| 🟠 Orange | Away / Be Right Back |
| ⚫ Gray | Offline / Unknown |

**Double-click** the tray icon to open the BusyLight window.

### Context Menu (right-click the tray icon)

| Item | Action |
|---|---|
| **BusyLight öffnen** | Open the settings/status window |
| **Status jetzt abrufen** | Fetch Teams presence immediately |
| **Override → [status]** | Manually set the LED to a specific profile |
| **Clear Override** | Return to automatic Teams presence |
| **Start with Windows** | Enable/disable autostart |
| **Exit** | Quit the application |

---

## Settings Window

Open by double-clicking the tray icon.

### Status Bar (bottom)

Shows at a glance (left to right):
- **Teams:** current presence status
- **BLE device** name and connection state
- **Protocol:** firmware protocol version (shown as `v1` when connected)
- **App version** (e.g. `App v0.1.0`)

### Tab: Präsenz

**Top toolbar:**
- **Teams:** current presence status (updates live)
- **Jetzt abrufen** — fetch presence immediately from Microsoft Graph
- **Override** — manually select a LED profile; click **Anwenden** to activate
- **Override beibehalten** — if checked, the active override is remembered and restored after a restart

**LED Profile Table:**

Each row represents one Teams presence status. You can configure:

| Column | Description |
|---|---|
| **Status** | Teams presence name |
| **Aktiv** | Enable/disable this profile |
| **Farbe** | Color swatch — click to open the color wheel |
| **R / G / B** | Manual RGB values (0–255) |
| **Hell. %** | Brightness in percent |
| **Modus** | Animation: Static, Pulse, Chase, Rainbow, Blink, Fill |
| **Geschw.** | Animation speed (0 = slow, 255 = fast) |
| **~Hz** | Estimated animation frequency (informational) |

**Live-Vorschau checkbox** (top-right): when checked, the LED ring shows a live preview as you change settings. Uncheck to stop the preview.

### Tab: Allgemein

| Setting | Description |
|---|---|
| **Client-ID / Tenant-ID** | Azure AD application registration |
| **Teams-Abfrageintervall** | How often to poll Teams (seconds, default 30) |
| **BLE Retry-Intervall** | How often to retry BLE reconnect (seconds, default 10) |
| **Maximale Helligkeit** | Global brightness cap 0–100 % |
| **LEDs bei gesperrtem Bildschirm nicht ändern** | When checked (default), the LED state is frozen while your Windows session is locked. The correct color is restored automatically when you unlock. Uncheck to let Teams presence changes update the LEDs even while locked. |

### Tab: BLE-Gerät

Shows the currently paired device with its connection status.
Click **Neues Gerät suchen…** to scan for a different BusyLight and replace the current pairing.

### Tab: Zuordnung

Maps incoming Teams statuses to LED profiles. Useful if you want e.g. "Do Not Disturb" and "Busy" to show the same red profile.

### Menu: Hilfe

**Hilfe → Dokumentation öffnen** opens the online documentation in your browser.

### Saving Settings

Click **Datei → Speichern** or press the Save menu item.
An asterisk `*` in the title bar indicates unsaved changes.
When closing with unsaved changes, you will be asked whether to save.

> **Note:** Saving settings (colors, brightness, mode, …) does **not** interrupt the BLE connection. Changes take effect immediately — the new LED command is sent right after saving. The connection is only re-established if you select a different device via **Neues Gerät suchen…**.

---

## Troubleshooting

### The device is not found during scan

- Make sure the BusyLight is powered on and within Bluetooth range (~10 m)
- Check that Bluetooth is enabled on your laptop
- Click **Erneut suchen** in the picker dialog

### The LED doesn't change color

- Check the tray icon — if it's gray, Teams presence may be "Offline" or unknown
- Right-click the tray → **Status jetzt abrufen** to force a refresh
- Check **Tab: Präsenz** — the row for the current status must have **Aktiv** checked. If a status is disabled, the LEDs keep the last active color rather than turning off.

### The LED briefly shows the wrong color when reconnecting

This is expected: the device retains its last state when the connection drops. The correct color is restored within a second after reconnect.

### Authentication keeps popping up

Delete `%APPDATA%\BusyLight\msal_token_cache.bin` and restart — this forces a fresh sign-in.

### The app doesn't start with Windows

Open BusyLight, right-click the tray icon, and toggle **Start with Windows**.

---

## Uninstallation

1. Exit BusyLight from the tray context menu
2. Delete `BusyLight.exe`
3. Delete `%APPDATA%\BusyLight\` (contains settings and token cache)
4. If autostart was enabled: remove the registry entry at
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\BusyLight`
