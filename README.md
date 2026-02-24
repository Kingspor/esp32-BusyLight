# ESP32 Bluetooth BusyLight
Using an ESP32 with NeoPixel-LEDs to create a Bluetooth-BusyLight, which reads the current status from MS-Teams. Additionally controllable via Desktop(Traybar) Application.

## Features
- 🔵 Bluetooth Low Energy (BLE) Connection
- 🌈 WS2812B RGB LED-Ring (7 LEDs)
- 🔋 LiIon-powered (18650 Li-Ion)
- ⚡ USB-C recharging


## Hardware
- ESP32 WROOM-32 Development Board (Type-C)
- WS2812B LED-Ring (7 LEDs)
- 18650 Li-Ion Akku (3000-3500 mAh)
- TP4056 charging module
- MT3608 Step-Up Converter (5V)

ToDo: [Vollständige Teileliste](hardware/parts_list.md)

## Software Requirements
- Arduino IDE 2.x
- ESP32 Board Support
- Adafruit NeoPixel Library

## Installation
ToDo: [Siehe Setup Guide](docs/setup_guide.md)

## Teams-Status Farben
- 🟢 Green: Available
- 🔴 Red: Busy
- 🔴 Red (Pulse): Do not disturb
- 🟡 Yellow: Away
- ⚪ Off: Offline

## License
MIT License - siehe [LICENSE](LICENSE)

## Credits
- Based on Adafruit NeoPixel Library


![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Arduino](https://img.shields.io/badge/Arduino-2.x-green.svg)
![ESP32](https://img.shields.io/badge/ESP32-supported-orange.svg)