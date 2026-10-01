# Ember

A small, text-only desktop widget for Windows. It stays quiet and shows only relevant information: CPU, GPU, memory, disk, network and Claude usage limits, in a compact btop-like style. It also has separate markdown sticky notes.

```
cpu  22%      ⣤⣤⣄⣤⣤⣄⣤⣤⣄⣤⣄⣤
↓   32.0 Mbps ⣶⣶⣾⣾⣷⣿⣷⣧⣷⣼⣦⣶ pk 56
↑    3.4 Mbps ⣤⣶⣾⣷⣶⣦⣤⣦⣴⣾⣴⣾ pk  3

─ G P U ────────────────────────
rtx  64%  71° ⣶⣶⣶⣶⣶⣶⣶⣶⣶⣶⣶⣶
  └ game.exe                 58%

─ C L A U D E ──────────────────
5h  ████████▍                42%
    ↻ 3h10                 lasts
wk  ███▋                     18%
    ↻ 4d02                 lasts
```

- Spec: [docs/requirements.md](docs/requirements.md)
- Choices where the spec is silent: [docs/decisions.md](docs/decisions.md)

## Requirements

- Windows 10 or 11, x64.
- Admin rights once, for the install (scheduled task) and for CPU temperatures (LibreHardwareMonitor kernel driver).
- For Claude usage: Claude Code installed and logged in (`%USERPROFILE%\.claude\.credentials.json`).

## Download

Each push to `main` builds `Ember.exe` on GitHub Actions (workflow **build**). Open the latest run and download the artifact **Ember-win-x64**. It contains `publish/Ember.exe`, `install.ps1` and `uninstall.ps1`.

## Build

You need the .NET 10 SDK.

```powershell
dotnet build Ember.slnx -c Release
dotnet test tests/Ember.Tests
dotnet publish src/Ember/Ember.csproj -c Release -r win-x64 -o publish
```

The result is one self-contained file: `publish\Ember.exe` (no .NET runtime needed on the target machine).

The project uses plain Win32 through P/Invoke (no WinForms), so it also builds on Linux or macOS with the .NET 10 SDK. `-p:EmberOffline=true` builds without LibreHardwareMonitorLib (no temperatures), for machines without access to nuget.org.

Project layout:

| Folder | Content |
| --- | --- |
| `src/Ember` | The Windows app: windows, GDI+ drawing, sensors (PDH, GetIfTable2, DXGI, LHM), Claude poller, notes, settings. |
| `src/Ember.Core` | Pure logic without Windows APIs: layout, braille graphs, bars, gradient, show/hide delay, outlier cores, Claude projection, parsers, markdown styling, settings. |
| `tests/Ember.Tests` | xUnit tests for `Ember.Core`. They run on any OS. |

## Install

1. Get `Ember.exe` (download or build, see above).
2. Open PowerShell **as administrator** in the folder with `install.ps1`.
3. Run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```

The script copies `Ember.exe` to `%LOCALAPPDATA%\Ember`, creates the scheduled task **Ember** (at logon, highest privileges, no time limit) and starts it. Later starts have no UAC prompt.

Options: `-Source <path to Ember.exe>`, `-InstallDir <folder>`, `-User <DOMAIN\user>`, `-NoStart`.

To remove Ember: `.\uninstall.ps1` (as administrator). Settings and notes stay in `%APPDATA%\Ember` unless you add `-RemoveData`.

## Use

- **Widget:** it shows on the desktop layer (under normal windows). Mouse clicks go through it.
  - Always visible: CPU load, network down/up, Claude 5h and weekly limits.
  - Other items show only when a value crosses its threshold for 5 s, and hide after 10 s of normal values.
- **Tray icon** (amber dot): New note, Edit layout, Settings, Exit.
- **Edit layout:** the widget gets a dark background. Drag it to move it. Right-click it for Settings / New note / Done. The position is saved as an offset from a corner of the monitor (Settings → Layout → Corner).
- **Top process lines** (`└ name …`): click for details (name, PID, path, command line, CPU, RAM, GPU, disk, start time). Right-click for End process (asks first), End process tree, Open file location.
- **Notes:** tray → New note, or the hotkey (default **Ctrl+Alt+N**). Drag the top strip to move, an edge to resize. Right-click → New note / Delete note. Markdown: `#` headings, `**bold**`, `*italic*`, `` `code` ``, `-` / `1.` lists, `- [ ]` checkboxes (click to tick). Saved 1 s after each change to `%APPDATA%\Ember\notes\<id>.md`.
- **Settings:** items on/off, thresholds, intervals, adapter exclusion list, layout (monitor, corner, offset, font size, opacity), note hotkey, start at logon, debug log. Changes apply at once. File: `%APPDATA%\Ember\settings.json`.
- **Debug log:** Settings → Other → Debug log. File: `%APPDATA%\Ember\ember.log`. Use it when an item does not show and you expect it to.
- **Second start** of `Ember.exe` brings the running widget and notes to the front.
- **Idle:** sampling stops while the session is locked or the display is off.

