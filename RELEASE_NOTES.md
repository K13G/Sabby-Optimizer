## Sabby Optimizer 0.23.18

### Update overlay
🩷 **✓** Sabby no longer disappears as soon as an update starts.  
🟢 **+** The updater now places its faded overlay directly over the visible Sabby window instead of covering the entire desktop.  
🟢 **+** The current optimizer remains visible underneath during **Downloading** and **Verifying**.  
🟢 **+** The app closes only after the installer has been fully downloaded and SHA-256 verified, when installation actually needs to replace the running files.  
🩷 **✓** If download or verification fails, the overlay closes and the existing Sabby window remains open.

### Left hover menu
🩷 **✓** Replaced the clipped 226 px sidebar hit-test trick with a real overlay-width animation.  
🟢 **+** Collapsed sidebar width is physically 58 px; expanded width is physically 226 px, so the mouse hit area now matches the visible menu.  
🟢 **+** The sidebar spans the shell instead of being constrained to the narrow first Grid column, allowing the expanded flyout to receive mouse input over the workspace.  
🩷 **✓** The dedicated edge hotspot and 18 px root-level edge detection remain as additional fallbacks.  
🩷 **✓** The 170 ms delayed collapse remains, preventing the menu from closing while the pointer moves into the flyout.
