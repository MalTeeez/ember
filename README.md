# Ember

A small, text-only desktop widget for Windows. It stays quiet and shows only relevant information: CPU, GPU, memory, disk, network and Claude usage limits, in a compact btop-like style. It also has separate markdown sticky notes.

```
+  ▤                          ×      ← toolbar: new note, minimized notes, hide (drag here to move)
cpu   25% 3.6G  83° ⣤⣤⣤⣤⣤⣤⣤⣤⣤⣤⣤⣤
c0  ⣠⣤⣤ 24% 79° c3  ⣶⣾⣿ 89% 63°      ← hot cores (1, 2 or 3 columns)
  └ game.exe          27% i ▣ ✕      ← details, file properties, end process
↓     29M   pk 106M ⣶⣤⣀⣤⣴⣾⣿⣷⣦⣄⣀⣠
↑    6.6M   pk 7.9M ⣤⣤⣾⣷⣄⣠⣾⣿⣦⣠⣴⣶

── gpu ─────────────────────────
4090  77%       85° ⣦⣄⣤⣶⣿⣷⣦⣄⣤⣶⣿⣷

── claude ──────────────────────
5h   ::::::::::::::::::::::  42%
  ↻ refresh ················ 52m
  ◷ will last
wk   ::::::::::::::::::::::  71%
  ↻ refresh ·············· 2d22h
  ⚠ empty 1d22h before reset
```

(Icons are drawn with Segoe Fluent Icons / Segoe MDL2 Assets; above they are shown as text. Colours come from the theme.)

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

- **Widget:** on the desktop layer (under normal windows). Mouse clicks go through it, except on buttons, process lines and the top row.
  - Always visible: CPU load, network down/up, Claude 5h and weekly limits.
  - Other items show only when a value crosses its threshold for 5 s, and hide after 10 s of normal values.
  - When an item gets smaller, its space stays for 60 s (Settings → General → Motion), so lines below do not jump. A top process stays at least 15 s unless it ends. Size changes are animated.
- **Top row:** drag it to move the widget. Right-click it for Settings / New note / Show everything.
- **Toolbar** (first line by default): **+** new note, one icon per minimized note (click to restore), **×** hide the widget (tray → Show widget brings it back). Tooltips show at once.
- **Process lines** (`└ name … 27% i ▣ ✕`): **i** details window, **▣** file properties, **✕** end process (asks first). Click the line for details; right-click for more (end process tree, open file location).
- **Hot cores:** a core shows when it is a load outlier, above the hot-core load (85 %), or unusually hot (10 °C above the median core, or above the CPU temperature warning). Up to 3 in one column (load, clock, temperature, graph), up to 6 in two, up to 9 in three columns. More cores: the 9 with the highest score (load plus temperature) show, and one line says how many are hidden and their total use (one full core = 100 %).
- **Tray icon** (amber dot): Show widget, New note, restore minimized notes, Edit layout, Show everything, Settings, Exit.
- **Edit layout:** the widget gets a dark background and can be dragged anywhere. The position is saved as an offset from a corner of the monitor (Settings → General → Corner).
- **Show everything** (tray or Settings → General → Debug): every enabled line shows at once, thresholds and delays are ignored, and missing data gets sample values. Use it to choose a theme and layout.
- **Settings** (tray, or right-click the top row): pages General, Layout, Appearance, Thresholds, Intervals, Network, Notes.
  - **Layout:** the list of lines. Move lines up/down, switch them on/off (double-click), add headers and spacers (blank, rule, dots, dashed, double, or your own text). Load a built-in layout (default, compact, everything) or import/export JSON.
  - **Appearance:** theme preset (ember, btop, mono, nord, dusk, glass), background (none, solid, gradient, acrylic, blur, panel, glass), colours (colour picker), opacity, corner radius, padding, border, text shadow, header style (rule, spaced, plain, bracket, dots, fade), bar style (colons, squares, pips, braille, line, shade, blocks), graph colours (intensity like btop, category, mono). Import/export the theme as JSON.
  - **General → Size:** font size slider and text opacity.
  - Changes apply at once. File: `%APPDATA%\Ember\settings.json`. Example presets are in [presets/](presets/).
