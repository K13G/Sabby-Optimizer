# Sabby Optimizer 0.23.31

### Dashboard
🟢 **+** Added a larger live dashboard health section with **System Readiness, Storage Used, CPU Load, and Memory Load** percentages.  
🟢 **+** The existing CPU/GPU/memory/network graphs remain live and now sit alongside concrete readiness percentages.

### Navigation & appearance
🩷 **✓** Removed the vertical divider line from the left navigation.  
🟢 **+** The left navigation surface now uses the same surface color as the main application panels.  
🟢 **+** Settings/Credits circular buttons shrink in the compact rail and grow smoothly when the sidebar expands.  
🩷 **✓** Credits remains hidden until the sidebar is expanded.

### Debloat
🟢 **+** Debloat now scans a much broader set of current-user Appx packages instead of hiding large groups before classification.  
🟢 **+** Added more Windows/OEM consumer packages to the known removable catalogue, including additional Weather, Maps, Calculator, Notepad, Photos, Sound Recorder, Journal, Family, Power BI and communications packages.  
🩷 **✓** Unknown Appx packages default to protected/manual-only instead of being treated as removable.  
🩷 **✓** Windows shell, Store, runtime, security and account components remain protected.

### Fix
🔴 **−** Renamed the user-facing **Fixify** tab to **Fix**.  
🟢 **+** Added verified Windows Audio restart.  
🟢 **+** Added Print Spooler restart and DHCP Client restart tools.  
🟢 **+** Expanded the Fix page with cleaner repair categories and quick-action visibility.

### Tweaks
🟢 **+** Added more documented Windows experience/privacy controls, including Get Started promotional content, app-promotion suggestions, and feedback notifications.  
🟢 **+** Existing advanced network, CPU, FPS, privacy, security and input controls remain grouped separately so they do not get silently mixed into Apply Best.

### Other tabs
🟢 **+** Ping now exposes best-endpoint scan, route tracing, DNS flush and undo actions together.  
🟢 **+** Maintenance has a dedicated quick-action strip for TEMP analysis, cleanup and SAFE ONLY startup cleanup.  
🟢 **+** Game Profiles, GPU Drivers, Updates, Benchmarks, Backups, PC Restore and Settings now expose clearer toolset/status strips without changing their existing data paths.  
🩷 **✓** These changes retain the existing 0.23.30 release; **0.23.30 is not deleted or replaced**.

### Release hygiene
🟢 **+** All user-facing release documentation now describes the additions specifically instead of vague “new stuff” text.
