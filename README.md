# Pulse

A fast, native **Windows 11 replacement for Task Manager**, built with C# / .NET WinUI 3.

> Status: **v0.5.0.** A real WinUI 3 app (`src/Pulse/`) with five pages — Processes (filter, per-process disk I/O, right-click End/Restart/Suspend/Resume/priority/end-tree, details), Performance (CPU, per-core, GPU/power/temps, disk, network), Startup apps, Services (start/stop/restart), and Settings (run-as-admin, appearance, always-on-top, update speed, "make Pulse the default Task Manager") — is [downloadable from Releases](https://github.com/AverageCodeNerd/pulse/releases/latest). The HTML in `mockups/` remains the visual target for the fuller design.

## Repository layout

```
Pulse/
├─ src/Pulse/       → WinUI 3 app (the real thing)
├─ docs/            → GitHub Pages download/landing site (docs/index.html)
├─ mockups/         → Clickable HTML prototype of the app UI (visual spec)
└─ README.md
```

### `src/Pulse/` — the app
```
App.xaml(.cs)            → app entry point
MainWindow.xaml(.cs)     → the window: stat cards, toolbar, process list
Models/ProcessInfo.cs    → one process row (INotifyPropertyChanged)
Services/SystemMonitor.cs→ process sampling + CPU%/RAM math + End task
Services/Native.cs       → GlobalMemoryStatusEx P/Invoke for total/used RAM
```

## Build & run

Requires the .NET 8 SDK and the Windows App Runtime 1.8 (already present on the dev machine).

```powershell
cd src/Pulse
dotnet build Pulse.csproj -c Debug -p:Platform=x64
./bin/x64/Debug/net8.0-windows10.0.19041.0/Pulse.exe
```

It's an *unpackaged* WinUI 3 app (`WindowsPackageType=None`), so it launches straight from the built `.exe` — no MSIX install needed during development.

### Make Pulse the default Task Manager
**Settings → Make Pulse the default Task Manager** flips a switch that redirects every way Windows opens Task Manager (Ctrl+Shift+Esc, the taskbar menu, `Run → taskmgr`) to Pulse. It works via the Image File Execution Options *Debugger* hook and needs one admin approval. The switch writes/removes this value (what you'd otherwise do by hand in `regedit`):

```
HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe
    Debugger = "C:\path\to\Pulse.exe"   (REG_SZ)
```

Turning the switch off (or deleting that value) restores the built-in Task Manager.

### What works today
- Live process list: name, per-process **CPU %** (from CPU-time deltas), working-set **memory**, **threads**, **PID**, responding **status**.
- Real total **CPU %** and **RAM** with progress bars, live process count.
- Click any column header to **sort** (toggles asc/desc); **End task** button + double-click to kill.
- Native Mica backdrop, follows the system light/dark theme.

## The two deliverables so far

### 1. App UI mockup — `mockups/index.html`
Open it in any browser. It emulates the WinUI 3 / Fluent look and is fully clickable:
- **Processes** — grouped Apps/Background list, heat-mapped CPU/Mem/Disk/Net/GPU columns, sortable headers, row selection, **End task**, live-updating values.
- **Performance** — animated CPU graph + per-core grid, plus Memory / Disk / Wi-Fi / GPU with sparklines and stat panels.
- **Startup apps** — impact ratings and enable/disable toggles.
- Light/dark theme toggle (bottom-right).

This is the reference we'll translate into XAML.

### 2. Download page — `docs/index.html`
The marketing/download site, ready for **GitHub Pages**.

## Distribution: GitHub Releases + Pages

### One-time setup
1. Create a repo named `pulse` under **github.com/AverageCodeNerd** and push this folder (links already point at your username).
2. Enable Pages: repo **Settings → Pages → Source: `Deploy from a branch` → Branch: `main` / folder: `/docs`**. Your site goes live at `https://AverageCodeNerd.github.io/pulse/`.
3. (Optional) Add a custom domain in the same Pages settings.

### Cutting a release
Requires the .NET 8 SDK and [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
# 1. Self-contained publish (bundles .NET + Windows App SDK — end users need nothing)
cd src/Pulse
dotnet publish Pulse.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true

# 2. Package the installer (per-user, no admin) -> dist/Pulse-Setup.exe
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer/Pulse.iss

# 3. Publish the GitHub release
gh release create v0.5.0 dist/Pulse-Setup.exe dist/Pulse-portable-x64.zip --title "Pulse v0.5.0" --notes-file notes.md
```

The download buttons point at `.../releases/latest/download/Pulse-Setup.exe`, so they always resolve to the newest release — no page edits per version.

## Next steps
- [x] WinUI 3 project with a live Processes view against real data.
- [x] Left-rail navigation + **Performance** page (CPU history graph, per-core grid).
- [x] **Startup apps** page via the registry `Run` keys + Startup folders.
- [x] **GPU / power / temperature** (NVML) + disk & network graphs on Performance.
- [x] **Settings** page + "make Pulse the default Task Manager" (IFEO hook).
- [x] App icon + self-contained installer + release (download button works).
- [x] Process filter, Run new task, right-click actions (Restart / Open location / Copy), details dialog, Delete shortcut.
- [x] Persisted settings + light/dark/system appearance + always on top.
- [x] **Services** page (list + start/stop/restart via elevated relaunch).
- [x] Run as administrator; per-process **Disk I/O** column.
- [x] Power actions: Suspend/Resume, End process tree, Set priority.
- [ ] Per-process **network / GPU** columns (needs ETW / PDH counters).
- [ ] System tray (minimize to tray).
- [ ] Visual polish + customization (accent colors, column chooser, layout).
- [ ] CPU package power/temperature (needs a signed kernel driver, e.g. LibreHardwareMonitor).
- [ ] Code-sign the installer (removes the SmartScreen warning).
- [ ] winget / Scoop manifests; GitHub Actions workflow to automate releases.
- [ ] ARM64 build.

## License
MIT (intended).
