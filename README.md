# RunCat Neo for Windows

A running cat that lives in your system tray and speeds up as your CPU gets busier. It's an unofficial Windows port of [RunCat Neo](https://github.com/runcat-dev/RunCatNeo), which is a macOS app. I rewrote it in C# because I wanted the same thing on Windows.

The cat artwork and the animation timing come from the original (Apache-2.0, Copyright 2026 Kyome22). See `LICENSE` and `NOTICE`.

## Requirements

- Windows 10 or 11
- .NET 10 (the runtime is enough, though the SDK works too)

## Build and run

```powershell
cd src
dotnet build -c Release
.\bin\Release\net10.0-windows\RunCatNeo.exe
```

Want a single `.exe` that runs on a machine without .NET installed? Publish it self-contained:

```powershell
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## What it does

Left-click the tray icon to open the dashboard. Right-click for the menu.

The cat's speed follows CPU usage with the same formula the original uses: `speed = clamp(cpu% / 5, 1, 20)`, one frame every `500 ms / speed`. There's a "slower under load" toggle if you'd rather it calm down when things heat up instead of sprinting.

Eight runners ship with it: cat, dog, slime, drop, coffee, Newton's cradle, engine, and mochi. Same frames and frame orders as the macOS version.

A few things are specific to this port:

- The dashboard is a proper tray flyout. It opens right above the icon (found with `Shell_NotifyIconGetRect`, falling back to the cursor when the icon is hidden in the overflow), uses the Windows 11 glass backdrop, and closes when you click away.
- The macOS sprites are wide, because a menu bar is wide. The tray slot is square, so they get scaled to fill the height with a little horizontal cropping. Fit them naively and the cat ends up tiny.
- Icon color follows your taskbar theme, or you can force black or white.

The rest of the menu covers flipping the runner horizontally, the update interval (3, 5, or 10 seconds), and launch at login (a plain `HKCU` Run key, nothing fancy). Settings live in `%APPDATA%\RunCatNeo\settings.json`.

## Custom runners

Drop a folder of PNG frames into `%APPDATA%\RunCatNeo\Runners\`:

```
%APPDATA%\RunCatNeo\Runners\my-runner\
  frame-0.png
  frame-1.png
  frame-2.png
  runner.json        (optional)
```

The optional `runner.json` sets the name, the frame order, and whether the frames are silhouettes:

```json
{
  "name": "My Runner",
  "frameOrder": [0, 1, 2, 1],
  "isTemplate": true
}
```

With `isTemplate` set to true, the frames are treated as monochrome silhouettes and tinted to match your taskbar, so draw them as black shapes on a transparent background, roughly 56×36 like the built-ins. Set it to false to keep the frames in full color. New folders show up in the Runner menu the next time you open it.

## Custom metrics

RunCat watches `%APPDATA%\RunCatNeo\Metrics\*.json` and draws each file as a card on the dashboard. You keep the file up to date with whatever script you like, and RunCat reacts when the file changes. It doesn't poll and it never touches the network. The format matches the original ([spec](https://github.com/runcat-dev/RunCatNeo/blob/main/docs/CustomMetricsSchema.md)):

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

A row with a `normalizedValue` between 0 and 1 gets a small progress bar. `lastUpdatedDate` shows up as "3 min ago" and turns red if the file stops being readable. Write the file atomically (temp file, then rename) so RunCat never catches it half-written. Files inside the Metrics folder are picked up on their own; a file somewhere else you can add from the menu, under Custom metrics → Add JSON source.

### Claude Code usage

`scripts/runcat-statusline.py` is a statusLine hook for Claude Code. Each turn it writes your current model, context window, and rate-limit usage into the watched folder, and a Claude Code card appears on the dashboard.

```powershell
Copy-Item scripts\runcat-statusline.py $HOME\.claude\runcat-statusline.py
```

Then point Claude Code at it in `%USERPROFILE%\.claude\settings.json`:

```json
{
  "statusLine": {
    "type": "command",
    "command": "python \"%USERPROFILE%\\.claude\\runcat-statusline.py\""
  }
}
```

It writes `%APPDATA%\RunCatNeo\Metrics\claude-code.json` by default; set `RUNCAT_OUT_FILE` to send it elsewhere. The same trick works for Codex or anything else that can write a JSON file.

## Command-line flags

`--smoke` renders every runner and samples the CPU once, then exits, which is handy as a quick self-test. `--preview <png>` and `--dash-preview <png>` dump the runner icons and the dashboard to an image so you can check them without clicking around.

## What's missing

I skipped the macOS settings window, the separate metrics bar with the memory, disk, battery, and network graphs, the in-app runner editor, the runner gallery, localization, and the donation UI. The tray cat and the metrics dashboard are here; the rest isn't.

## Credits and license

This is an unofficial port. It isn't affiliated with the original authors, and they didn't ask for it.

The original is [RunCat Neo](https://github.com/runcat-dev/RunCatNeo) for macOS by [Kyome22 (Takuto Nakamura)](https://github.com/Kyome22). The runner artwork is theirs, redistributed under the same license. Everything here is Apache-2.0; see `LICENSE` and `NOTICE`, where I list what I reused and what I changed.

If you're the original author and want the wording or the credit changed, open an issue and I'll fix it.
