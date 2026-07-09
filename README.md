# Void AutoClicker

A precision auto clicker for Roblox with a fully custom dark "void" UI.
Windows-only (C# / .NET 8 / WinForms).  Made by Ethan.  v1.3.0

## Files
- **Program.cs** — the entire app (single source file)
- **VoidAutoClicker.csproj** — .NET 8 build config (embeds the icon)
- **void.ico** — Explorer / taskbar file icon
- **build.bat** — one-click builder (publishes the single .exe)
- **demo.html** — standalone animated demo / guided tour
- **icon_preview.png** — preview of the app icon

## Features
- **Update checker** — checks GitHub Releases on launch and offers a download if a newer version exists (with skip-this-version and a manual Check now button in Settings)
- **Stats Tracker** — hero card with all-time wins and XP left in large numbers, daily wins counter with goal pips, armor trim progress toward 396,900 (with avg XP/game and games-left estimates), and quick-add XP logging — all saved between sessions and themed to your accent color
- **Auto-Optimize** — automatically applies your enabled boosts the moment Roblox launches, and restores the power plan when it closes (all remembered between sessions)
- **FPS cap recommender** — reads your GPU / RAM / refresh rate and suggests a cap (can exceed refresh rate for lower input lag); editable value, Uncapped option, and one-tap Apply that writes Roblox's setting (reversible Remove button)
- **Smooth animations** — eased panel transitions, gliding sidebar highlight, animated toggles/sliders/buttons (subtle, professional)
- **Sound cues** — soft tick when the clicker arms / disarms (toggle in Settings, on by default)
- **Icon sidebar** — vertical icon rail (Click · Target · Boost · Stats · Style · Settings) replaces the old tab strip; hover for tooltips
- **Window size control** — Small / Medium / Large scaling in Settings (on top of automatic per-monitor DPI)
- Click rate up to 40 CPS with optional ± randomize range
- **Action: CLICK or HOLD** — rapid clicking, or press-and-hold
- **Output: MOUSE or KEYBOARD** — left/right mouse, or auto-press any key
- **Fixed-position clicking** — capture a screen spot and click it; optional return-cursor
- **Trigger modes** — Hold or Toggle, with a bindable trigger key
- **Panic key** — instantly stops everything (bindable, default F8)
- **Profiles** — save, switch, rename, and delete named presets (ships with a ready-made **Bedwars** profile)
- Roblox-only gating, optional Humanize, always-on-top, minimize-to-tray
- Live CPS graph, session stats, Calibrate + Auto-Tune timing tests
- **BOOST tab** — live CPU / RAM / Roblox monitor, plus reversible "set Roblox to High priority" and "performance power plan" boosts (one-click Optimize/Restore, auto-reverts on close; offers Run-as-Admin when needed)
- Floating in-game overlay (draggable, remembers position)
- 6 accent themes that recolor the whole app (incl. taskbar/tray icon)
- Welcome splash screen (toggleable)
- 1 ms timer resolution for stable click timing
- Per-monitor DPI aware (correct at any display scale)

## Build
Double-click **build.bat** (easiest), or run in this folder:

    dotnet publish -c Release -r win-x64 --self-contained true \
      -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
      -p:DebugType=none -p:DebugSymbols=false

Output: `bin\Release\net8.0-windows\win-x64\publish\VoidAutoClicker.exe`

For a tiny (~1 MB) build that needs .NET installed, drop `--self-contained true`.

Settings are saved to `%AppData%\VoidAutoClicker\settings.json`.
