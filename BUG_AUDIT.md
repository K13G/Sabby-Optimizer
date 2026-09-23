# Sabby Optimizer — 0.23.27 Bug Audit

This audit records the regression found in the 0.23.26 shell rewrite and the targeted cleanup applied for 0.23.27.

| Area | Regression / issue | 0.23.27 correction |
| --- | --- | --- |
| Shell | The 0.23.26 shell rewrite replaced the complete pre-existing navigation/workspace composition, leaving the UI displaced or effectively missing. | Restored the known-good 0.23.25 `MainWindow.xaml` and `MainWindow.xaml.cs` shell and kept the established performance fixes. |
| Navigation | Replacing the previous shell architecture discarded the established edge-hover, workspace push/squish, Credits visibility, and Settings placement behavior. | Restored those existing behaviors without reintroducing the old high-cost per-item hover machinery. |
| Settings | The shell rewrite moved the content origin and broke the expected centered layout. | Restored the previous content host/header/sidebar geometry and centered footer controls. |
| Scrolling | The regression was tied to the replacement shell rather than the existing live-scroll implementation. | Restored the 0.23.25 scrollbar host and drag path. |
| Themes | Blood Bath/event layers disappeared with the replaced shell composition. | Restored the existing event layer and appearance hooks. |
| Update flow | The updater/restart implementation should not be coupled to shell layout changes. | Left the verified download → verify → close → install → relaunch path intact. |
| Internal naming | Remaining `Phase` names/messages exposed implementation-era terminology. | Renamed the remaining phase-specific catalog aliases and removed the user-visible “current phase” wording. |

# Sabby Optimizer — 0.23.26 Bug Audit

This audit records the additional shell/UI issues found after the 0.23.25 performance pass and the fixes shipped in 0.23.26.

| Area | Issue found | 0.23.26 fix |
| --- | --- | --- |
| Sidebar architecture | The sidebar was a 226 px floating Border clipped to a 58 px RectangleGeometry while the workspace was separately translated. This created overlapping hit-test regions and extra render/layout work. | Replaced it with a real Grid column whose width is 72/240 px and whose child width follows the column. |
| Sidebar hover | The shell needed a close timer and several separate MouseEnter/MouseLeave regions to keep Settings/Credits open. | Removed the timer and put navigation + Settings + Credits inside one sidebar hover region. |
| Settings alignment | Settings was rendered in a wider footer than the compact rail, so its center was visually offset. | Settings owns the full 72 px compact cell and its icon bubble is centered in that cell. |
| Credits visibility | Credits depended on a hover-state trigger outside the main sidebar and could remain/appear at the wrong time. | Credits is inside the sidebar and is naturally clipped while collapsed. |
| Sidebar animation | Clip/FLIP animations changed geometry and transforms during every open/close. | Removed sidebar animation clocks and use one immediate column resize. |
| Workspace rendering | The previous menu transition temporarily transformed the live workspace. | Workspace stays at 1:1 render scale; only its layout column changes. |
| Edge hover | Full-window PreviewMouseMove handled the entire pointer stream for edge detection. | Edge detection is limited to the first 6 px and only while the sidebar is collapsed. |
| Presentation clutter | Duplicate legacy footer markup and hidden sidebar controls remained in the shell XAML. | Removed the duplicate footer/legacy controls entirely. |
| Large-window layout | Content used nearly the entire width on wide displays, making settings and cards look offset rather than centered. | Header/content hosts use a centered 1500 px maximum width. |
| Startup presentation | Default shell size was only 1240 × 800. | Default shell size is now 1480 × 900 while retaining a safe 1100 × 700 minimum. |

## Static performance audit notes

A repository-wide source inventory was checked across the WPF shell, views, view models, services, tweak handlers, update path, and release workflow. The highest-confidence interaction hotspots found in the shell were the floating clipped sidebar, its animation/timer machinery, and the duplicated footer hit-test path; those are removed in 0.23.26.

The broader application still contains intentional background `Task.Run` use around blocking Windows/CIM/PowerShell work. Those worker hops are not automatically bugs and were not removed merely because they appear in static search results. Exact CPU/RAM improvement depends on the active page and Windows workload, so no unmeasured percentage is claimed.

---

# Sabby Optimizer — 0.23.19 Bug Audit

This document records the static code/UI audit completed for 0.23.19. Items below are issues found during the audit and the change made for each one.

