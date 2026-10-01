# Ember — Requirements

Source: https://claude.ai/code/artifact/73313b90-90f2-4b07-a0b9-a50ed89dcbaa (Oct 1, 2026)

## Purpose and general rules

The widget shows only relevant information. It hides normal values. When a value is relevant, it also shows the cause.

- **Always visible:** combined CPU load, network down/up, Claude 5h and weekly limits.
- **All other items:** visible only when relevant, that is, when a value crosses its threshold.
- **Optional items:** VRAM and network quality. Off by default.
- **Configurable:** every threshold, interval and item on/off is in Settings.
- **Show delay:** the condition must stay true for 5 s. **Hide delay:** the value must stay normal for 10 s. This stops flicker.
- **Silent failure:** if a sensor or data source fails, hide its item. Show no error messages.
- **Top process:** click → details popup (name, PID, path, command line, CPU, RAM, GPU, disk, start time). Right-click → End process (with confirmation), End process tree, Open file location.
- **Notes:** separate windows. See Notes.
- **Normal state:** approximately 5 lines.

## Platform and runtime

One compiled C# executable for Windows 10 and 11 (x64).

| Item | Decision |
| --- | --- |
| Language | C# on .NET 10 (LTS). Support for .NET 8 ends in November 2026. LibreHardwareMonitorLib (LHM) is a .NET library. |
| Window | Borderless, transparent window on the desktop layer, not topmost. Clicks go through it, except on top-process lines and in edit mode. |
| Drawing | GDI+ text only. No animations, no GPU effects. Redraw only when the text changes. See Style. |
| Update rate | Sensors: 1 s. Claude usage: 1 min. Both configurable. |
| Admin rights | Necessary only once, at install. The installer creates a scheduled task that starts the widget at logon with highest privileges. No UAC prompt at each start. |
| Driver | LHM installs a kernel driver to read sensors. Some antivirus tools flag it. |
| Targets | RAM < 60 MB, CPU < 1%, GPU approximately 0%. |

## Style

Text only, similar to btop. One compact line for each item: label, value, unit, braille graph, peak.

```
cpu   23%     ⣀⣀⣠⣤⣀⣀⣠⣴⣶⣤⣀⣀
↓  61.0 Mbps  ⣀⣠⣤⣴⣶⣿⣿⣷⣶⣤ pk 61
↑   2.1 Mbps  ⣀⣀⣀⣀⣠⣀⣀⣀⣀⣀ pk 4

─ G P U ────────────────────────
rtx   64%  71°  ⣀⣤⣶⣿⣿⣷⣶⣿⣿⣷
  └ game.exe  58%

─ C L A U D E ──────────────────
5h  ████████▍           42%
    ↻ 3h10             lasts
wk  ███▌                18%
    ↻ 4d02             lasts
```

- **Font:** monospace. Cascadia Mono or Consolas (both on Windows).
- **Graphs:** braille characters (U+2800–U+28FF). One character = 2 samples × 4 levels. 12 characters = the last 24 s.
- **Usage bars:** block characters in 1/8 steps (`▏▎▍▌▋▊▉█`).
- **Sections:** CPU and network lines have no header. All other groups (GPU, MEMORY, DISK, CLAUDE) have a header: letter-spaced capitals and a dim rule. A header shows only when its section has visible items.
- **Top process:** indented under its section with `└`.
- **Emphasis:** value bright; label, unit and peak dim.
- **Width:** 32 characters.
- **Colours:** monochrome for normal values. Values with thresholds use a gradient: the colour moves smoothly from normal to amber at the warning threshold, then to red at the critical threshold. Each graph sample uses the same gradient.

## CPU

Combined load is always visible. Other CPU items show only when relevant.

| Item | Visible when | Source |
| --- | --- | --- |
| Combined load | Always | PDH `Processor Information(_Total)\% Processor Utility` |
| Outlier logical cores | σ ≥ 10 points, and \|core load − median\| > 2σ | PDH, one instance per logical core |
| Temperature | Package ≥ 75 °C (amber), ≥ 90 °C (red) | LHM |
| Throttling | `% Processor Performance` < 90 and load ≥ 50% | PDH |
| Top process | Combined load ≥ 60%, or an outlier core is visible | Process CPU time difference each second |

Rules:

- Smooth each core load over 3 s (exponential moving average).
- On hybrid CPUs, calculate the median and σ for each core type (P-core, E-core).
- For each outlier core, show the core number, a load bar and a median marker.
- `% Processor Utility` can go above 100% with turbo. Limit the display to 100%.
- On AMD Ryzen, LHM gives package (Tctl) and CCD temperatures, not per-core values.

## GPU

One block for each GPU. All GPU items show only when relevant.

| Item | Visible when | Source |
| --- | --- | --- |
| Load (busiest engine) | ≥ 10% | PDH `GPU Engine(*)\Utilization Percentage`, grouped by adapter |
| Temperature | ≥ 70 °C (amber), ≥ 85 °C (red) | LHM |
| VRAM | Optional, off by default. Dedicated use ≥ 80% (amber), ≥ 95% (red) | PDH `GPU Adapter Memory(*)\Dedicated Usage`; total from DXGI |
| Top process | Load ≥ 50% | PDH engine instance names contain the process ID |

Rules:

