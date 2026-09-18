## Sabby Optimizer 0.23.17

### Full UI revamp
🟢 **+** Reworked the global visual system so cards, buttons, text fields, selectors, tooltips, scrollbars, section headings, and navigation share one cleaner size/spacing language.  
🟢 **+** Updated Darkness, Dark, and Light palettes with clearer surface separation, less harsh borders, and more readable secondary text.  
🟢 **+** Reduced visual clutter with tighter content spacing, smaller repeated headings, cleaner control radii, and quieter hover overlays.

### Window & navigation
🟢 **+** Refined the custom title bar and workspace header with cleaner spacing, a compact accent indicator, and more consistent chrome.  
🟢 **+** Increased the compact sidebar rail slightly for better icon alignment while keeping the expanded menu as an overlay so pages do not shift.  
🩷 **✓** Added a reliable 18 px left-edge pointer check plus the edge hotspot, so the hover menu opens even when clipped hit-testing is inconsistent.  
🩷 **✓** Added a 170 ms delayed close so moving from the screen edge into the expanded menu no longer instantly collapses it.  
🟢 **+** Sidebar hover/selected states now use softer accent surfaces and cleaner spacing.

### Dashboard
🟢 **+** Rebalanced the overview cards and reduced oversized hero elements.  
🟢 **+** Hardware information now uses a clean **4 × 2** grid instead of a 3-row layout with an empty ninth slot.  
🟢 **+** Reduced vertical space so more useful system information is visible without scrolling.

### Tweaks
🟢 **+** Reduced tweak-card height and excess padding while preserving descriptions, verification, and safety information.  
🟢 **+** Tightened category badges, toolbar spacing, FPS status panels, and card gaps for a more consistent fullscreen layout.  
🩷 **✓** Button and badge text remains vertically centered through the shared control templates.

### Interaction polish
🟢 **+** Added a theme-matched tooltip style instead of native-looking Windows tooltips.  
🟢 **+** Scrollbars are thinner and quieter until hovered.  
🟢 **+** Button hover animation is softer and slightly slower so it feels smoother without adding layout work.  
🩷 **✓** No full-page transition animations were reintroduced, keeping the navigation stability/performance fixes from previous releases.
