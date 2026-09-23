# Sabby Optimizer Changelog

Legend: 🟢 **+** added/improved · 🔴 **−** removed · 🩷 **✓** fixed

## Sabby Optimizer 0.23.32

### Dashboard
🩷 **✓** CPU, GPU, memory, and network graphs are embedded inside their original hardware cards instead of appearing as a separate live-monitoring section.
🟢 **+** CPU/GPU/memory percentages sit in the matching card header next to the hardware name.
🩷 **✓** Removed the duplicated live CPU/memory/GPU row that made the dashboard look like a second tab.

### Sidebar
🩷 **✓** Edge-hover no longer restarts the sidebar animation on every mouse movement.
🟢 **+** Navigation icons use compact circular containers.
🩷 **✓** The sidebar keeps the workspace surface color and has no visible divider.

### Debloat
🩷 **✓** Optional/manual packages that Windows allows to remove are no longer incorrectly presented as “Protected by Windows”.
🟢 **+** Only actual Windows/core protection remains disabled.
🟢 **+** Unknown packages can be reviewed and manually removed when Windows permits it; they remain excluded from SAFE ONLY.

### Release
🟢 **+** Version 0.23.32. Previous releases, including 0.23.30 and 0.23.31, remain preserved on GitHub.

## 0.23.31 audit/update

🟢 **+** Dashboard: live CPU/GPU/memory/network graphs plus System Readiness, Storage Used, CPU Load, and Memory Load percentages.  
🩷 **✓** Navigation: removed the left divider, matched the navigation surface to the app surface, and made compact Settings/Credits circles smaller.  
🩷 **✓** Credits stays hidden while the rail is collapsed.  
🟢 **+** Debloat scans a broader current-user Appx inventory and classifies unknown/core packages as protected instead of hiding them.  
🟢 **+** Added more known removable Windows/OEM consumer packages.  
🔴 **−** User-facing “Fixify” renamed to **Fix**.  
🟢 **+** Added Windows Audio, Print Spooler, and DHCP repair actions.  
🟢 **+** Added documented Windows privacy/experience tweaks plus quick-action strips across the main tool pages.  
🩷 **✓** Existing release **0.23.30 remains intact**. This release is 0.23.31, not a deletion or replacement of 0.23.30.

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

# Sabby Optimizer Changelog

## Sabby Optimizer 0.23.26

### Shell and UI cleanup
🩷 **✓** Rebuilt the sidebar as a real layout column instead of a clipped floating rail. Hover no longer animates a large WPF tree, eliminating the old menu lag and the Settings hover-collapse race.
🩷 **✓** Settings is now centered in a fixed 72 px compact rail; Credits is physically clipped until the menu is expanded, removing duplicate/stray footer controls.
🟢 **+** Sidebar expansion is immediate and deterministic: the workspace reflows once instead of running clip/transform animations every frame.
🟢 **+** Removed the old sidebar animation timer, clip geometry, workspace transform, and footer hover race.
🟢 **+** Default window size increased to 1480 × 900 with a 1100 × 700 minimum.
🟢 **+** Main page content and header content are centered with a 1500 px maximum width on large displays.
🩷 **✓** Removed unused sidebar visual elements and duplicate footer markup.

### Performance
🟢 **+** Sidebar hover no longer creates animation clocks or repeatedly changes geometry.
🟢 **+** The main workspace remains at normal render scale during menu interaction, keeping text and cards sharp.
🟢 **+** The shell now uses one predictable layout path for collapsed and expanded navigation, reducing input/render churn.

### Audit status
The shell was re-audited after the 0.23.25 performance pass. The remaining persistent menu issues were traced to the floating clipped sidebar architecture rather than another page-specific animation.



Legend: 🟢 **+** added/improved · 🔴 **−** removed · 🩷 **✓** fixed

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

## Sabby Optimizer 0.23.23