### Claude usage

Ember reads the OAuth token of Claude Code and calls the same endpoint as `/usage` in Claude Code (`https://api.anthropic.com/api/oauth/usage`). This endpoint is not documented and can change. Ember never refreshes the token: when the token has expired, the Claude lines hide until Claude Code refreshes it. Every 1 min by default; on HTTP 429 the interval doubles until a request succeeds.

### Antivirus

LibreHardwareMonitor loads a kernel driver to read CPU temperatures. Some antivirus tools flag it. Without the driver (or without admin rights), temperatures stay hidden; everything else works.

## Not tested

This version was written and built on Linux. No Windows machine was available. The unit tests (Core logic) pass on Linux and on the GitHub Actions Windows runner, and the full app compiles and publishes for win-x64 there. Nothing below has been run on Windows:

1. **Start and window:** the widget appears, is transparent, sits on the desktop layer (not topmost), stays visible on Win+D, and has no taskbar button.
2. **Text drawing:** sharp GDI+ text, alignment of the 32-column grid, braille graphs and block bars in Cascadia Mono (Windows 11) and Consolas (Windows 10, braille through the Segoe UI Symbol fallback).
3. **Click-through:** clicks pass through the widget everywhere except on top-process lines; the hand cursor on those lines; the near-invisible line background (alpha 3) actually catches clicks at all opacity settings.
4. **Edit mode:** drag, saved position, right-click menu.
5. **DPI:** sharp text at 100/125/150/200 %, moving between monitors with different DPI, monitor index order.
6. **Sensors:** PDH counter names and values (`% Processor Utility`, per-core instances, `% Processor Performance`, GPU Engine, GPU Adapter Memory, PhysicalDisk, Pages Input/sec); hybrid CPU core groups (efficiency class); GetIfTable2 adapter filter on real adapters (Wi-Fi, Ethernet, Hyper-V, WSL, VPNs); DXGI adapter list and integrated-GPU detection; LibreHardwareMonitor CPU/GPU temperatures (Intel and AMD), name match between LHM and DXGI.
7. **Top process** values (CPU %, private bytes, I/O) and the details popup (command line, path for protected processes).
8. **Process actions:** End process, End process tree, Open file location, with and without admin rights.
9. **Claude usage:** real request and response with a real token, expired token, HTTP 401/429 handling, projection on live data.
10. **Network quality** pings to the gateway and 1.1.1.1 (ICMP may be blocked by a firewall).
11. **Notes:** RichEdit styling while typing, checkbox click, move/resize, delete, hotkey, saving and loading, behaviour with many notes.
12. **Settings window:** layout, scrolling, Tab navigation, immediate apply, start-at-logon toggle with UAC.
13. **install.ps1 / uninstall.ps1** and the scheduled task (start at logon without UAC, no 72 h time limit).
14. **Robustness:** Explorer restart (re-attach of widget, notes and tray icon), lock/unlock and display off/on (sampling stops and starts), single instance against an elevated running instance, sleep/resume.
15. **Resource targets:** RAM < 60 MB, CPU < 1 %, GPU ≈ 0 % — not measured.