| Area | Bug found | 0.23.19 fix |
| --- | --- | --- |
| Themes | Changing Dark/Darkness/Light could recreate theme resources with the default blue while a non-blue visual style was still selected. | Visual-style application now reloads its palette and explicitly refreshes accent brush and gradient resources after a theme dictionary swap. |
| Blood Bath | Event droplets were initialized at window load, so selecting Blood Bath later could leave the overlay without active clocks. | Appearance changes now notify the shell to restart only the event clocks required by the selected style. |
| Blood Bath | Crimson styling was visually too close to the default palette in some controls. | Blood Bath primary/secondary accent values were strengthened while keeping the dark background. |
| CPU | Event animation clocks could continue running while the window was inactive/minimized. | Event clocks pause when Sabby is inactive/minimized and resume at the selected speed when active. |
| Sidebar | Windows owned the left resize hit-test strip, so an exact screen-edge hover did not always reach WPF. | Left resize chrome was removed while top/right/bottom resizing remains; Sabby's edge trigger was widened. |
| Sidebar | Expanded navigation covered the workspace. | Sidebar width and workspace margin now animate together, squeezing content to the right. |
| Sidebar | Credits stayed visible while the rail was collapsed. | Credits is collapsed until the sidebar's expanded state is active. |
| Sidebar | Extra hidden footer controls duplicated Settings/Credits logic. | Public footer behavior was consolidated; hidden legacy footer remains non-rendered and is no longer part of the visible interaction path. |
| Header | Decorative accent/separator lines looked like misplaced UI artifacts. | Removed the title underline and sidebar separator. |
| Scrollbar | Custom Track did not bind its Value/Minimum/Maximum back to the ScrollBar, so dragging could fail. | Track now has complete value bindings. |
| Scrollbar | Main scrollbar grab area was too thin. | App scrollbar hit width increased to 12 px and thumb width to 7 px. |
| Debloat | Individual removal rebuilt the entire ObservableCollection. | Successful single removals update the existing bound page without clearing it. |
| Debloat | Rebuilding the collection after Remove could trigger BringIntoView/scroll jumps. | Remove no longer rebuilds the page; the next card is appended in place when needed. |
| Debloat | Appx removal used one direct command followed by a separate verification command and could leave ambiguous results. | Removal now resolves the exact installed package, removes it, retries read-back briefly, and returns success only after verification. |
| Debloat | 100+ cards could be instantiated simultaneously in a non-virtualized UniformGrid. | Results are paged 32 at a time. |
| Meters | Tweaks used green-left/red-right while Debloat/Maintenance used red-left/green-right. | Tweaks now follows the same red-left → green-right convention. |
| Backups | Snapshot detection could begin expensive handler work from the UI dispatcher. | Snapshot creation is dispatched to a worker and yields before starting. |
| Backups | Original-state and snapshot-history reads were sequential. | Both reads run concurrently. |
| Tweaks | Some handlers execute synchronous setup before their first await, causing click hitches. | Handler detect/apply/undo/compatibility work is invoked from a worker thread by the tweak engine. |
| Updater | Update overlay could behave as an independent window rather than being visually tied to Sabby. | The helper overlay is owned by the running Sabby HWND when available. |
| Updater | Users needed clear assurance that the app remains present during download/verification. | The main app remains open under the overlay; installer close/relaunch happens only after verification. |
| Memory | Debloat's full card tree was unnecessarily large. | Paging substantially reduces simultaneous WPF elements. |
| Memory | Previously visited pages could accumulate. | Existing bounded navigation cache remains limited to recent pages. |
| Internal UI | Development-number labels appeared in user-facing text/comments/names. | Active source/UI terminology was renamed to feature-oriented names and user-facing numbered labels removed. |
| Theme UI | Native/default resource behavior could visually disagree with the selected visual style. | Accent brush objects are now replaced/mutated explicitly on apply. |

## Performance notes

0.23.19 targets the largest static sources of UI stalls and excess WPF visual-tree memory found in the audit. Exact total CPU, RAM, and startup percentage changes depend on hardware, selected pages, installed Appx packages, and active visual effects, so the project does not publish an invented whole-app percentage.

Concrete changes include: Debloat renders at most 32 result cards at once; animated theme resource refreshes remain at half the earlier 4 Hz rate; inactive event effects pause; backup/tweak operations no longer start synchronous system work on the UI dispatcher; and navigation caching remains bounded.


## 0.23.20 follow-up

| Area | Issue found | Fix |
| --- | --- | --- |
| Updater | Relaunch was dependent on generic silent-install behavior. | App updates now pass `/SABBYUPDATE=1`; Setup explicitly launches the new Sabby when that flag is present. |
| Hover UI | Every normal button hover allocated short WPF storyboards. | Global button hover now uses state setters only. |
| Sidebar | Workspace squeeze used `ThicknessAnimation`, causing layout work every frame. | Replaced with compositor ScaleX + TranslateX transforms. |
| Animated styles | DynamicResource palette updates still invalidated large parts of the tree twice per second. | Reduced animated palette refresh to 1 Hz. |
| Tweaks | View models wrapped an engine that already dispatches system handlers in another `Task.Run`. | Removed redundant outer scheduler hops. |
| Ping | The same nested `Task.Run` pattern existed in tweak checks. | Uses engine async operations directly. |
| Memory | Four recent page trees were retained. | Cache reduced to three. |
| Idle CPU | Game detection polled every 2.5 seconds. | Polling reduced to every 5 seconds. |
| Startup | Full catalog/self-check work could begin close to first interaction. | Added a 700 ms interaction grace period after hardware discovery. |