### Scrolling
🩷 **✓** Disabled deferred workspace scrolling, so the page now moves **while the scrollbar thumb is being dragged** instead of jumping only after release.  
🟢 **+** Main scrollbar hit width increased to **14 px** and the thumb now stretches across the usable track instead of exposing only a narrow center grab area.  
🟢 **+** Clicking the scrollbar track can move directly toward the clicked position for faster navigation.

### Sidebar performance
🩷 **✓** Removed the animated sidebar Width change that forced WPF to remeasure the navigation tree every frame.  
🟢 **+** Sidebar reveal now uses a lightweight animated clip while the workspace push/squish remains compositor-based.  
🟢 **+** Hover-open timing is shorter and branding fades are synchronized with the new reveal animation.  
🟢 **+** Exact-edge hover behavior and the workspace push/squish behavior are preserved.

### Update/restart
🟢 **+** The verified 0.23.22 installer/restart path remains intact: download over the app → verify → close Sabby → install → automatically relaunch the installed build.

## Sabby Optimizer 0.23.22

### Installer restart hotfix
🩷 **✓** Fixed Setup error **740 — “The requested operation requires elevation”** after installation.  
🩷 **✓** Removed the non-elevated `runasoriginaluser` launch path that conflicted with Sabby's `requireAdministrator` application manifest.  
🟢 **+** Normal installs now relaunch Sabby through Windows ShellExecute using the **runas** verb.  
🟢 **+** App-driven silent updates use the same elevated relaunch path with `--post-update`.  
🟢 **+** Expected flow is now: **download over Sabby → verify → close Sabby → install → relaunch the newly installed version**.

### Why this version is 0.23.22
0.23.21 was already published with a fixed installer hash. The restart correction is shipped as a new immutable release instead of silently replacing the existing 0.23.21 installer.

## Sabby Optimizer 0.23.21

### Update reliability
🩷 **✓** Fixed the update path where Sabby could hide to the notification area instead of actually closing for installation.  
🟢 **+** The updater now creates a short-lived install marker only after the installer has downloaded and passed SHA-256 verification.  
🟢 **+** When installation starts, Sabby's normal close handler recognizes that marker and performs a real close instead of Close-to-Tray.  
🟢 **+** The updater waits for the existing Sabby process to exit before launching Inno Setup, preventing the running app from keeping its own files locked.  
🟢 **+** A stuck old process gets a bounded close timeout so an accepted update cannot hang forever.  
🟢 **+** Inno Setup launches the newly installed Sabby executable automatically after replacement completes.  
🩷 **✓** The update marker is cleared on the next normal startup, restoring the user's normal tray-close preference.

### Expected flow
**Download over the open app → verify → close Sabby → install 0.23.21 → automatically reopen Sabby 0.23.21.**

## Sabby Optimizer 0.23.20

### Update restart
🩷 **✓** App-driven updates now pass a dedicated update flag to Setup.  
🟢 **+** After installation finishes, Setup explicitly launches the newly installed Sabby Optimizer again as the original Windows user.  
🩷 **✓** The faded update overlay still stays over the open app during download and SHA-256 verification; Sabby closes only for the actual file replacement.

### Performance
🟢 **+** Animated visual-style resource refreshes are reduced from 2 Hz to **1 Hz**, cutting that global WPF invalidation workload another 50%.  
🟢 **+** Removed global per-hover button storyboard creation; hover now uses lightweight visual-state setters.  
🟢 **+** Sidebar push/squish now uses ScaleX + TranslateX compositor transforms instead of a per-frame Thickness/Margin layout animation.  
🟢 **+** Removed redundant nested Task.Run calls around tweak-engine and ping-engine operations.  
🟢 **+** Navigation cache now retains **3** recent pages instead of 4, reducing retained WPF trees.  
🟢 **+** Enabled game detection polls processes every **5 seconds** instead of 2.5 seconds.  
🟢 **+** Full hardware-aware catalog/self-check work waits 700 ms after hardware discovery so the first interactions get more uninterrupted dispatcher time.

