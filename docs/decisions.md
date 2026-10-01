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
