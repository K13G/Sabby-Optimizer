## Sabby Optimizer 0.23.24

### Sidebar & settings placement
🩷 **✓** Fixed the collapsed Settings button being pushed too far right when Credits was hidden. Settings is now anchored to the 58 px compact rail instead of being centered inside the two-button footer width.  
🩷 **✓** Moving from the expanded sidebar down onto Settings/Credits no longer starts a close race. The footer is now part of the sidebar hover region.  
🟢 **+** Credits still remains hidden while the rail is collapsed and appears only when the menu is expanded.

### Hover performance
🩷 **✓** Removed full-workspace ScaleX animation from the sidebar transition. Scaling the entire WPF page made text blur and forced expensive redraws on dense pages.  
🟢 **+** Sidebar open/close now uses a FLIP transition: one layout update to the final workspace width, then a short TranslateX animation back to rest.  
🟢 **+** Workspace content stays at 100% scale during the animation, so text/cards remain sharp.  
🟢 **+** Sidebar reveal/branding timing is shorter and synchronized for a faster response.

### Existing fixes retained
🟢 **+** Live scrollbar dragging from 0.23.23 remains enabled.  
🟢 **+** The 14 px main scrollbar grab target remains.  
🟢 **+** Update install/restart behavior remains unchanged.
