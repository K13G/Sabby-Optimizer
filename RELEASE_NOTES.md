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
