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
