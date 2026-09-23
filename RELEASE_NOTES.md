## Sabby Optimizer 0.23.31

### Dashboard
🟢 **+** Live CPU, memory, GPU, and network mini-graphs with percentage readouts.  
🟢 **+** Performance, network, and privacy readiness percentages.  
🟢 **+** One shared low-frequency monitor with bounded 24-sample history.

### Navigation & UI
🟢 **+** Sidebar now matches the workspace surface color.  
🔴 **−** Removed the thin left navigation selection line.  
🟢 **+** Settings/Credits icon bubbles shrink in collapsed mode and grow when expanded.

### Debloat
🟢 **+** Expanded optional Appx classification for legacy Skype, 3D Viewer, 3D Builder, Camera, Sticky Notes, Calculator, Notepad, Photos, and more Microsoft consumer packages.  
🟢 **+** More packages receive useful names and descriptions instead of generic unknown entries.

### Fix
🔴 **−** Renamed the user-facing **Fixify** page to **Fix**.  
🟢 **+** Fix now covers repair, recovery, cleanup, and diagnostics.

### Tweaks
🟢 **+** Added 7 opt-in privacy/safety controls: Consumer Features, Cloud Optimized Content, Windows Location Services, Camera App Access, Microphone App Access, Location App Access, and Widgets/News.

### Versioning
🟢 **+** 0.23.30 remains a separate preserved rollback/reference release.

# Sabby Optimizer Release Notes

## Sabby Optimizer 0.23.30

### 0.23.27 functional baseline

- Restores the known-good 0.23.27 shell/application baseline.
- Does **not** carry forward the 0.23.28/0.23.29 shell/settings rework that introduced the current regressions.
- Keeps the release identity at 0.23.30 so an installed 0.23.28 or 0.23.29 can see the update.
- Preserves the existing 0.23.27 application behavior, update path, settings persistence, and performance work.

### Versioning

The executable/installer version is 0.23.30 for correct update ordering. The implementation baseline is 0.23.27.

## Sabby Optimizer 0.23.27

### Shell restored and repaired
🩷 **✓** Restored the complete pre-0.23.26 shell layout so the dashboard, page header, navigation, Settings, Credits, workspace, scrolling, and visual-style layers are present again.
🩷 **✓** Removed the 0.23.26 floating-column shell rewrite that caused the UI to appear missing or displaced.
🩷 **✓** Preserved the 0.23.25 performance work instead of replacing the whole shell with a new layout model.
🟢 **+** Kept live scrollbar dragging, exact-left-edge menu opening, workspace push/squish, centered Settings, hidden Credits while collapsed, Blood Bath resources, and the existing update overlay/restart path.
