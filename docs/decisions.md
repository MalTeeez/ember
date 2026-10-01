# Ember — Decisions

This file lists choices where [requirements.md](requirements.md) is silent, and the reason for each.
Rule: when the spec is silent, choose the simplest option.

## Build and structure

| # | Decision | Reason |
| --- | --- | --- |
| B1 | Plain Win32 via P/Invoke. No WinForms. | Lower RAM. The project targets `net10.0` (not `net10.0-windows`), so it compiles on Linux without the Windows Desktop targeting pack. |
| B2 | Three projects: `src/Ember` (app, Win32), `src/Ember.Core` (pure logic, no Windows APIs), `tests/Ember.Tests` (xUnit, references Core only). | Tests run on Linux without the app or LibreHardwareMonitorLib. |
| B3 | `EnableWindowsTargeting` is not set. | It is only necessary for `-windows` target frameworks. With it, restore also needs the Windows Desktop reference pack. |
| B4 | Publish = self-contained, single file, not trimmed, not compressed. | "One executable" that runs without an installed .NET runtime. No trimming because LibreHardwareMonitorLib uses reflection (WMI). No compression because it increases RAM at start. |
| B5 | Build property `EmberOffline=true` builds without LibreHardwareMonitorLib (no temperatures). | The development machine had no access to nuget.org. CI builds the full version. |
| B6 | The GitHub Actions workflow was added in the first commit, not at the end. | It was the only way to restore NuGet packages and build/test on Windows during development. |
| B7 | LibreHardwareMonitorLib pinned to 0.9.4. | Repeatable builds. Newer versions can change the kernel driver; test before an update. |

## Claude usage

| # | Decision | Reason |
| --- | --- | --- |
| C1 | Endpoint `GET https://api.anthropic.com/api/oauth/usage` with `Authorization: Bearer <claudeAiOauth.accessToken>` and `anthropic-beta: oauth-2025-04-20`. Response fields `five_hour`, `seven_day`, `seven_day_<model>` with `utilization` (0–100) and `resets_at` (ISO 8601). | Same as Claude Code `/usage`, from open-source usage monitors (for example github.com/wakamex/ccusage) and anthropics/claude-code issue #31021. Not documented; it can change. |
| C2 | `User-Agent: Ember/<version>`. | Honest client name. |
| C3 | The credentials file is read again before each poll. `expiresAt` (ms) in the past = hide, no request. | Claude Code refreshes the file itself. Ember never refreshes. |
| C4 | HTTP 429 also hides the lines (the spec says a failed request hides them). The interval doubles up to a maximum of 1 h. | Spec: "double the interval until a request succeeds". A maximum stops a stuck widget. |
| C5 | Per-model lines: every `seven_day_<x>` object except `seven_day_oauth_apps`. Labels (3 columns): `op` (opus), `son` (sonnet), `hai` (haiku), else the first 3 letters. Same two-line layout as `wk`, with the window-average rate. | Simplest consistent layout. |
| C6 | Projection colour: severity of (time to reset ÷ time to limit), warning 1, critical 2. The spec example (limit in 1 h, reset in 4 h → ratio 4) is red. | The spec gives no numbers for this gradient. |
| C7 | The 5h rate uses the samples from 30 min ago to now. A new window (reset time moves by more than 5 min) or a drop in usage clears the history. | Spec: "last 30 min of the widget's own samples". |
| C8 | Usage bars are 20 cells wide and monochrome. The "lasts" / "limit 1h00" / "—" text is right-aligned on the second line. | Matches the spec example (42 % = 8⅜ cells). The spec gives no threshold for used %. |

## GPU

