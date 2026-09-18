# Sabby Optimizer Changelog

Legend: 🟢 **+** added/improved · 🔴 **−** removed · 🩷 **✓** fixed

## Sabby Optimizer 0.23.15

### Startup & responsiveness
🟢 **+** Hardware discovery now starts immediately in parallel with the first WPF frame instead of waiting 3.5 seconds and potentially another 30 seconds for page demand.  
🟢 **+** Dashboard hardware data begins populating as soon as the background worker finishes instead of sitting on a long **Detecting…** state.  
🟢 **+** The full tweak catalog starts after only a short 180 ms render grace period.  
🩷 **✓** Removed extension-rule loading from the normal startup tweak-catalog path to eliminate work from a feature no longer exposed in the UI.  
🟢 **+** The 0.23.14 theme optimization remains: animated-theme global resource refresh work is **50% lower** than 0.23.13 (4 Hz → 2 Hz).

### Sidebar & menu
🩷 **✓** Restored a reliable far-left hover hotspot so moving the pointer to the screen edge opens the sidebar again.  
🟢 **+** Sidebar opening still uses one lightweight clip animation instead of a separate storyboard for every menu label.  
🟢 **+** Sidebar and button hover glows now fade in/out with short opacity animations.  
🩷 **✓** Leaving the Sabby window collapses the sidebar cleanly.

### Tweaks
🩷 **✓** Replaced confusing **Active • protected** text with **Already on / already optimized** when Windows was already in Sabby's target state.  
🟢 **+** Game Mode, Background Capture, and Windows Animations can now deactivate from a pre-existing optimized state because those handlers already define explicit safe fallback behavior.  
🩷 **✓** Settings without a trustworthy rollback value still remain non-destructive instead of inventing a Windows value.

### UI
🟢 **+** Re-centered button text and tweak category/safety badges by removing old 1 px baseline offsets.  
🟢 **+** Added cleaner card hover surfaces, button glow transitions, and more consistent fullscreen/windowed alignment.  
🩷 **✓** Removed the amber protected-switch treatment from settings Sabby did not originally change.

### Updates
🟢 **+** Clicking **Update now** closes the visible Sabby window immediately.  
🟢 **+** A lightweight hidden updater process downloads the installer, verifies SHA-256, starts the silent installer, and lets Setup reopen Sabby.  
🟢 **+** Update downloads use a 256 KB asynchronous stream buffer and no longer keep the full Settings UI alive during the download.  
🩷 **✓** Startup update notices now point to **Settings > Sabby updates** instead of the removed Extensions page.

### Concrete performance reductions
🟢 **+** **50% fewer animated-theme resource refreshes** than 0.23.13.  
🟢 **+** **Up to 33.5 seconds of intentional hardware-discovery waiting removed** from the previous delayed path.  
🟢 **+** Update-window close latency is reduced to helper-launch time instead of waiting for the installer download.  
🩷 **✓** Whole-app startup/RAM percentage varies by PC and enabled features, so release notes report concrete measured/structural reductions instead of inventing a benchmark.

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