## 0.23.23 follow-up

| Area | Issue found | Fix |
| --- | --- | --- |
| Scrollbar | Main workspace had deferred scrolling enabled, so dragging the thumb did not update content until release. | Disabled deferred scrolling for the main ScrollViewer. |
| Scrollbar | The visual scrollbar was wider than the actual thumb/track grab area. | Track now fills the template width, thumb stretches across it, and the main scrollbar is 14 px wide. |
| Sidebar | Sidebar Width was animated on hover, forcing WPF layout/measure work every frame. | Sidebar stays at full logical width and reveals through RectangleGeometry clipping; workspace remains compositor transformed. |
| Sidebar | Branding fade outlasted the rail reveal and made the interaction feel sluggish. | Reduced branding fade timing to track the shorter reveal. |


## 0.23.24 follow-up

| Area | Issue found | Fix |
| --- | --- | --- |
| Settings footer | When Credits collapsed, the Settings button was centered inside a 96 px footer and appeared too far right. | Footer content is left-anchored so Settings stays centered on the compact 58 px rail. |
| Sidebar hover | Settings/Credits lived outside SidebarHost, so entering them could let the close timer collapse the menu. | Footer is now included in hover retention and stops/restarts the same close timer. |
| Sidebar performance | Workspace ScaleX animated the entire live WPF page, causing blur and expensive redraws. | Removed ScaleX entirely; workspace uses a one-pass margin layout plus TranslateX FLIP animation. |
| Sidebar clarity | Scaling changed perceived text/card size during hover. | Workspace remains at 100% render scale throughout the transition. |


## 0.23.25 deep performance audit

| Area | Issue found | Fix |
| --- | --- | --- |
| Logging | FileLogger used synchronous File.AppendAllText from whichever thread logged, including the UI dispatcher. | Logging is queued and batch-flushed on the thread pool; retention cleanup also moved off startup. |
| Startup | Full hardware discovery started before the first window frame, competing for CPU/process/I/O with XAML creation. | Hardware discovery starts after first paint plus a 450 ms idle grace period. |
| Tweaks visual tree | All filtered rich tweak cards were instantiated at once in a non-virtualized UniformGrid. | Tweaks pages the filtered catalog 24 cards at a time. |
| Tweaks state updates | One tweak state change could trigger repeated full-catalog Active/Ready/Protected counts. | State-stat refreshes are coalesced at DispatcherPriority.Background. |
| Tweaks cleanup | An old full-scan initialization method remained dead code. | Removed the unreachable method. |
| Settings sliders | Every pointer-pixel change on intensity/speed triggered a global appearance resource refresh. | Slider preview refresh is debounced to a short 70 ms quiet window. |
| Settings persistence | Every behavior checkbox immediately serialized and replaced settings.json. | Rapid behavior changes are coalesced into one 280 ms delayed save. |
| Style picker | Virtualization relied on framework defaults. | Explicit recycling virtualization and content scrolling are enabled. |
| Appearance | Animated frame updates allocated/replaced a new LinearGradientBrush each tick. | Existing gradient stops are mutated when possible; identical Color resources are skipped. |
| Navigation hover | Sidebar-item hover templates created short Storyboards/animation clocks. | Hover feedback is now direct state styling without per-item animation clocks. |
| Monitoring | Optional NVIDIA monitoring could spawn nvidia-smi every 2 seconds. | GPU sensor result is cached for 5 seconds and helper priority is lowered when supported. |


## 0.23.31 follow-up

| Area | Issue/change | Fix |
| --- | --- | --- |
| Dashboard | No live graphs or percentage readouts. | Added bounded CPU/RAM/GPU/network graphs and readiness percentages. |
| Sidebar | Left rail looked visually separate and had an unwanted line. | Same workspace surface color; selection indicator removed. |
| Sidebar footer | Settings/Credits circles stayed large while collapsed. | Compact/expanded sizes now differ. |
| Debloat | More optional Appx packages were displayed as generic entries. | Expanded classification and friendly descriptions. |
| Navigation | User-facing Fixify name. | Renamed to Fix while keeping the internal enum. |
| Tweaks | Missing several documented privacy/safety opt-in controls. | Added seven reversible policy controls. |
| Versioning | Prior release must remain available. | Workflow now explicitly preserves v0.23.30. |

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

