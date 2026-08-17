# Ubour

Portable Windows launcher for the bundled official GoodbyeDPI engine.

## Behaviour

- Starts the engine with the verified `-9` profile, without a PowerShell window.
- Minimizing hides the main window and keeps the engine active through the system-tray icon.
- Closing the window stops the engine and exits fully.
- Checks the official engine releases when opened. It never downloads an update automatically.
- Uses `x86_64` on 64-bit Windows and `x86` on 32-bit Windows.

## Release notes

`Ubour.exe` requires administrator permission because its bundled engine uses a Windows packet-interception driver.

The application does not change the public IP address and is not a VPN.

## Licensing

The engine and its third-party notices are copied unchanged into `licenses/` from the official GoodbyeDPI release. Review them before publishing.
