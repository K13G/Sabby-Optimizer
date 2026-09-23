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
