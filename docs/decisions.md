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
| C5 | Per-model lines: every `seven_day_<x>` object except `seven_day_oauth_apps`. Labels: `opus`, `son` (sonnet), `hai` (haiku), else the first 4 letters. Same two-line layout as `wk`, with the window-average rate. | Simplest consistent layout. |
| C6 | Projection colour: severity of (time to reset ÷ time to limit), warning 1, critical 2. The spec example (limit in 1 h, reset in 4 h → ratio 4) is red. | The spec gives no numbers for this gradient. |
| C7 | The 5h rate uses the samples from 30 min ago to now. A new window (reset time moves by more than 5 min) or a drop in usage clears the history. | Spec: "last 30 min of the widget's own samples". |
| C8 | Usage bars are 20 cells wide and monochrome. The "lasts" / "limit 1h00" / "—" text is right-aligned on the second line. | Matches the spec example (42 % = 8⅜ cells). The spec gives no threshold for used %. |
