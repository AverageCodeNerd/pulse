# Pulse

A fast, native **Windows 11 replacement for Task Manager**, built with C# / .NET WinUI 3.

> Status: **working MVP.** A real WinUI 3 app (`src/Pulse/`) builds and runs with a live process list, real per-process CPU/memory/threads, total CPU & RAM, sortable columns, and End task. The HTML in `mockups/` remains the visual target for the fuller design.

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

### Publishing a build
1. Build the installer (`Pulse-Setup.exe`) — later this is automated by a GitHub Actions workflow.
2. Create a release: **Releases → Draft a new release → tag `v0.1.0`**, then attach `Pulse-Setup.exe`.
3. The download buttons already point at
   `.../releases/latest/download/Pulse-Setup.exe`, so they always resolve to the newest release — no page edits needed per version.

## Next steps
- [x] Scaffold the WinUI 3 project and build the Processes view against real data.
- [ ] Left-rail navigation + a **Performance** page (CPU history graph, per-core grid) to match the mockup.
- [ ] Per-process disk/network/GPU columns (needs ETW / PDH counters).
- [ ] Startup apps via the registry `Run` keys + Startup folder.
- [ ] App icon, and a self-contained installer + release workflow so the download page's button works.

## License
MIT (intended).