| # | Decision | Reason |
| --- | --- | --- |
| G1 | GPU list from DXGI (`EnumAdapters1`), without software adapters and the Microsoft Basic Render Driver. Read at start and every 10 min. | Gives name, LUID (to match PDH instance names) and VRAM total. |
| G2 | Integrated GPU = less than 512 MiB dedicated video memory. | DXGI has no "integrated" flag. iGPUs report 0–512 MiB dedicated memory. |
| G3 | Adapter load = for each engine, the sum over processes; then the busiest engine. Top process = highest load of one process on one engine of that adapter. | Task Manager method (spec). |
| G4 | LHM GPU temperature: sensor "GPU Core" (else the first temperature sensor), matched to the DXGI adapter by name. If there is exactly one LHM GPU temperature and one discrete adapter, they are matched. | LHM does not expose the LUID. |
| G5 | Short GPU label: `rtx`, `gtx`, `qdr`, `rx`, `arc`, `igp`, else `gpu`; a number is added when two GPUs have the same label. | Fits the 4-column label (spec example `rtx`). |
| G6 | The GPU line shows when load, temperature, VRAM or top process is visible; load/temperature fields are blank when their item is not visible. VRAM is a separate line `vram 7.8/8.0 GB 97%`. | One block for each GPU (spec). |
| G7 | GPU load and CPU load have no colour gradient (monochrome graphs). | The spec gives them a visibility threshold only, not warning/critical values. |
| G8 | PDH "GPU Engine" and "GPU Adapter Memory" counters are added only while a GPU item that needs them is on. | The GPU Engine wildcard counter has many instances and costs CPU. |

## Memory, disk and processes

| # | Decision | Reason |
| --- | --- | --- |
| M1 | Critical values that the spec does not give: commit charge 95 %, hard page faults 5000/s, disk active 95 %, disk queue 5. | Settings need warning and critical values for each item. |
| M2 | Free space colour: gradient on % (warning 10, critical 5); below 20 GB free it is at least amber. | Spec: "< 10 % or < 20 GB (amber), < 5 % (red)". |
| M3 | Free space is read every 10 s, fixed drives only. | It changes slowly; removable drives can spin up. |
| M4 | Process data (CPU time, private bytes, I/O bytes) comes from one `NtQuerySystemInformation(SystemProcessInformation)` call. Top disk process = ReadTransferCount + WriteTransferCount per second. | This is the same kernel data as PDH `Process\IO Data Bytes/sec`, but with PIDs and at much lower CPU cost than the PDH Process object. The value is approximate, as the spec says. |
| M5 | Processes are scanned only while a top-process item is visible or waiting for its show delay. | CPU target < 1 %. |
| M6 | Top CPU process % = share of all logical cores (Task Manager style, 100 % = whole CPU). | Same scale as the combined load. |
| M7 | Disk label `d0`, `d1` … from the PDH PhysicalDisk instance number, followed by its volumes. | Short, fits the 4-column label. |

## Widget interaction

| # | Decision | Reason |
| --- | --- | --- |
| U1 | Desktop layer = the widget window is owned by the desktop window `Progman` and placed at the bottom of the z-order. | Not topmost, stays visible on Win+D, and needs no injection into Explorer. On `TaskbarCreated` the owner is set again. |
| U2 | Click-through uses `WS_EX_TRANSPARENT`. While top-process lines are visible, a 100 ms timer checks the cursor and removes the flag only while the cursor is over such a line. Without top-process lines there is no timer. | A layered window cannot be click-through and clickable in parts at the same time. |
| U3 | "Edit mode" = tray menu → Edit layout. The widget gets a dark background, can be dragged, and a right-click opens a menu (Settings, New note, Done). After a drag, the offset from the selected corner and the monitor are saved. | The spec names edit mode but does not define it. Right-click on the widget for Settings works in edit mode, because the widget is click-through otherwise. |
| U4 | Position = corner (default top right) + offset in DIP from the monitor work area. The widget grows away from its corner. | Stays in place when lines appear or hide, and when the resolution or DPI changes. |
| U5 | Process details popup: a normal small window with read-only text. CPU and disk are measured over 250 ms when it opens; GPU is the last sampled value (only when a GPU item is on). It closes when it loses focus. | Simplest. |
| U6 | "End process" asks for confirmation; "End process tree" does not (as in the spec). The tree ends children first. | Spec. |
| U7 | Edit-mode drag and DPI: per-monitor DPI v2; fonts are created for the DPI of the target monitor. | Spec: sharp text on all monitors. |

