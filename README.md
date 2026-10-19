<div align="center">

<img src="assets/images/app_icon.png" width="128" height="128" alt="Ubour Logo" />

# Ubour (عبور)
### Next-Generation Anti-Censorship & Local AdBlock Suite for Windows & Android

[![Latest Release](https://img.shields.io/github/v/release/mohammedjaferalshouha/ubour-vpn?color=10B981&label=Release&logo=github)](https://github.com/mohammedjaferalshouha/ubour-vpn/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-emerald.svg)](LICENSE)
[![CI/CD Build & Test](https://github.com/mohammedjaferalshouha/ubour-vpn/actions/workflows/build_release.yml/badge.svg)](https://github.com/mohammedjaferalshouha/ubour-vpn/actions/workflows/build_release.yml)
[![Security Scan](https://github.com/mohammedjaferalshouha/ubour-vpn/actions/workflows/security_scan.yml/badge.svg)](https://github.com/mohammedjaferalshouha/ubour-vpn/actions/workflows/security_scan.yml)
[![CodeQL Analysis](https://github.com/mohammedjaferalshouha/ubour-vpn/actions/workflows/codeql.yml/badge.svg)](https://github.com/mohammedjaferalshouha/ubour-vpn/actions/workflows/codeql.yml)
[![Platform: Android](https://img.shields.io/badge/Platform-Android%207.0%2B-blue.svg)](#android-app)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6.svg)](#windows-app)
[![Privacy: Zero-Logging](https://img.shields.io/badge/Privacy-100%25%20Local%20%2F%20Zero--Logging-success.svg)](#privacy-security)
[![Knowledge Graph](https://img.shields.io/badge/Architecture-Knowledge%20Graph-8A2BE2.svg)](https://mohammedjaferalshouha.github.io/ubour-vpn/)

**[English](README.md) | [العربية (Arabic)](README.ar.md)**

<br/>

### ⚡ One-Click Direct Downloads (تحميل مباشر بنقرة واحدة)

[![Download Android APK](https://img.shields.io/badge/Download-Android%20APK%20(Universal)-3DDC84?style=for-the-badge&logo=android&logoColor=white)](https://github.com/mohammedjaferalshouha/ubour-vpn/releases/latest/download/Ubour-VPN-Release.apk)
[![Download Windows x64](https://img.shields.io/badge/Download-Windows%20x64%20(64--bit)-0078D6?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/mohammedjaferalshouha/ubour-vpn/releases/latest/download/Ubour-windows-x64.zip)
[![Download Windows x86](https://img.shields.io/badge/Download-Windows%20x86%20(32--bit)-5C2D91?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/mohammedjaferalshouha/ubour-vpn/releases/latest/download/Ubour-windows-x86.zip)
[![Interactive Knowledge Graph](https://img.shields.io/badge/Interactive%20Graph-Explore%20Architecture-8A2BE2?style=for-the-badge&logo=diagram-next&logoColor=white)](https://mohammedjaferalshouha.github.io/ubour-vpn/)
[![All Releases & Archive](https://img.shields.io/badge/All%20Releases-Previous%20Versions%20Archive-orange?style=for-the-badge&logo=github&logoColor=white)](https://github.com/mohammedjaferalshouha/ubour-vpn/releases)

</div>

---

## 📖 Overview

**Ubour (عبور)** is a high-performance, open-source censorship circumvention and network privacy suite engineered for **Android** and **Windows PC**. 

Unlike conventional VPN services that route all your internet traffic through external third-party proxy servers (introducing latency, bandwidth caps, and privacy risks), **Ubour** operates **directly on your device**:
- **Censorship Circumvention**: Manipulates and segments TCP/TLS packets and HTTP headers at the socket/kernel level to evade Deep Packet Inspection (DPI) censorship systems deployed by Internet Service Providers (ISPs).
- **Built-in Local Ad & Tracker Blocker**: Integrates over 180,000+ full rule lists from **uBlock Origin** and **AdGuard**, filtering unwanted advertisements and telemetry locally before they leave your device.
- **Independent DNS & Privacy**: Pure direct connection with neutral DNS resolvers (Google DNS 8.8.8.8, Quad9 9.9.9.9, AdGuard DNS 94.140.14.14) without forcing intermediary proxies.
- **Isolated Cloudflare WARP & VLESS Reality Support**: Optional dedicated tunnels for specialized censorship environments when explicitly chosen.
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

<a id="android-app"></a>
### 📱 Android Application (`apk+ublock`)
* **Integrated Native ByeDPI Engine**: Real-time TCP segmentation, SNI fragmentation, and fake packet injection.
* **Local Ad & Tracker Shield**: Over 180,000+ active blocking rules compiled from uBlock Origin and AdGuard.
* **Split Tunneling with Tabs & Live Search**:
  * Easily exclude sensitive applications (e.g., Banking apps, government portals like Sanad) to connect directly.
  * Dedicated tabs: **All Apps** and **Excluded Only** with instantaneous real-time search.
* **Battery Optimization Intelligence**: Automated detection and status indicator for background process stability.
* **Independent DNS Engine**: Fully decoupled DNS resolution defaulting to standard Google DNS (8.8.8.8 / 8.8.4.4), Quad9 (9.9.9.9 / 149.112.112.112), and AdGuard DNS.
* **4 Flexible Operation Modes**:
  1. *Direct DPI Bypass (100% Speed)*: Pure direct connection without proxy overhead.
  2. *Direct Bypass + AdBlock*: Full DPI evasion + local ad & tracker filtering.
  3. *AdBlock Only*: Ultra-light battery saver mode for pure local ad blocking.
  4. *Cloudflare WARP + AdBlock*: Full encrypted tunnel via Cloudflare WARP.
* **Custom VLESS Reality Support**: Built-in Sing-box core supporting direct VLESS Reality custom endpoints.
* **Upstream Updates Center**: Live checking and one-click rules update against official upstream repositories.
* **Cryptographically Signed**: Official production keystore with Android V2/V3 signature schemes for seamless OTA updates.
* **Modern Material 3 Interface**: Elegant Dark/Light mode with comprehensive Arabic (RTL) & English localization.

<a id="windows-app"></a>
### 💻 Windows PC Application (`windows/src/Ubour`)
* **Multi-Mode Operation Suite**:
  1. *Bypass Only (GoodbyeDPI)*: Direct kernel-level DPI evasion via WinDivert with zero latency and native speeds.
  2. *Bypass + AdBlock*: Full DPI bypass combined with local DNS ad and telemetry filtering.
  3. *AdBlock Only*: Ultra-low footprint local DNS ad blocking without packet manipulation.
  4. *Cloudflare WARP*: Full WireGuard tunnel via high-performance Wintun virtual network adapter and Sing-box core.
* **⚡ One-Click WARP Port Scanner**: Built-in port latency tester to scan and pick the most responsive Cloudflare WARP port (`2408`, `4500`, `1701`, etc.) automatically.
* **Custom Endpoint & Port Selector**: Configure custom Cloudflare clean IPs and ports (`WarpEndpointHost`, `WarpEndpointPort`) directly from the GUI.
* **Sub-Mode Toggles**: Choose between *With AdBlock* (dual protection) or *Tunnel Only* (maximum throughput).
* **Local Custom Configuration**: Supports loading custom `singbox_warp.json` configurations directly from the application folder.
* **Dual Architecture Standalone**: Native x64 and x86 standalone binaries without requiring external runtimes.
* **Built-in Auto Updater**: Automatic verification against the latest GitHub releases.
* **Rigorous Verification**: 81 comprehensive unit tests passing with 100% success across automated CI/CD.

---

## 📦 Direct Downloads & Packages Table

| Platform | Direct Download Link | Architecture | Type | Status |
| :--- | :--- | :--- | :--- | :--- |
| **Android APK** | [📥 **Download Universal APK**](https://github.com/mohammedjaferalshouha/ubour-vpn/releases/latest/download/Ubour-VPN-Release.apk) | `arm64-v8a`, `armeabi-v7a`, `x86_64`, `x86` | Universal Signed Release APK | Verified ✓ |
| **Windows x64** | [📥 **Download Windows x64**](https://github.com/mohammedjaferalshouha/ubour-vpn/releases/latest/download/Ubour-windows-x64.zip) | `x64` (64-bit) | Standalone Executable (Win 10/11) | Verified ✓ |
| **Windows x86** | [📥 **Download Windows x86**](https://github.com/mohammedjaferalshouha/ubour-vpn/releases/latest/download/Ubour-windows-x86.zip) | `x86` (32-bit) | Standalone Executable (Win 7/8/10/11) | Verified ✓ |
| **All Releases** | [📚 **View Full Release Archive**](https://github.com/mohammedjaferalshouha/ubour-vpn/releases) | All Platforms | Full Release Changelog & Assets | Complete |

---

## 🛠️ Build & Test Instructions

### Android App
```bash
# Clone the repository
git clone https://github.com/mohammedjaferalshouha/ubour-vpn.git
cd ubour-vpn/apk+ublock

# Run unit tests and lint checks
./gradlew test lint

# Build signed release APK
./gradlew assembleRelease
```
The output APK is located at: `app/build/outputs/apk/release/app-release.apk`

### Windows PC App
```bash
cd ubour-vpn/windows/src

# Run all 81 unit tests
dotnet test Ubour.Tests/Ubour.Tests.csproj -c Release

# Publish Windows applications (Self-Contained Portable)
dotnet publish Ubour/Ubour.csproj -c Release -r win-x64 --self-contained true
dotnet publish Ubour/Ubour.csproj -c Release -r win-x86 --self-contained true
```

---

<a id="privacy-security"></a>
## 🔒 Privacy & Security

- **Zero Logging**: Ubour does not log, collect, or transmit any user browsing activity, IP addresses, or DNS queries.
- **Client-Side Processing**: All packet manipulation and ad blocking occur 100% locally on your device.
- **Automated Security Scanning**: Continuous secret leak scanning via `gitleaks` and automated dependency vulnerability audits via Dependabot.
- **CodeQL Advanced Analysis**: Strict static code analysis enabled across C#, Java, Kotlin, and GitHub Actions.
- **Security Policy**: Responsible vulnerability disclosure guidelines detailed in [`SECURITY.md`](SECURITY.md).

---

## 🤝 Credits & Acknowledgments

Ubour is built upon outstanding open-source projects:
- **[GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI)** by ValdikSS — Windows DPI circumvention utility.
- **[WinDivert](https://github.com/basil00/Divert)** by basil00 — User-mode packet capture and diversion library for Windows.
- **[Wintun](https://www.wintun.net/)** by WireGuard LLC — High-performance TUN driver for Windows.
- **[ByeDPI](https://github.com/hiddify/ByeDPI)** & **[ByeDPIAndroid](https://github.com/dovecoteescapee/ByeDPIAndroid)** by dovecoteescapee & hufrea — Android native ByeDPI engine.
- **[Sing-box](https://github.com/SagerNet/sing-box)** — Universal proxy platform for WARP & VLESS.
- **[uBlock Origin](https://github.com/gorhill/uBlock)** by Raymond Hill (gorhill) — Open-source content blocking rules.
- **[AdGuard Filters](https://github.com/AdguardTeam/AdguardFilters)** — Comprehensive ad and tracking protection rulesets.

---

## 📄 License

Distributed under the **MIT License**. See [`LICENSE`](LICENSE) for complete terms.
