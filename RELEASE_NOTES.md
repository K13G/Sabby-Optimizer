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