## Settings

| # | Decision | Reason |
| --- | --- | --- |
| S1 | The settings window is one scrolling list generated from `SettingsSchema` (Core): sections Items, Thresholds, Intervals, Network, Layout, Other. | One place for labels, parsing and validation; testable on Linux. |
| S2 | A change applies when the value is valid (each key press for text fields). Invalid text is ignored until it is valid. No OK/Cancel buttons. | Spec: "Changes apply at once". |
| S3 | Show and hide delays (5 s / 10 s) are also settings. The internet ping host (default 1.1.1.1) is a setting. | Spec: "every threshold, interval … is in Settings". |
| S4 | Start at logon: the checkbox shows whether the scheduled task "Ember" exists (`schtasks /Query`, no admin). A change runs an elevated PowerShell (one UAC prompt) that registers or removes the task. The setting is not stored in settings.json. | Spec: "Only a change asks for admin rights". The task is the truth. |
| S5 | Scheduled task: at logon of the current user, highest privileges, no execution time limit, runs on battery, ignore a new instance. | The default task time limit (72 h) would stop the widget. |
| S6 | Settings JSON with comments and trailing commas is accepted. A broken file gives default settings (and is overwritten on the next change). Values are clamped (sensor interval 0.25–60 s, Claude 15–3600 s, opacity 10–100 %, font 6–32 pt). | Robustness. |
| S7 | Debug log: `%APPDATA%\Ember\ember.log`, cut at 1 MB (one `.old` file). Each failing source is logged once until it works again. | Spec: debug log, off by default. |

## Notes

| # | Decision | Reason |
| --- | --- | --- |
| N1 | Editor = RichEdit 4.1+ (`Msftedit.dll`, class `RICHEDIT50W`) inside a borderless layered window owned by the desktop window. | Built into Windows; supports per-range colour and bold/italic/strike-out. |
| N2 | Styling: the whole note is styled again after each change (notes are small). Headings bold; bold/italic as written; inline code with a slightly lighter background; markers and ticked-checkbox text dim (ticked text also struck out). | Spec: styled as you type, markers visible but dim, same colours as the widget. |
| N3 | Window: drag the top strip (14 px) to move; drag an edge to resize; right-click → New note / Delete note. Delete asks for confirmation only if the note is not empty. | The spec does not define move/resize. |
| N4 | Positions and sizes: `%APPDATA%\Ember\notes\notes.json` (screen pixels). Text: `<id>.md` with CRLF line ends. Id = creation time plus 4 random hex digits. | Spec: one `.md` file per note. A note whose position is off screen moves to the primary monitor. |
| N5 | Default hotkey for a new note: Ctrl+Alt+N. A hotkey that another program has is logged and ignored. | The spec gives no default. |
| N6 | Known limit: Ctrl+Z can also undo a styling step. | RichEdit records formatting in its undo stack. |

## Network

| # | Decision | Reason |
| --- | --- | --- |
| P1 | Physical adapter = `ConnectorPresent`, not a filter row (`FilterInterface`), not loopback/tunnel/PPP type, `TunnelType` none, and no excluded word in description or alias (case-insensitive). | Spec rules, plus the WFP/QoS filter rows that GetIfTable2 also returns for each physical adapter. |
| P2 | When more than one adapter has traffic (≥ 1 KB/s), one dim line per adapter shows below the totals: `└ name ↓61M ↑2.1M`. Uses the show/hide delay. | Spec: "Show the adapter name only when more than one physical adapter has traffic". The totals stay as they are. |
| P3 | Down/up value and peak use the unit of the peak (kbps, Mbps, Gbps; bits, base 1000). Graph scale = peak of the last 24 s. | Keeps the peak at 1–999 so `pk 61` fits; graph and numbers have the same scale. |
| P4 | Ping: gateway = IPv4 default gateway of the first active non-tunnel interface, read every minute; internet host is a setting (default 1.1.1.1); timeout 1 s. Loss line says `local` (loss at the gateway) or `isp` (loss only to the internet). Latency lines `ping … lan` / `ping … isp`. | Spec. |

