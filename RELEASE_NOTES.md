# Sabby Optimizer Release Notes

## Sabby Optimizer 0.23.27

### Shell restored and repaired
🩷 **✓** Restored the complete pre-0.23.26 shell layout so the dashboard, page header, navigation, Settings, Credits, workspace, scrolling, and visual-style layers are present again.
🩷 **✓** Removed the 0.23.26 floating-column shell rewrite that caused the UI to appear missing or displaced.
🩷 **✓** Preserved the 0.23.25 performance work instead of replacing the whole shell with a new layout model.
🟢 **+** Kept live scrollbar dragging, exact-left-edge menu opening, workspace push/squish, centered Settings, hidden Credits while collapsed, Blood Bath resources, and the existing update overlay/restart path.

### Cleanup
🩷 **✓** Removed remaining internal development “phase” terminology from source-facing names and user-visible messages.
🟢 **+** Kept the application versioning and stable update pipeline aligned at 0.23.27.

## Sabby Optimizer 0.23.26

### Shell and UI cleanup
🩷 **✓** Rebuilt the sidebar as a real layout column instead of a clipped floating rail. Hover no longer animates a large WPF tree, eliminating the old menu lag and the Settings hover-collapse race.
🩷 **✓** Settings is now centered in a fixed 72 px compact rail; Credits is physically clipped until the menu is expanded, so there is no duplicate/stray footer UI.
🟢 **+** Sidebar expansion is immediate and deterministic: the workspace reflows once instead of running clip/transform animations every frame.
🟢 **+** Removed the old sidebar animation timer, clip geometry, workspace transform, and footer hover race.
🟢 **+** Default window size increased to 1480 × 900 with a 1100 × 700 minimum.
🟢 **+** Main page content and header content are centered with a 1500 px maximum width on large displays.
🩷 **✓** Removed the unused sidebar/footer visual elements that caused duplicate settings controls and stray lines.

### Performance
🟢 **+** Sidebar hover no longer creates animation clocks or repeatedly changes geometry.
🟢 **+** The main workspace remains at normal render scale during menu interaction, keeping text and cards sharp.
🟢 **+** The shell now uses one predictable layout path for collapsed and expanded navigation, reducing input/render churn.

### Scope
This release specifically targets the persistent shell/menu lag, Settings alignment, menu hover retention, oversized empty workspace presentation, and unnecessary sidebar visual machinery reported after 0.23.25.

### Deep performance cleanup
🟢 **+** Moved file logging off the UI thread and batch log writes in the background, removing synchronous disk writes from clicks, navigation, tweak operations, and appearance changes.  
🟢 **+** Full hardware/CIM discovery now waits until after the first interactive frame plus a short idle grace period instead of competing with cold startup.  
🟢 **+** Tweak cards are paged **24 at a time**, cutting the largest WPF visual tree roughly in half on the current catalog while keeping search/category/sort across the entire catalog.  
🩷 **✓** Tweak statistics are coalesced to one background-priority UI refresh instead of repeatedly recounting the whole catalog for every state-property change.  
🟢 **+** Style picker explicitly uses WPF recycling virtualization.  
🟢 **+** Animated appearance gradients are reused instead of allocating/replacing a new gradient brush every frame.  
🟢 **+** Identical color resources are no longer replaced unnecessarily.

### Interaction cleanup
🩷 **✓** Appearance sliders now debounce their expensive global DynamicResource refresh to ~70 ms while dragging, instead of repainting the full application for every pointer pixel.  
🟢 **+** Rapid Settings checkbox changes are coalesced into one settings-file write after a short quiet period.  
🔴 **−** Removed per-navigation-item hover Storyboards; hover feedback now uses direct lightweight state changes without creating animation clocks.  
🩷 **✓** Removed an unused Tweaks background-initialization method that could no longer be reached.

### Background CPU
🟢 **+** NVIDIA sensor polling, when monitoring is explicitly running, now reuses the last GPU sample for 5 seconds instead of spawning nvidia-smi every 2 seconds.  
🟢 **+** NVIDIA sensor helper processes are lowered to BelowNormal priority where Windows permits it.  
🟢 **+** Existing inactive-window event-animation pausing, bounded navigation cache, lazy tweak detection, live-scroll fixes, and the 0.23.24 FLIP sidebar transition remain enabled.

### Scope
This pass targets static/code-visible sources of UI stalls, allocation churn, synchronous I/O, large visual trees, and unnecessary background work. Exact RAM/CPU/FPS improvement varies by page, hardware, installed apps, and selected visual style; the changelog does not invent a percentage.
