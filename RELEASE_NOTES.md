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
