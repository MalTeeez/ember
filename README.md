# Ember

A small, text-only desktop widget for Windows. It stays quiet and shows only relevant information: CPU, GPU, memory, disk, network and Claude usage limits, in a compact btop-like style. It also has separate markdown sticky notes.

```
+  ▤                          ×      ← toolbar: new note, minimized notes, hide (drag here to move)
cpu   83° ⣤⣤⣤⣤⣤⣤⣤⣤⣤⣤  25% 3.6GHz     ← temperature, load graph, load, clock
 c0  ⣠⣤⣤  24% 79° c3  ⣶⣾⣿  89% 63°    ← hot cores, aligned right (1, 2 or 3 columns)
  └ game.exe          27% i ▣ ✕      ← details, file properties, end process

── net ─────────────────────────
↓  29M ⣴⣾⣷⣶⣤⣀⣤⣴⣾⣿⣷⣦⣄⣀⣠ 3.0k pkt     ← rate, graph, packets per second
↑ 6.6M ⣶⣶⣦⣤⣤⣾⣷⣄⣠⣾⣿⣦⣠⣴⣶ 3.2k pkt

── gpu ─────────────────────────
4090  77%  85° ⣶⣿⣷⣦⣄⣤⣶⣿⣷⣦⣄⣤⣶⣿⣷⣦⣄

── claude ──────────────────────
5h      :::::::::::::::::::  42%
 ↻  52m                ◷  will last
week    :::::::::::::::::::  71%
 ↻  2d22h      ⚠  empty 1d22h early
```

(Icons are drawn with Segoe Fluent Icons / Segoe MDL2 Assets; above they are shown as text. Colours come from the theme.)

- Spec: [docs/requirements.md](docs/requirements.md)
- Choices where the spec is silent: [docs/decisions.md](docs/decisions.md)

## Requirements

- Windows 10 or 11, x64.
- Admin rights once, for the install (scheduled task) and for CPU temperatures (LibreHardwareMonitor kernel driver).
- For Claude usage: Claude Code installed and logged in (`%USERPROFILE%\.claude\.credentials.json`).
- For Codex usage: the Codex CLI logged in with a ChatGPT plan (`%USERPROFILE%\.codex\auth.json`, or `%CODEX_HOME%`).
- For Copilot usage: a Copilot editor plugin that keeps `%LOCALAPPDATA%\github-copilot\apps.json` (JetBrains, Vim/Neovim, Visual Studio), or the GitHub CLI logged in (`gh auth login`). The VS Code extension keeps its token in VS Code's own store, which Ember cannot read; use `gh` then.

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
- **CPU line and hot cores:** the cpu line shows temperature (when high), graph, load and clock (with Hz). Hot cores are indented and aligned right; each shows graph and load, plus temperature (1 and 2 columns) and clock (1 column) when known. On AMD (no per-core sensor) a core shows the temperature of its CCD. A core shows when it is a load outlier, above the hot-core load (85 %), or unusually hot (10 °C above the median core, or above the CPU temperature warning). Up to 3 in one column (clock, temperature, graph), up to 6 in two (graph, temperature), up to 9 in three columns (graph; the label colour shows the temperature). More cores: the 9 with the highest score (load plus temperature) show, and one line says how many are hidden and their total use (one full core = 100 %).
- **Disk and memory lines:** `C:  busy ⣀⣠⣴⣶⣿  98%` with `└ queued: 3 reads, 1 write` under it (drive letter, up to 3 letters for a disk with more partitions, `disk2` without a letter; queued = average read/write requests waiting, rounded), `C:  free  14GB / 500GB ▮▯▯  3%`. Memory lines have no "ram" label: `used    29GB / 32GB ▮▮▮▮▮▯▯  90%`, `commit  35GB / 40GB ▮▮▮▮▮▯▯  87%`, `hard page faults ⣀⣠⣴ 983/s` (pages read from disk), and under it each process with hard page faults: private memory (colour = share of the RAM, full at 25 %) and hard faults/s (colour = share of all hard faults). Up to 5 processes; click one for details.
- **Usage limits** (Claude, Codex, Copilot): 2 lines each: bar and %, then the time to the reset (↻) and the projection (will last / empty … before reset, or "early" when the long text does not fit). All bars have the same length; the label column grows for long labels (week, opus, sonnet, premium). When a source gives no data, its section hides (no empty space) and stays hidden for at least 1 min.
- **Tray icon** (amber dot): Show widget, New note, restore minimized notes, Edit layout, Show everything, Settings, Open log, Exit.
- **Edit layout:** the widget gets a dark background and can be dragged anywhere. The position is saved as an offset from a corner of the monitor (Settings → General → Corner).
- **Show everything** (tray or Settings → General → Debug): every enabled line shows at once, thresholds and delays are ignored, and missing data gets sample values. Use it to choose a theme and layout.
- **Settings** (tray, or right-click the top row): pages General, Layout, Appearance, Thresholds, Intervals, Network, Notes.
  - **Layout:** the list of lines. Move lines up/down, switch them on/off (double-click), add headers and spacers (blank, rule, dots, dashed, double, or your own text). Load a built-in layout (default, compact, everything) or import/export JSON.
  - **Appearance:** theme preset (ember, btop, mono, nord, dusk, glass, gruvbox, dracula, tokyo-night, catppuccin, latte, solarized, everforest, rose-pine, kanagawa, monokai, one-dark, matrix, synthwave, paper), background (none, solid, gradient, acrylic, blur, panel, glass), colours (colour picker), opacity, corner radius, padding, border, text shadow, header style (rule, spaced, plain, bracket, dots, fade), header alignment (left, center, right), indent and icons, row backgrounds (stripes, header bands, process bands, graph and bar tracks, chips behind warning values, strength), bar style (colons, squares, pips, braille, line, shade, blocks), graph colours (intensity like btop, category, mono). Import/export the theme as JSON.
  - **General → Size:** font size slider and text opacity.
  - Changes apply at once. File: `%APPDATA%\Ember\settings.json` (General → Open settings file). Example presets are in [presets/](presets/).
