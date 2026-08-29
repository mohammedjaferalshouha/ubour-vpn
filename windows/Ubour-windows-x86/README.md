# Ubour VPN for Windows (عبور)

A high-performance, modern multi-mode VPN & Network AdBlock client for Windows 10 & 11 (32-bit).

## Key Features

- **4 Powerful Modes**:
  1. **Cloudflare WARP + AdBlock**: High-speed encrypted cloud WireGuard tunnel with integrated zero-latency DNS ad & tracker blocking.
  2. **AdBlock Only**: 100% direct line ISP speed with local microsecond DNS ad blocking (dual-stack IPv4/IPv6 port 53 listener).
  3. **GoodbyeDPI Bypass**: Direct DPI circumvention with customizable strength (-1, -5, -9).
  4. **Custom VLESS Reality**: Direct integration with Sing-box core for advanced proxy connections.
- **Microsecond DNS Filter Engine**: Ultra-compact in-memory FNV-1a hash matching engine loaded with over 182,000 AdGuard and uBlock Origin rules.
- **Anti-DoH / Anti-DoT Protection**: Prevents hardcoded browser DNS leaks and ensures 100% filter enforcement.
- **Modern UI & Localization**: Full Arabic and English support, system dark/light theme, and seamless system tray background minimization.
- **Zero Installation / Portable**: Fully standalone package ready to run.

## How to Run

1. Extract the `.zip` archive completely to any folder.
2. Run `Ubour.exe` as Administrator (required for packet driver and DNS interception).
3. Select your preferred mode and click **Connect**.

## Architecture & Bundled Core Engines

- `sing-box.exe` v1.13.19 (Official x86)
- `goodbyedpi.exe` v0.2.3rc3 (Official x86)
- `WinDivert.dll` & `WinDivert64.sys`
- `wintun.dll`

## Licenses

All third-party engine licenses and notices are preserved in the `licenses/` directory.

