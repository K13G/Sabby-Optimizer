## Sabby Optimizer 0.23.25

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
