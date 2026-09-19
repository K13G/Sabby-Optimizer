## Sabby Optimizer 0.23.22

### Installer restart hotfix
🩷 **✓** Fixed Setup error **740 — “The requested operation requires elevation”** after installation.  
🩷 **✓** Removed the non-elevated `runasoriginaluser` launch path that conflicted with Sabby's `requireAdministrator` application manifest.  
🟢 **+** Normal installs now relaunch Sabby through Windows ShellExecute using the **runas** verb.  
🟢 **+** App-driven silent updates use the same elevated relaunch path with `--post-update`.  
🟢 **+** Expected flow is now: **download over Sabby → verify → close Sabby → install → relaunch the newly installed version**.

### Why this version is 0.23.22
0.23.21 was already published with a fixed installer hash. The restart correction is shipped as a new immutable release instead of silently replacing the existing 0.23.21 installer.
