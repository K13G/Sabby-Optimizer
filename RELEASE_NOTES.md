# Sabby Optimizer 0.23.32

## Update detection
🩷 **✓** Fixed stable-update detection being delayed by cached GitHub raw-manifest responses. Sabby now cache-busts the official mutable stable manifest on each check.
🟢 **+** Startup update checking now begins after a short 700 ms interactive-shell grace period instead of waiting 2.2 seconds.
🩷 **✓** Automatic Stable-channel checking remains enabled and still uses the permanent K13G manifest URL.
🟢 **+** A newly published mandatory release is now detected on the next startup check without requiring a manual reinstall or changing the update URL.

## Dashboard
🟢 **+** Keeps the live CPU/GPU/memory/network graphs and readiness percentages from 0.23.31, with the metrics grouped directly with their hardware cards.
🟢 **+** Keeps the dashboard hardware/status layout improvements from 0.23.31.

## Navigation
🩷 **✓** Keeps the borderless left navigation surface and compact circular controls from 0.23.31.
🟢 **+** Settings and Credits circles continue to shrink with the compact rail and expand with the hover menu.
🩷 **✓** Credits remains hidden while the sidebar is collapsed.

## Debloat
🟢 **+** Keeps the broader current-user Windows Appx scan and expanded known removable catalogue from 0.23.31.
🩷 **✓** Windows shell, Store, runtime, security and account components remain protected.
🟢 **+** Unknown packages remain manual-review only instead of being silently removed.

## Fix
🔴 **−** The user-facing page remains named **Fix**, not Fixify.
🟢 **+** Keeps verified Windows Audio, Print Spooler and DHCP Client repair actions.

## Other tabs
🟢 **+** Keeps the expanded Ping, Maintenance, Game Profiles, GPU Drivers, Updates, Benchmarks, Backups, PC Restore and Settings tool strips introduced in 0.23.31.

## Release preservation
🩷 **✓** 0.23.30 is explicitly retained as the rollback/reference release.
🩷 **✓** 0.23.31 is also retained instead of being removed when 0.23.32 is published.