- Load = the busiest engine (3D, compute, copy, video). Task Manager uses the same method.
- Integrated GPUs use shared memory. Do not show VRAM for them.

## Memory and disk

These sections show only when relevant. Used RAM alone is not shown, because Windows uses free RAM as cache.

| Item | Visible when | Source |
| --- | --- | --- |
| Available RAM | < 15% of total (amber), < 5% (red) | `GlobalMemoryStatusEx` |
| Commit charge | ≥ 85% of commit limit | `GetPerformanceInfo` |
| Hard page faults | `Memory\Pages Input/sec` ≥ 1000 | PDH |
| Top memory process | Any memory item is visible | Private bytes per process |
| Disk busy (per physical disk) | Active time ≥ 80%, or queue length ≥ 2 | PDH `PhysicalDisk`: 100 − `% Idle Time`, `Avg. Disk Queue Length` |
| Free space (per volume) | < 10% or < 20 GB (amber), < 5% (red) | `GetDiskFreeSpaceEx` |
| Top disk process | A disk is busy | PDH `Process\IO Data Bytes/sec` (also counts network I/O, so it is approximate) |

## Network

Down and up are always visible, one line each. Quality warnings are optional and off by default. When on, they show only when relevant.

| Item | Visible when | Source |
| --- | --- | --- |
| Up / down total | Always | `GetIfTable2` byte counters, difference each second |
| Packet loss | Optional. ≥ 5% of the last 20 pings | ICMP ping to the gateway and to 1.1.1.1, every 5 s |
| Latency | Optional. Median of last 5: gateway ≥ 20 ms, internet ≥ 100 ms | Same pings |

Rules:

- Count only physical adapters (`ConnectorPresent` flag). Exclude loopback, tunnels, VPN and virtual adapters (Hyper-V, WSL). VPN traffic also goes through the physical adapter.
- Loss at the gateway = local problem. Loss only to the internet = ISP problem. Show which one.

* Also exclude adapters by description: Tailscale, WireGuard, Wintun, ZeroTier, TAP-Windows, OpenVPN, Hyper-V, vEthernet, VirtualBox, VMware. The list is configurable.
* Show the adapter name only when more than one physical adapter has traffic.

## Claude usage

The 5h and weekly lines are always visible. Each line shows used %, time to reset, and projected time to limit.

Projection (W = window length: 5 h or 7 days):

```latex
\text{elapsed} = W - (t_{reset} - t_{now})
\qquad \text{rate} = \frac{\text{used}}{\text{elapsed}}
\qquad t_{limit} = \frac{100 - \text{used}}{\text{rate}}
```

Example: 50% used after 1 h of 5 h. The rate is 50%/h, so the limit comes in 1 h. The reset is in 4 h, so the line is red.

Rules:

- **5h rate:** use the last 30 min of the widget's own samples. This follows your current pace. Before 30 min of samples exist, use the window average.
- **Weekly rate:** use the window average. A 30 min rate is too noisy for 7 days.
- Limit after reset: show "lasts" in grey. Limit before reset: show the time to limit with the gradient colour.
- Elapsed < 10 min: do not project. Show "—".
- Per-model weekly limit: show it only if its used % is higher than the general weekly limit.

Data source:

- Get the OAuth token from the Claude Code credentials file (`%USERPROFILE%\.claude\.credentials.json`).
- Call the usage endpoint that Claude Code uses for `/usage`. It is not documented and can change.
- Claude.ai and Claude Code use the same plan limits, so this one source covers both.
- Poll every 1 min (configurable). On HTTP 429, double the interval until a request succeeds.
- Do not refresh the token. A refresh can stop Claude Code's own login. If the token is expired or a request fails, hide the Claude lines.

## Notes

Notes are separate windows, not part of the status widget. You can have any number of notes.

- Each note is a small borderless window on the desktop layer, with its own position and size.
- New note: from the tray icon, or with a hotkey (configurable). Delete: from the right-click menu of the note.
- Simple markdown: headings, bold, italic, inline code, lists, checkboxes. The editor styles the text as you type. The markdown markers stay visible but dim. There is no preview mode.
- Click a checkbox to tick it.
- Storage: one `.md` file per note in `%APPDATA%\Ember\notes\`. Save 1 s after each change.
- Same font and colours as the status widget.

## Settings

A small settings window. Open it with a right-click on the widget or from the tray icon. It loads only when opened, so it uses no RAM at other times.

- **Items:** on/off for each item. VRAM and network quality are off by default. All others are on.
- **Thresholds:** warning and critical values for each item.
- **Intervals:** sensors (default 1 s), Claude usage (default 1 min).
- **Network:** the adapter exclusion list.
- **Layout:** position, monitor, font size, opacity.
- **Notes:** hotkey for a new note.
- **Start at logon:** on/off. Only a change asks for admin rights.
- **Storage:** `%APPDATA%\Ember\settings.json`. Changes apply at once.

## Robustness

- **DPI:** per-monitor DPI awareness (v2), so text stays sharp on all monitors.
- **Explorer restart:** a desktop-layer window can disappear when explorer.exe restarts. Attach the window again on the `TaskbarCreated` message.
- **Idle:** stop all sampling when the session is locked or the display is off.
- **Single instance:** a second start only brings the existing instance to the front.
- **Debug log:** off by default. Turn it on in Settings when a source fails silently.

## Open items

- [x] Language: C# on .NET 10.