## Layout and style

| # | Decision | Reason |
| --- | --- | --- |
| L1 | Column grid (32 columns): label 0–2, value 3–7 (right aligned), unit or temperature 8–12, space, graph 14–25 (12 braille = 24 samples), peak 26–31. Lines without a graph (memory, disk) put the value at column 5 and right-align the percentage. | Aligns all graphs; close to the spec example. |
| L2 | Braille level: 0 = one bottom dot (baseline), then 2–4 dots at ⅓, ⅔ and full scale (rounded). Missing history at the left is a blank braille cell. The colour of a graph cell is the gradient of its larger sample. | btop look; spec: 2 samples × 4 levels. |
| L3 | Colours: bright #E8E8E8, dim #808080, amber #FFB000, red #FF453A. The gradient starts one (critical − warning) span below the warning value. | The spec says "smoothly from normal to amber at the warning threshold" without a start point. |
| L4 | A blank line goes before each section header. | As in the spec example. |
| L5 | Font: setting "Cascadia Mono" (default), then Consolas, then Courier New. Characters that the font does not have (checked with `GetGlyphIndices`) are drawn with Segoe UI Symbol in their cell. Anti-aliased grayscale text (ClearType cannot be used on a transparent background). | Windows 10 has no Cascadia Mono and Consolas has no braille. |
| L6 | Opacity applies to the whole widget (and to notes) through the layered-window constant alpha. In edit mode the widget has a 75 % dark background. | Simplest. |

## Version 2: styles, layout, interaction

These decisions replace older ones where they conflict (noted in brackets).

