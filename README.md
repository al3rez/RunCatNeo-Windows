# RunCat Neo for Windows

Unofficial Windows port of [RunCat Neo](https://github.com/runcat-dev/RunCatNeo) — a cute running cat
(and friends) in your system tray, paced by CPU usage. The busier your CPU, the faster it runs.

Runner artwork and behavior ported from RunCat Neo, Copyright 2026 Kyome22 (Takuto Nakamura),
licensed under the Apache License 2.0 (see `LICENSE`).

## Requirements

- Windows 10/11
- .NET 10 runtime (or SDK)

## Build & run

```powershell
cd src
dotnet build -c Release
.\bin\Release\net10.0-windows\RunCatNeo.exe
```

Self-contained single exe (no .NET runtime needed on target machine):

```powershell
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Features

- **8 built-in runners** — Cat, Dog, Slime, Drop, Coffee, Newton's Cradle, Engine, Mochi
  (same frames and frame orders as the original).
- **CPU-paced animation** — identical formula to the original: `speed = clamp(cpu% / 5, 1, 20)`,
  frame interval `500 ms / speed`. Optional **Slower under load** mode inverts it.
- **Metrics dashboard** — left-click the tray icon (or menu → **Show dashboard**) for a native-style
  tray flyout that pops up **directly above the icon** (resolved via `Shell_NotifyIconGetRect`, with a
  cursor fallback when the icon is in the overflow), with rounded corners, a drop shadow, a slide-in
  from the taskbar edge, and click-away dismiss. Shows a live CPU card plus any custom metrics sources.
  See [Custom metrics](#custom-metrics) below.
- **Theme-aware** — icon tint follows the taskbar theme (Auto), or force Black/White.
- **Flip horizontally**, **update interval** (3/5/10 s), **launch at login** (HKCU Run key).
- **Sized for the tray** — the original's wide macOS menu-bar sprites are rendered to fill the square
  tray slot (fill-height with a bounded horizontal crop) so mascots look prominent, not tiny.
- **Custom runners** — drop a folder into `%APPDATA%\RunCatNeo\Runners\`:

  ```
  %APPDATA%\RunCatNeo\Runners\my-runner\
    frame-0.png
    frame-1.png
    frame-2.png
    runner.json        (optional)
  ```

  `runner.json`:

  ```json
  {
    "name": "My Runner",
    "frameOrder": [0, 1, 2, 1],
    "isTemplate": true
  }
  ```

  `isTemplate: true` treats frames as monochrome silhouettes tinted to the taskbar theme
  (recommended — use black shapes on transparent background, ~56×36 px like the built-ins);
  `false` draws them in full color. New folders appear in the Runner menu the next time it opens.

- Left-click the tray icon toggles the dashboard; right-click opens the menu. The tooltip shows
  current CPU %. Settings persist in `%APPDATA%\RunCatNeo\settings.json`.

## Custom metrics

RunCat watches `%APPDATA%\RunCatNeo\Metrics\*.json` and shows each file as a dashboard card.
You write a small script that keeps a JSON file up to date; RunCat reacts to file changes (no
polling, no network). The schema matches the original
([full spec](https://github.com/runcat-dev/RunCatNeo/blob/main/docs/CustomMetricsSchema.md)):

```json
{
  "title": "Claude Code",
  "symbol": "staroflife",
  "metricsBarValue": "42.7%",
  "metrics": [
    { "title": "Model",   "formattedValue": "Opus 4.8" },
    { "title": "Context", "formattedValue": "42.7%", "normalizedValue": 0.427 }
  ],
  "lastUpdatedDate": "2026-07-17T18:45:05Z"
}
```

Rows with `normalizedValue` (0–1) get a progress bar; `lastUpdatedDate` shows as relative time
and turns red (`Failed`) if the file becomes unreadable. Write atomically (temp file + rename).
Files placed directly in the Metrics folder are picked up automatically; files elsewhere can be
registered via **menu → Custom metrics → Add JSON source…**.

### Claude Code / Codex usage

`scripts/runcat-statusline.py` is a Claude Code statusLine hook that writes your live
model / context-window / rate-limit usage into the watched folder. Setup:

```powershell
Copy-Item scripts\runcat-statusline.py $HOME\.claude\runcat-statusline.py
```

Then in `%USERPROFILE%\.claude\settings.json`:

```json
{
  "statusLine": {
    "type": "command",
    "command": "python \"%USERPROFILE%\\.claude\\runcat-statusline.py\""
  }
}
```

Run Claude Code and a **Claude Code** card appears on the dashboard, updating each turn. The
script defaults to writing `%APPDATA%\RunCatNeo\Metrics\claude-code.json` (override with
`RUNCAT_OUT_FILE`). The same pattern works for Codex or anything else — just emit the schema above.

`RunCatNeo.exe --smoke` runs a headless self-test; `--preview <png>` and `--dash-preview <png>`
render the runner icons and dashboard to an image for inspection.

## Not ported

The original's full settings window, metrics *bar* (the separate menu-bar item with memory/disk/
battery/network graphs), custom runner editor UI, runner gallery, localization, and donation UI
are out of scope. The tray runner and the custom-metrics dashboard are covered.

## Credits & license

This is an **unofficial** Windows port and is not affiliated with or endorsed by the original authors.

- Original: [**RunCat Neo**](https://github.com/runcat-dev/RunCatNeo) (macOS) by [Kyome22 (Takuto Nakamura)](https://github.com/Kyome22).
- Runner artwork is from the original project, redistributed under its license.
- Licensed under the **Apache License 2.0** — see [`LICENSE`](LICENSE) and [`NOTICE`](NOTICE) (which lists what was reused and what changed).

If you maintain the original and would prefer different wording or attribution, please open an issue.