- **Notes:** tray → New note, toolbar **+**, or the hotkey (default **Ctrl+Alt+N**). Drag the top bar to move, an edge to resize. The top bar has the note colour and no title; only its buttons show it: minimize (to the widget toolbar) and delete (no confirmation). Note opacity: Settings → Notes (separate from the widget). A `>` quote is italic; `>>` and deeper are also indented. Markdown is styled as you type, markers stay dim: `#`/`##`/`###` headings (larger text), `**bold**`, `*italic*`, `~~strike~~`, `` `code` ``, fenced code blocks (```` ``` ```` or `~~~`), `>` and `>>` quotes (indented), `-` / `1.` lists, `- [ ]` checkboxes (click to tick), links `[text](url)` and bare URLs (Ctrl+click opens), `---` rules. Saved 1 s after each change to `%APPDATA%\Ember\notes\<id>.md`.
- **Debug log:** Settings → General → Debug log. Open it with tray → **Open log** or Settings → General → **Open log** (also: Open settings file, Open data folder). File: `%APPDATA%\Ember\ember.log`.
- **Second start** of `Ember.exe` brings the running widget and notes to the front.
- **Idle:** sampling stops while the session is locked or the display is off.

### Codex and Copilot usage

- **Codex** (`── codex`): the 5 h and weekly plan windows, from the same data as `/status` in the Codex CLI (`https://chatgpt.com/backend-api/wham/usage`). Same lines and projection as Claude. Every 1 min.
- **Copilot** (`── copilot`): premium requests this month (`prem`) with time to the monthly reset and a projection; on the Free plan also the chat and completions quotas. From `https://api.github.com/copilot_internal/user`, the data the Copilot editor status uses. Every 5 min. Unlimited quotas are not shown.
- Both endpoints are not documented and can change. Tokens are only read, never refreshed. Any failure hides the lines; HTTP 429 keeps the last values and adds 30 s to the interval (up to 5 min).

### Device batteries

`── devices`: one line per device with a battery: name, bar and %. Red below 15 %, amber below 30 % (Settings → Thresholds). Settings → Layout → "Device batteries always" off shows them only when low. Every 60 s.

- **Bluetooth** (classic and LE): the battery level that Windows itself shows in Settings → Bluetooth & devices. Devices that do not report a level to Windows (many mice with their own software) do not show.
- **Razer HyperSpeed / wireless receivers** (for example the DeathAdder V3 HyperSpeed, receiver 1532:00C5): Ember asks the receiver with the Razer protocol (the same feature report as Razer Synapse and OpenRazer). When the mouse sleeps or is off, the receiver gives no level and the line hides. Other wireless mice (Logitech, SteelSeries, …) use their own protocols and are not supported.

### Claude usage

Ember reads the OAuth token of Claude Code and calls the same endpoint as `/usage` in Claude Code (`https://api.anthropic.com/api/oauth/usage`). This endpoint is not documented and can change. Ember never refreshes the token: when the token has expired, the Claude lines hide until Claude Code refreshes it. Every 1 min (also the minimum); on HTTP 429 the last values stay and the interval grows by 30 s per 429, up to 5 min, until a request succeeds.

### Antivirus

LibreHardwareMonitor loads a kernel driver to read CPU temperatures. Some antivirus tools flag it. Without the driver (or without admin rights), temperatures stay hidden; everything else works.

## Not tested

This version was written and built on Linux. No Windows machine was available. The unit tests (Core logic: layout, styles, formats, grid, markdown, settings) pass on Linux and on the GitHub Actions Windows runner, and the full app compiles and publishes for win-x64 there. Nothing below has been run on Windows:

1. **Start and window:** the widget appears, sits on the desktop layer (not topmost), stays visible on Win+D, has no taskbar button.
2. **Backgrounds:** acrylic, blur and glass (system blur through `SetWindowCompositionAttribute` on a layered window, rounded region), gradient, panel, solid, none; border and text shadow; behaviour on Windows 10 vs 11. The fix for the square shadow behind rounded corners (DWM non-client rendering off, DWM corner preference) and the acrylic opacity (colour layer over the blur) are not checked.
3. **Text drawing:** row backgrounds (bands, tracks, chips), header icons, smaller icons with a space after them, the 20 themes; sharp GDI+ text, bold runs, 32-column alignment, braille and bar glyphs in Cascadia Mono and Consolas (fallback font), icon font glyphs (Segoe Fluent Icons / MDL2) centred in their 2 cells and drawn as stroked paths (thicker lines).
4. **Mouse:** click-through everywhere except buttons, process lines and the top row; hover highlight and instant tooltips (none while dragging); drag by the top row; the near-invisible hit background (alpha 3) actually catches clicks.
5. **Animation:** smooth height changes, also with bottom corners (the widget grows upward), CPU use while animating.
6. **Layout stability:** kept space after items hide, sticky top processes, hot-core grid with 1/2/3 columns and the hidden-cores line on a CPU with many cores.
7. **DPI:** 100–200 %, moving between monitors with different DPI.
8. **Sensors:** PDH counter names and values (`% Processor Utility`, `% Processor Performance`, `Processor Frequency` per core, GPU Engine, GPU Adapter Memory, PhysicalDisk, Pages Input/sec); hybrid CPU core groups; packet counters of GetIfTable2; AMD CCD temperatures from LHM; per-core temperature mapping from LHM ("CPU Core #n", "P-Core #n"/"E-Core #n") to logical cores; GetIfTable2 adapter filter; DXGI adapter list and model labels (the fix for a GPU listed twice, which gave "4091", assumes both entries have the same hardware IDs); LibreHardwareMonitor temperatures (Intel and AMD).
9. **Process actions:** details window (look, buttons, Copy, selecting and copying values), file properties dialog, end process (confirmation), end tree, open location, with and without admin rights.
10. **Claude, Codex and Copilot usage:** real requests with real tokens (Claude credentials, Codex auth.json with the ChatGPT-Account-Id header, Copilot token from apps.json or `gh auth token`), expired tokens, HTTP 401/403/429, projections on live data, Copilot monthly reset date.
11. **Device batteries:** Bluetooth battery levels through the device property (which devices report one), stale values of disconnected devices, Razer DeathAdder V3 HyperSpeed receiver (transaction id, answer while the mouse sleeps, charging flag, conflicts with Razer Synapse running at the same time).
12. **Network quality** pings to the gateway and 1.1.1.1.
13. **Notes:** title bar and buttons, minimize to the toolbar and restore, delete, resize, RichEdit styling (heading sizes, quote indents, code background, strike, links, Ctrl+click), undo not affected by styling (TOM `Undo(tomSuspend)`), saving and loading.
14. **Settings window:** page list, scrolling (no tearing), sliders, preset lists that keep the chosen name, Open log / settings / folder buttons, colour picker, layout editor, theme/layout import and export (file dialogs), Tab navigation, start-at-logon toggle with UAC.
15. **install.ps1 / uninstall.ps1** and the scheduled task.
16. **Robustness:** Explorer restart, lock/unlock, display off/on, single instance against an elevated instance, sleep/resume.
17. **Resource targets:** RAM < 60 MB, CPU < 1 %, GPU ≈ 0 % — not measured.
