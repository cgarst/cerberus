# Development

This guide covers building Cerberus from source, running tests, exporting the web UI, generating screenshots, and working with the internal HTTP API.

## File structure

- `CerberusService/`: .NET 10 Windows tray service, HTTP server, native display and audio interop, telemetry, and WPF `SetupWindow`
- `CerberusExpo/`: Expo / React Native mobile, tablet, and web UI (exported to `CerberusService/WebUI/`)
- `CerberusService.Tests/`: unit tests (`RoomControllerTests`) and console environment integration tests
- `installer/`: Inno Setup script for `CerberusServiceSetup.exe`
- `scripts/`: build scripts, dev installer, web export, RTSS OpenGL test window, and screenshot generator

## Build and test

```powershell
# Build CerberusService.exe, test executables, and run unit tests
.\scripts\build.ps1
.\scripts\build.ps1 -SkipChecks

# Build the standalone Windows installer (requires Inno Setup 6)
.\scripts\build-installer.ps1
```

For local development, double-click [scripts/dev-install.cmd](scripts/dev-install.cmd) to elevate, build `CerberusServiceSetup.exe`, install with autostart, and start the service. You can also run `.\build\CerberusService.exe --console` directly.

`CerberusService.exe` enforces a single instance (except `--demo-setup`) and supports these flags:
- `--console`: mirror logs to stdout
- `--config <path>`: explicit config path (defaults to `config.json` next to the `.exe` if present, otherwise `C:\ProgramData\CerberusService\config.json`)
- `--configure [--autostart]` / `--unconfigure`: register or remove the Windows Firewall rule, HTTP URL ACL, and logon scheduled task
- `--demo-setup`: launch the WPF Setup & Profiles window with safe demo values

## Exporting the web UI and demo mode

To rebuild and sync `CerberusExpo` into `CerberusService/WebUI/`:

```bash
./scripts/export-web.sh
```

To preview the web or mobile UI with simulated hardware telemetry (`Ryzen 7 7800X3D`, `Radeon RX 9070 XT`, `32 GB DDR5`), active game hero art, and sample shortcuts and windows:
- Toggle Demo Mode (Simulated PC) in Settings
- Or open the web UI with `?demo=1` (for example `http://localhost:8090/?demo=1`, or add `&streaming=1` to preview the active streaming state)

## Testing RTSS / Afterburner OSD

Use the minimal OpenGL 3D test window ([scripts/gltest.cs](scripts/gltest.cs)) to test RTSS/Afterburner OSD toggling without launching a full game:

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /out:build\gltest.exe scripts\gltest.cs
.\build\gltest.exe
```

## Screenshots

The script [scripts/generate_screenshots.py](scripts/generate_screenshots.py) generates the README screenshots in [docs/screenshots/](docs/screenshots/) from `?demo=1` (Playwright) and `--demo-setup` (Win32 `PrintWindow` in dark mode):

```powershell
pip install playwright pillow
playwright install chromium
python scripts/generate_screenshots.py
python scripts/generate_screenshots.py --only phone tablet streaming config
```

## Configuration and internal HTTP API

Cerberus stores runtime settings in `config.json`, initialized from [config.example.json](config.example.json). Key options include `Port` (`8090`), `BearerToken`, `Profiles` (`Id`, `Name`, `IsDefault`, `Monitor`, `Audio`, `Width`, `Height`, `RefreshRate`, `Hdr`, `ConsoleEnvironmentMode`, `CloseApps`, `Integrations`), `ControllerTriggerProfileId`, `WakeGuardSeconds` (`15`), `IdleRevertMinutes` (`45`), `IdleSleepAfterRevert` (`false`), and `StreamingVirtualDisplayPatterns`.

When `BearerToken` is configured, API requests require `Authorization: Bearer <token>`:

| Method & Path | Description |
| --- | --- |
| `GET /` | Embedded mobile and tablet web UI |
| `GET /devices` | Connected displays, audio playback devices, and registered integrations |
| `GET /profiles` | Configured profiles, availability, and active/default profile IDs |
| `POST /profiles/{id}/activate` | Activate any profile by ID (optional `consoleEnvironmentMode`) |
| `GET /config` / `POST /config` | Read or update and save `config.json` live |
| `GET /health` | Status, active mode/profile, streaming state, idle seconds, and live telemetry |
| `POST /mode/living-room` / `POST /mode/office` | Switch to Living Room or Office/Default mode |
| `POST /console-environment` | Switch `ConsoleEnvironmentMode` in the current room and persist to config |
| `POST /streaming/stop` | Stop an active Sunshine/Apollo streaming session |
| `POST /sleep` / `POST /shutdown` | Switch to default profile, then sleep (S3) or shut down |
| `GET /shortcuts` / `POST /shortcuts/launch` | List or launch desktop shortcuts |
| `GET /icon?path=...` | Extract and return a shell file icon data URL |
| `GET /windows` / `POST /windows/focus` / `POST /windows/close` / `POST /windows/task-view` | List, focus, close windows, or open Task View (`Win+Tab`) |
| `POST /osd` | Set or toggle the RTSS/Afterburner On-Screen Display |
