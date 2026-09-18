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