| # | Decision | Reason |
| --- | --- | --- |
| V1 | All colours, header/bar/graph styles and the background are in a **theme** (`Theme` in settings.json). 6 built-in themes; any theme can be exported/imported as JSON (`presets/themes/`). [replaces L3, L6] | Easy variations of the text styling. |
| V2 | Lines are ordered and switched on/off by **layout entries** (`Entries` in settings.json): items, headers, spacers (blank, rule, dots, dashed, double, custom text). 3 built-in layouts; export/import as JSON (`presets/layouts/`). Unknown items in a file are dropped; missing items are added at the end, switched off. Optional fields inside lines (temperatures, clocks, projection line, bytes) are separate switches. [replaces the per-item switches] | Per-line reordering fused with the item switches. |
| V3 | Numbers use 4-character fields: 3 for the value, 1 for the unit letter (`23%`, `61M`, `7.8G`, `4.8G`, `71°`). A label is always followed by a space. Network values have no "bps" text (bits by default, bytes as an option). [replaces L1, P3] | Requested. |
| V4 | **Kept height:** when an entry gets fewer lines, its height stays (empty lines) until it was not needed for `HoldSeconds` (default 60 s). Headers/spacers stay while their group keeps space. | No jumping lines and no constant resizing. |
| V5 | **Sticky top process:** a new top process replaces the shown one only after it was on top for `ProcessStickSeconds` (default 15 s), or at once when the shown process is gone. The sampler gives the top 5 so the shown process keeps its own current value. | Less flicker of process names. |
| V6 | **Hot cores:** outlier above the median (an idle core among busy ones is not "hot"), or smoothed load ≥ 85 %, or temperature ≥ the CPU warning value or ≥ 10 °C above the median core. Grid: 1 column up to 3 cores, 2 up to 6, 3 up to 9 (max 3 rows, filled column by column). Choice by score = load + 3 × (°C above median − 3) + 30 × temperature severity. Over 9: "n hot cores hidden, at y%" (sum of loads, one full core = 100 %). The core label colour shows its temperature. | Requested; temperature matters even at moderate load. |
| V7 | Per-core clock = PDH `Processor Frequency` × `% Processor Performance` / 100 for each instance. Per-core temperature: LHM core sensors mapped to logical cores through the CPU-set core index (both threads of a core get the same value). AMD: none (LHM has no per-core values). | No driver-specific clock API needed. |
| V8 | **Backgrounds:** none, solid, gradient (two colours, angle), acrylic (system acrylic, tinted), blur (system blur + tint), panel (solid + border), glass (blur + top highlight). The system blur uses `SetWindowCompositionAttribute` (accent policy, undocumented but used by Rainmeter/TranslucentTB) and a rounded window region. Background opacity and text opacity are separate. | Readability on any wallpaper; the requested "half dark", acrylic and blur. |
| V9 | **Animation:** only the height animates (ease-out cubic, default 160 ms, 15 ms frames); width changes (font size) jump. Lines are clipped during the animation. | Fast and simple. |
| V10 | **Graph colours:** "intensity" (default) colours each braille cell by its value along the theme's colour stops (btop style); "category" uses the item accent; "mono" the text colour. Usage bars: filled cells in the Claude accent (or along the stops), empty cells dim. [replaces G7, C8] | Requested. |
| V11 | **Icons:** Segoe Fluent Icons (Windows 11), else Segoe MDL2 Assets; an icon takes 2 cells and is centred in them. Fallback glyphs are also centred vertically. | Fixes the off-centre ↻. |
| V12 | **Claude:** 3 lines per limit: bar + %, `↻ refresh ···· 52m`, and `◷ will last` / `⚠ empty 1d22h before reset` (time between reaching the limit and the reset) / `too early to project`. Durations: `52m`, `3h05m`, `1d22h`. [replaces C8] | Requested wording. |
| V13 | **GPU label** = model number (`4090`, `A770`, `7900`, `780M`); `igpu`/`gpu` without one; equal labels get a number. [replaces G5] | Shows what differs between GPUs. |
| V14 | **Top row** of the widget is a drag handle (moves the widget, position saved) and right-click opens the widget menu. **Toolbar** entry (default first): new note, one icon per minimized note, hide widget. | Requested; also gives a right-click way to Settings. [extends U2, U3] |
| V15 | **Hover:** a 100 ms cursor check runs while the widget has hits (with the toolbar: always). It switches click-through, highlights the hovered button and shows an instant tooltip (own layered window, no delay). | Requested instant tooltips. |
| V16 | **Process details window:** custom painted in theme colours, buttons Open location, Properties (Explorer properties of the executable), Copy, End process (asks), End tree. | Requested better look and actions. |
| V17 | **Settings window:** page list (General, Layout, Appearance, Thresholds, Intervals, Network, Notes); sliders for sizes/opacity; colour fields show their colour and open the Windows colour picker; layout editor list with buttons. [replaces S1] | Grouped options. |
| V18 | **Notes:** title bar (slightly lighter than the note) with the first line as title, minimize and delete buttons. Delete has no confirmation (requested). Minimized notes are hidden and listed in the widget toolbar and the tray menu; the state is in notes.json. Note opacity is at least 75 % for readability; the system blur is not used for notes (RichEdit draws an opaque background). Ctrl+click opens a link (plain click places the caret). Formatting is kept out of the undo stack with TOM `Undo(tomSuspend)`. [replaces N3, N6] | Requested; links stay editable. |
| V19 | Markdown: `#` 1.6×, `##` 1.35×, `###` 1.15× text size; quotes indent 14 pt per level and are italic; fenced code (``` or ~~~) and inline code get a lighter background; `~~strike~~`; `[text](url)` and bare http(s) URLs underlined in the link colour; `---`/`***`/`___` rules dim. | Common basic markdown. |

## Version 3: Codex, Copilot, device batteries

| # | Decision | Reason |
| --- | --- | --- |
| X1 | **Codex:** `GET https://chatgpt.com/backend-api/wham/usage`, `Authorization: Bearer <tokens.access_token>`, `ChatGPT-Account-Id: <tokens.account_id>` (else the `chatgpt_account_id` claim of the token) from `%CODEX_HOME%\auth.json` (default `%USERPROFILE%\.codex\auth.json`). `rate_limit.primary_window` / `secondary_window` with `used_percent`, `limit_window_seconds`, `reset_at` (else `reset_after_seconds`). A token whose `exp` claim is past is not sent; it is never refreshed. | Same data as Codex CLI `/status`, from open-source usage monitors (for example github.com/ajhochy/Rhythm issue #1568). Not documented. |
| X2 | **Copilot:** `GET https://api.github.com/copilot_internal/user`, `Authorization: token <GitHub OAuth token>`, `X-GitHub-Api-Version: 2025-04-01`. Token: first `oauth_token` for github.com in `github-copilot\apps.json` / `hosts.json` (under `%LOCALAPPDATA%` and `%USERPROFILE%\.config`), else `gh auth token` (asked at most every 30 min). Used % = 100 − `percent_remaining` (else from `entitlement`/`remaining`); reset = `quota_reset_date(_utc)`; window = one month. Unlimited quotas are hidden. Poll every 5 min. | The endpoint only accepts tokens of GitHub's own OAuth apps (Copilot plugins, GitHub CLI). Quotas change slowly. |
| X3 | Claude, Codex and Copilot share one poller design (`UsagePoller`) and one tracker (`UsageTracker`): short window (recent-rate projection when it is 5 h), long window, extra windows. Copilot extra windows (chat, completions) show always; Claude per-model windows only when higher than the weekly one. | Same rules for all providers. |
| X4 | **Bluetooth batteries:** device property `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2` (DEVPKEY_Bluetooth_Battery) of present devices under the BTHENUM, BTHLE and BTHLEDEVICE enumerators, through SetupAPI (no WinRT). One line per device name (lowest value if a device reports through several services). | The value Windows Settings shows; no driver or pairing access needed. |
| X5 | **Razer receivers:** HID interfaces of vendor 1532 with a 91-byte feature report (report id + 90 bytes) and a known receiver id (DeathAdder V3 HyperSpeed 00C5) or a product name with HyperSpeed/Wireless/Receiver/Dongle. Battery = class 0x07 command 0x80, charging 0x84; transaction ids 0x1F, 0xFF, 0x3F are tried and the answering one is kept. Queried every second battery tick (default 120 s). | OpenRazer/Synapse protocol; few queries so Synapse is not disturbed. |
| X6 | Device battery lines are always shown by default (option: only when low); thresholds warning 30 %, critical 15 % (lower is worse). | The user asked to see the remaining battery. |
| X7 | Older settings files: items that did not exist yet are added as in the default layout (on, with their section header). Imported layout files: missing items are added switched off. | New features show after an update; an imported layout stays as its author made it. |

## Version 4: layout and look fixes

| # | Decision | Reason |
| --- | --- | --- |
| Y1 | The default layout has a **net** section header. Settings of an older layout version get the header (with a spacer) before the first network item, once (`LayoutVersion` 2). | Requested. |
| Y2 | **CPU line and hot cores** show no load number (the graph shows it); only the "n hot cores hidden, at y%" line has a number. Graphs start right after the fields and go to the right edge (cpu: 17 cells; one-column cores: 17; two columns: 7 + temperature; three columns: 6). History keeps 54 samples. [replaces V6 layout] | Requested; better use of the width. |
| Y3 | **Network lines:** `↓  61M ⣀⣠… 98M`: arrow, value, graph (20 cells), peak of the graph window after the graph. [replaces the `pk` field] | Requested. |
| Y4 | **GPU line:** `4090  77%  85° graph`: load, temperature slot (when the GPU temperature field is on), graph to the right edge. | Same structure as the CPU line. |
| Y5 | **Usage labels** are words, not abbreviations: `5h`, `week`, `opus`, `sonnet`, `premium`, `chat`, `code` (at most 8 characters). One label column width for all usage rows (Claude, Codex, Copilot), from the longest label seen; it only grows. So all bars have the same length. [replaces C2 labels] | Requested. |
| Y6 | **Usage block: 2 lines** per limit. Line 1: label, bar, %. Line 2 (indent 1): `↻ <time to reset>`, and the projection right-aligned: `◷ will last`, `⚠ empty 1d22h before reset` (or `… early` when it does not fit), `◷ too early to project` (or `too early`). [replaces V12] | Requested. |
| Y7 | **Missing usage data:** when a source (Claude, Codex, Copilot) has no data, its items give no lines and keep no space, so the header hides too. After a loss of data, the section stays hidden for at least 1 min, even if data comes back sooner. | Requested: no empty section. |
| Y8 | **Header alignment** (left, center, right) and **indent** (rule characters before a left title or after a right title; 0 = at the edge). Built-in themes keep their look (ember/btop 2, mono 1, nord/dusk/glass 0). | Requested. |
| Y9 | **GPU duplicates:** DXGI adapters with the same name, vendor, device, subsystem and VRAM are one GPU; only the one with load or VRAM use is kept (the first if none is active; all if several are active = real twin GPUs). Twin GPUs: the first keeps the model label, others get `#n` (`40#2`). [replaces V13 numbering] | A GPU listed twice showed as "4091". |
| Y10 | **Acrylic opacity:** the accent tint is fixed and light; the opacity slider sets a colour layer painted over the blur (as for "blur"). | The slider changed only the tint influence. |
| Y11 | **Rounded corners:** DWM non-client rendering is off for the widget and the DWM corner preference follows the corner radius (Windows 11), so no square shadow shows behind a rounded window. | Requested; not checked on Windows. |
| Y12 | **No tooltips while dragging** the widget: tooltips hide on mouse down in the drag area and stay off until the cursor goes to another hit. | Requested. |
| Y13 | **Icons** are drawn as glyph paths, filled and stroked (stroke ≈ 6 % of the font size, round joins), in the widget and the note title bar. | Requested thicker icons, near the font weight. |
| Y14 | **Settings scrolling:** drawing is off while all controls move, then the window and controls redraw once. | Fixes tearing, mostly when scrolling up. |
| Y15 | **Preset lists** show the chosen theme/layout name (`Theme.Name`, `LayoutName`); an empty entry stays for "keep as is". | Requested. |
| Y16 | **Open log:** tray menu and Settings → General → Debug (with Open settings file and Open data folder). Opens with the default program (Notepad if none); makes a short log first if none exists. | Requested. |
| Y17 | **Notes look:** the top bar has the same colour as the note and shows no title; only the minimize and delete icons show it. Notes have their own opacity (Settings → Notes, default 100 %, 30–100); the widget background and text opacity do not change them. [replaces V18 title bar and the 75 % rule] | Requested. |

## Version 5

| # | Decision | Reason |
| --- | --- | --- |
| Z1 | **cpu line:** `cpu   83° graph  25% 3.6GHz` — temperature slot, graph, load, clock with "Hz" (6 characters: `4.8GHz`, `800MHz`). [replaces Y2 for the cpu line] | Requested; "4.8G" alone was unclear. |
| Z2 | **Hot cores** aligned right with an indent. 1 column: indent 2, `c7   88° graph 100% 5.1GHz` (same right edge as the cpu line). 2 columns: 15 + 1 + 15, indent 1, `c18 ⣴⣶⣿  95% 83°` (graph 7 without temperatures). 3 columns: 9 + 1 + 9 + 1 + 9, indent 3, `c7 ⣶⣿ 95%` (label colour = temperature). Every core shows its load. [replaces Y2 for cores] | Requested. |
| Z3 | **Core temperature on AMD:** LHM has no per-core sensor, only per CCD (`CCD1 (Tdie)` …). Physical cores (by CPU-set core index) are split evenly over the CCDs in order; a core shows its CCD temperature. | Shows a useful temperature; exact per-core values are not available. |
| Z4 | **Network lines:** `↓  61M graph 457k pkt` — packets per second (unicast + non-unicast counters of MIB_IF_ROW2), value, space, `pkt`. The graph is still scaled to its peak; the peak value is no longer shown. [replaces Y3] | Requested. |
| Z5 | **Usage polling:** interval at least 1 min (Claude, Codex). HTTP 429: the last values stay on screen, the interval grows by 30 s per 429 up to 5 min, and goes back to the setting after a success. [replaces the doubling backoff] | Requested. |
| Z6 | **Header icons** (option): cpu ⟶ Diagnostic, net ⟶ Ethernet, gpu ⟶ TV monitor, memory ⟶ Component, disk ⟶ Hard drive, claude/codex/copilot ⟶ Robot, devices ⟶ Battery, others ⟶ Tag (Segoe MDL2 glyphs). | Requested. |
| Z7 | **Icons** are 78 % of the font size (was 90 %) and icons before text get one more space. | They were taller than the line and touched the text. |
| Z8 | **Row backgrounds** (each a toggle, all off by default): stripes on every second line of a section, band behind headers, band behind process lines, track behind graphs, track behind bars, chips behind values in warning/critical colours; one strength slider. Drawn as slightly rounded shapes; runs with the same background join. | Requested. |
| Z9 | **20 built-in themes** with different backgrounds, header styles, bars and row backgrounds: ember, btop, mono, nord, dusk, glass, gruvbox, dracula, tokyo-night, catppuccin (mocha), latte (light), solarized, everforest, rose-pine, kanagawa, monokai, one-dark, matrix, synthwave, paper (light). Colours from the public palettes of these schemes. | Requested more themes. |
| Z10 | **Notes:** `>` is a quote (italic, label colour) without indent; `>>` indents one step, `>>>` two. [replaces V19 quote indent] | Requested. |
| Z11 | **Process details:** the values are read-only edit controls in the theme colours: select with the mouse, copy with Ctrl+C or the context menu. Esc still closes the window. | Requested copyable text. |
| Z12 | **Font log:** only problems (missing glyphs or no icon font), once per font setup. | Normal start-up logged a line each time. |
| Z13 | **Disk activity line:** `C:   busy graph  98% queue 3.0`. Name = drive letter; a disk with more partitions shows up to 3 letters (`D:E:F:`); no letter: `disk2`. The name column is as wide as the longest name. Metric names before their values; the busy graph fills the free space. [replaces `d0  66% q 1.0  C:`] | Requested readability. |
| Z14 | **Memory lines:** `ram  disk paging graph 983/s` (PDH `Memory\Pages Input/sec` = hard page faults) and `ram   35G of 40G committed  87%`. [replaces `pgin` and `cmt`] | Requested readability. |
| Z15 | **Disk queue:** `3.0 queued` (average number of I/O requests waiting). **Free space:** `C:  free  14GB / 500GB bar  3%`. [replaces Z13 wording] | Requested; "queue 3.0" had no unit. |
| Z16 | **Memory section without "ram" labels:** `used  29GB / 32GB bar 90%` (shown when little RAM is available; the line shows the used part), `commit  35GB / 40GB bar 87%`, `disk paging graph 983/s`. Sizes with "B". [replaces Z14] | Requested. |
| Z17 | **Hard page faults per process:** `HardFaultCount` of SYSTEM_PROCESS_INFORMATION (offset 16, x64), difference per second. Processes with at least 1 hard fault/s are listed under the paging line, most first, up to 5 (then "n more with hard faults"). Each: name, private bytes (colour along the graph colours by share of the RAM, full at 25 %), hard faults/s (colour by share of all hard faults). Soft faults are not counted. | Requested; no ETW needed. |
| Z18 | **Disk queue** on its own line under the busy line: `└ queued: 3 reads, 1 write` from PDH `Avg. Disk Read/Write Queue Length`. These are averages over the sample time (3 requests waiting for half a second = 1.5), so they are rounded to whole requests; above 0 and below 0.5 is `<1`; nothing = `none`. The busy graph now goes to the %. Paging line: `hard page faults`. [replaces Z15 queue, Z16 paging name] | Requested; no fractions of requests. |
| Z19 | **Process names** in the widget: no `.exe`; the first command-line argument that is not an option (`-x`, `--x`, `/x`) follows, as a file name if it is a path (`python3.13 main.py`, `svchost netsvcs`). The command line is read once per process (cached by PID and start time). Cut names end with `..`. The details window keeps the full name. | Requested. |

