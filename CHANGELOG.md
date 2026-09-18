# Sabby Optimizer Changelog

Legend: 🟢 **+** added/improved · 🔴 **−** removed · 🩷 **✓** fixed

## Sabby Optimizer 0.23.14

### Performance & memory
🟢 **+** Added a bounded four-page navigation cache so browsing many tabs no longer keeps every visited page alive for the entire session.  
🟢 **+** Animated-theme resource refresh frequency is reduced from 4 Hz to 2 Hz, cutting that global WPF invalidation workload by 50%.  
🟢 **+** Animated themes pause their global color updates while Sabby is minimized or not the active window.  
🔴 **−** Removed retired Presets, Config Studio, and Monitoring page registrations from the startup navigation graph.

### Navigation & menu
🟢 **+** Sidebar expansion now uses one compact rail/clip animation instead of launching a separate opacity storyboard for every navigation label.  
🟢 **+** Sidebar hover detection now reacts to enter/leave only instead of processing every mouse-move event across the window.  
🩷 **✓** Rapid/re-entrant tab changes are queued safely so the highlighted menu item cannot desync from the page being shown.  
🩷 **✓** Evicted Game Profiles pages now detach their game-detection event subscription instead of remaining retained in memory.

### UI & animations
🟢 **+** Tightened card padding, radii, button borders, input borders, and metric-card sizing for a cleaner, less chunky interface.  
🟢 **+** Sidebar open/close timing is shorter and more consistent while retaining smooth easing.  
🩷 **✓** Kept expensive full-page navigation animations disabled; only small compositor-friendly UI motion remains.

### Performance target
🩷 **✓** This pass specifically targets lower RAM retention and substantially lower interaction stutter. Exact whole-app percentages depend on which tabs, themes, and background features are active; Sabby does not report an unmeasured RAM/FPS claim.


## 0.23.13

### Settings
🟢 **+** Added a **Sabby updates** card with installed version, available version, status, **Check now**, and **Install update & restart**.  
🟢 **+** Automatic Stable-channel checks remain enabled.

### Navigation
🔴 **−** Removed the **Extensions** tab.  
🩷 **✓** Old Extensions sessions return to Settings.

### Updater
🟢 **+** Changed the official manifest to `K13G/Sabby-Optimizer`.  
🩷 **✓** Migrates localhost and `mrcoem/mrcoem` feeds automatically.  
🩷 **✓** Silent installs relaunch the updated Sabby executable.

### GitHub
🟢 **+** Cleaner front page with version/platform/owner badges.  
🟢 **+** Grouped, specific release notes instead of generic “bug fixes” text.
