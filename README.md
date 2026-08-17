<div align="center">

<img src="assets/images/app_icon.png" width="128" height="128" alt="Ubour Logo" />

# Ubour (عبور)
### Next-Generation Anti-Censorship & AdBlock Suite for Windows & Android

[![License: MIT](https://img.shields.io/badge/License-MIT-emerald.svg)](LICENSE)
[![Platform: Android](https://img.shields.io/badge/Platform-Android%207.0%2B-blue.svg)](#-android-application)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6.svg)](#-windows-pc-application)
[![Privacy: Zero-Logging](https://img.shields.io/badge/Privacy-100%25%20Local%20%2F%20Zero--Logging-success.svg)](#-privacy--security)
[![Engine: ByeDPI + GoodbyeDPI](https://img.shields.io/badge/Engine-ByeDPI%20%7C%20GoodbyeDPI-orange.svg)](#-credits--acknowledgments)

**[English](README.md) | [العربية (Arabic)](README.ar.md)**

</div>

---

## 📖 Overview

**Ubour (عبور)** is a high-performance, open-source censorship circumvention and network privacy suite engineered for **Android** and **Windows PC**. 

Unlike conventional VPN services that route all your internet traffic through external third-party proxy servers (which can introduce latency, bandwidth caps, and privacy risks), **Ubour** operates **directly on your device**:
- **Censorship Circumvention**: Manipulates and segments TCP/TLS packets and HTTP headers at the socket/kernel level to evade Deep Packet Inspection (DPI) censorship systems deployed by Internet Service Providers (ISPs).
- **Built-in Local Ad & Tracker Blocker**: Integrates full rule lists from **uBlock Origin** and **AdGuard**, filtering unwanted advertisements and telemetry locally before they leave your device.
- **Native Full Speeds**: Zero server overhead ensures 100% of your native ISP bandwidth and minimal latency.

---

## 📸 Screenshots Showcase

<div align="center">
<table>
  <tr>
    <td align="center" width="25%"><b>Main Interface (Connected)</b></td>
    <td align="center" width="25%"><b>Split Tunneling (Excluded)</b></td>
    <td align="center" width="25%"><b>All Apps & Search</b></td>
    <td align="center" width="25%"><b>Settings & Optimization</b></td>
  </tr>
  <tr>
    <td><img src="assets/images/android_main.png" alt="Main Interface" width="100%" /></td>
    <td><img src="assets/images/android_split_tunnel.png" alt="Split Tunneling" width="100%" /></td>
    <td><img src="assets/images/android_all_apps.png" alt="All Apps Search" width="100%" /></td>
    <td><img src="assets/images/android_settings.png" alt="Settings & Battery" width="100%" /></td>
  </tr>
</table>
</div>

---

## 🌟 Key Features

### 📱 Android Application (`apk+ublock`)
* **Integrated Native ByeDPI Engine**: Real-time TCP segmentation, SNI fragmentation, and fake packet injection.
* **Local Ad & Tracker Shield**: Over 160,000+ active blocking rules compiled from uBlock Origin and AdGuard.
* **Split Tunneling with Tabs & Live Search**:
  * Easily exclude sensitive applications (e.g., Banking apps, government portals like Sanad) to connect directly.
  * Dedicated tabs: **All Apps** and **Excluded Only** with instantaneous real-time search.
* **Battery Optimization Intelligence**: Automated detection and status indicator for background process stability.
* **3 Operation Modes**:
  1. *Full Mode*: Comprehensive DPI circumvention + Ad & Tracker blocking.
  2. *AdBlock Only*: Ultra-light battery saver mode for pure ad filtering.
  3. *Bypass Only*: Pure DPI evasion without packet filtering.
* **Custom Encrypted DNS**: Switch effortlessly between AdGuard DNS, Cloudflare (1.1.1.1), and Google DNS.
* **Cryptographically Signed**: Official production keystore with Android V2/V3 signature schemes for seamless OTA updates.
* **Modern Material 3 Interface**: Elegant Dark/Light mode with comprehensive Arabic (RTL) & English localization.

### 💻 Windows PC Application (`vpn/Ubour`)
* **GoodbyeDPI & WinDivert Integration**: Kernel-level packet filter bypassing ISP DPI with zero latency.
* **Multi-Preset DPI Selector**: Quick switching between Standard Mode, Fast Fragmentation, and Fake SNI.
* **Lightweight WPF Interface**: Clean Arabized GUI with system tray minimization and auto-reconnect.
* **Built-in Auto Updater**: Seamless one-click verification against GitHub releases.

---

## 📦 Releases & Downloads

| Platform | Package | Architecture | Description |
| :--- | :--- | :--- | :--- |
| **Android** | `Ubour-VPN-v1.0.0-Release.apk` | `arm64-v8a`, `armeabi-v7a`, `x86_64`, `x86` | Official Signed Release APK |
| **Windows x64** | `Ubour-windows-x64.zip` | `x64` (64-bit) | Standalone Executable (Win 10/11) |
| **Windows x86** | `Ubour-windows-x86.zip` | `x86` (32-bit) | Standalone Executable (Win 7/8/10/11) |

---

## 🛠️ Build Instructions

### Android App
```bash
# Clone the repository
git clone https://github.com/mohammedjaferalshouha/ubour-vpn.git
cd ubour-vpn/apk+ublock

# Build signed release APK
./gradlew assembleRelease
```
The output APK is located at: `app/build/outputs/apk/release/app-release.apk`

### Windows PC App
```bash
cd ubour-vpn/vpn/Ubour

# Publish self-contained Windows application
dotnet publish -c Release -r win-x64 --self-contained true
```

---

## 🔒 Privacy & Security

- **Zero Logging**: Ubour does not log, collect, or transmit any user browsing activity, IP addresses, or DNS queries.
- **Client-Side Processing**: All packet manipulation and ad blocking occur 100% on your device.
- **Open Source**: Complete transparency with freely auditable source code.

---

## 🤝 Credits & Acknowledgments

Ubour is built upon outstanding open-source projects:
- **[GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI)** by ValdikSS — Windows DPI circumvention utility.
- **[WinDivert](https://github.com/basil00/Divert)** by basil00 — User-mode packet capture and diversion library for Windows.
- **[ByeDPI](https://github.com/hiddify/ByeDPI)** & **[ByeDPIAndroid](https://github.com/dovecoteescapee/ByeDPIAndroid)** by dovecoteescapee — Android native ByeDPI engine.
- **[uBlock Origin](https://github.com/gorhill/uBlock)** by Raymond Hill (gorhill) — Open-source content blocking rules.
- **[AdGuard Filters](https://github.com/AdguardTeam/AdguardFilters)** — Comprehensive ad and tracking protection rulesets.

---

## 📄 License

Distributed under the **MIT License**. See [`LICENSE`](LICENSE) for complete terms.