### Behavior retained
🟢 **+** The sidebar still visually pushes and compresses the workspace while opening.  
🟢 **+** Blood Bath and other visual styles remain available with the lower-cost animation/resource path.

## Sabby Optimizer 0.23.19

### UI & navigation
🩷 **✓** Fixed the exact-left-edge hover trigger by removing the Windows resize hit area from the left edge and widening Sabby's own edge trigger.  
🟢 **+** The expanded sidebar now **pushes and squeezes the workspace to the right** instead of covering tweak cards and text.  
🩷 **✓** Credits now stays hidden while the sidebar is collapsed and appears only with the expanded menu.  
🔴 **−** Removed the stray page-title accent line and unnecessary sidebar separator.  
🟢 **+** Scrollbars now have a larger grab target and a correctly bound thumb, so dragging the right scrollbar works.

### Themes & Blood Bath
🩷 **✓** Fixed visual styles falling back to blue after switching Dark / Darkness / Light.  
🟢 **+** Theme changes now refresh the actual accent brushes and gradient, not only color resource keys.  
🟢 **+** Blood Bath uses a clearer crimson palette and its event overlay starts/stops immediately when the selected visual style changes.  
🟢 **+** Event animation clocks pause while Sabby is inactive or minimized to reduce idle CPU use.

### Debloat
🩷 **✓** Reworked Appx removal to resolve the installed package, remove it, wait briefly, and verify that Windows no longer reports the package.  
🩷 **✓** Successful removals no longer clear/rebuild the whole card collection, fixing the scroll-down jump after pressing Remove.  
🟢 **+** Debloat now renders 32 cards at a time with Previous/Next paging instead of building 100+ WPF cards at once, reducing visual-tree RAM and UI work.  
🩷 **✓** Bulk SAFE ONLY removal keeps the current page stable while verified removals disappear.

### Performance & backups
🟢 **+** Expensive tweak handler work is dispatched away from the WPF UI thread, preventing synchronous PowerShell/registry setup from freezing clicks.  
🟢 **+** Backup snapshot detection runs away from the dispatcher and backup history/original-state reads run concurrently.  
🩷 **✓** Opening Backups yields to the first frame before loading history, reducing the visible tab-click hitch.  
🟢 **+** Existing bounded page caching and reduced animated-theme refresh work remain enabled.

### Consistency
🩷 **✓** Tweaks now uses the same meter direction as Debloat/Maintenance: **red/low on the left → green/high on the right**.  
🔴 **−** Removed public/internal numbered development labels from active Sabby UI/source naming and replaced them with feature names.

### Updating
🩷 **✓** The updater remains over the still-open Sabby window while downloading and verifying.  
🟢 **+** The helper overlay is now owned by the original Sabby window, keeping it visually attached to the app instead of behaving like a desktop-wide overlay.  
🟢 **+** Sabby closes only after the verified installer is ready to replace program files.

### Audit
See **[BUG_AUDIT.md](BUG_AUDIT.md)** for every static-audit issue found and addressed in this release.

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

## Sabby Optimizer 0.23.16

### Update experience
🟢 **+** Added a full-screen dimmed update overlay so it is immediately obvious that Sabby is updating.  
🟢 **+** The updater now shows centered **Downloading**, **Verifying**, and **Installing** stages instead of disappearing with no visible feedback.  
🟢 **+** Download progress is shown as a real percentage when GitHub provides the installer size.  
🟢 **+** The overlay explains that Sabby will reopen automatically after installation.  
🩷 **✓** If an update fails before installation, the overlay shows a clear failure state, writes the error to the update-helper log, and reopens Sabby instead of silently leaving the app closed.  
🟢 **+** The overlay uses lightweight opacity-only fade animation and does not load the full Sabby UI while the installer downloads.

### Existing performance improvements retained
🟢 **+** Hardware discovery still begins immediately in parallel with the first frame.  
🟢 **+** Animated-theme global resource refresh work remains **50% lower** than 0.23.13.  
🟢 **+** The visible main Sabby window still closes immediately when the updater helper takes over.

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
