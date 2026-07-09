# Pulse

A fast, native **Windows 11 replacement for Task Manager**, built with C# / .NET WinUI 3.

> Status: **design mockup stage.** The UI/UX is prototyped in HTML (`mockups/`) as the visual spec. The real WinUI 3 app has not been built yet.

## Repository layout

```
Pulse/
├─ docs/            → GitHub Pages download/landing site (docs/index.html)
├─ mockups/         → Clickable HTML prototype of the app UI (visual spec)
└─ README.md
```

Later, the real app lives alongside these:

```
├─ src/Pulse/       → WinUI 3 app (App.xaml, MainWindow.xaml, Views/, Services/)
└─ .github/workflows/release.yml → builds the installer, attaches to Releases
```

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
- [ ] Get sign-off on the mockup look & layout.
- [ ] Scaffold the WinUI 3 project (`src/Pulse`) and rebuild the Processes view against real data (`System.Diagnostics` / performance counters).
- [ ] Wire Performance counters and the per-core CPU grid.
- [ ] Startup apps via the registry `Run` keys + Startup folder.
- [ ] Packaging (MSIX or self-contained installer) + release workflow.

## License
MIT (intended).