- **Notes:** tray → New note, toolbar **+**, or the hotkey (default **Ctrl+Alt+N**). Drag the title bar to move, an edge to resize. Title bar buttons: minimize (to the widget toolbar) and delete (no confirmation). Markdown is styled as you type, markers stay dim: `#`/`##`/`###` headings (larger text), `**bold**`, `*italic*`, `~~strike~~`, `` `code` ``, fenced code blocks (```` ``` ```` or `~~~`), `>` and `>>` quotes (indented), `-` / `1.` lists, `- [ ]` checkboxes (click to tick), links `[text](url)` and bare URLs (Ctrl+click opens), `---` rules. Saved 1 s after each change to `%APPDATA%\Ember\notes\<id>.md`.
- **Debug log:** Settings → General → Debug log. File: `%APPDATA%\Ember\ember.log`.
- **Second start** of `Ember.exe` brings the running widget and notes to the front.
- **Idle:** sampling stops while the session is locked or the display is off.

### Claude usage

Ember reads the OAuth token of Claude Code and calls the same endpoint as `/usage` in Claude Code (`https://api.anthropic.com/api/oauth/usage`). This endpoint is not documented and can change. Ember never refreshes the token: when the token has expired, the Claude lines hide until Claude Code refreshes it. Every 1 min by default; on HTTP 429 the interval doubles until a request succeeds.

### Antivirus

LibreHardwareMonitor loads a kernel driver to read CPU temperatures. Some antivirus tools flag it. Without the driver (or without admin rights), temperatures stay hidden; everything else works.

## Not tested

This version was written and built on Linux. No Windows machine was available. The unit tests (Core logic: layout, styles, formats, grid, markdown, settings) pass on Linux and on the GitHub Actions Windows runner, and the full app compiles and publishes for win-x64 there. Nothing below has been run on Windows:

1. **Start and window:** the widget appears, sits on the desktop layer (not topmost), stays visible on Win+D, has no taskbar button.
2. **Backgrounds:** acrylic, blur and glass (system blur through `SetWindowCompositionAttribute` on a layered window, rounded region), gradient, panel, solid, none; border and text shadow; behaviour on Windows 10 vs 11.
3. **Text drawing:** sharp GDI+ text, bold runs, 32-column alignment, braille and bar glyphs in Cascadia Mono and Consolas (fallback font), icon font glyphs (Segoe Fluent Icons / MDL2) centred in their 2 cells.
4. **Mouse:** click-through everywhere except buttons, process lines and the top row; hover highlight and instant tooltips; drag by the top row; the near-invisible hit background (alpha 3) actually catches clicks.
5. **Animation:** smooth height changes, also with bottom corners (the widget grows upward), CPU use while animating.
6. **Layout stability:** kept space after items hide, sticky top processes, hot-core grid with 1/2/3 columns and the hidden-cores line on a CPU with many cores.
7. **DPI:** 100–200 %, moving between monitors with different DPI.
8. **Sensors:** PDH counter names and values (`% Processor Utility`, `% Processor Performance`, `Processor Frequency` per core, GPU Engine, GPU Adapter Memory, PhysicalDisk, Pages Input/sec); hybrid CPU core groups; per-core temperature mapping from LHM ("CPU Core #n", "P-Core #n"/"E-Core #n") to logical cores; GetIfTable2 adapter filter; DXGI adapter list and model labels; LibreHardwareMonitor temperatures (Intel and AMD).
9. **Process actions:** details window (look, buttons, Copy), file properties dialog, end process (confirmation), end tree, open location, with and without admin rights.
10. **Claude usage:** real request with a real token, expired token, HTTP 401/429, projection on live data.
11. **Network quality** pings to the gateway and 1.1.1.1.
12. **Notes:** title bar and buttons, minimize to the toolbar and restore, delete, resize, RichEdit styling (heading sizes, quote indents, code background, strike, links, Ctrl+click), undo not affected by styling (TOM `Undo(tomSuspend)`), saving and loading.
13. **Settings window:** page list, scrolling, sliders, colour picker, layout editor, theme/layout import and export (file dialogs), Tab navigation, start-at-logon toggle with UAC.
14. **install.ps1 / uninstall.ps1** and the scheduled task.
15. **Robustness:** Explorer restart, lock/unlock, display off/on, single instance against an elevated instance, sleep/resume.
16. **Resource targets:** RAM < 60 MB, CPU < 1 %, GPU ≈ 0 % — not measured.
