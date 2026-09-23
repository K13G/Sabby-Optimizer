<div align="center">

# Sabby Optimizer

**Clean Windows performance tuning, latency diagnostics, rollback protection, and automatic updates.**

![Version](https://img.shields.io/badge/stable-0.23.32-b00020?style=flat-square)
![Platform](https://img.shields.io/badge/platform-Windows%2011-111827?style=flat-square)
![Owner](https://img.shields.io/badge/owner-K13G-111827?style=flat-square)

</div>

## Latest update — 0.23.32

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


## Automatic updates

Stable manifest:

`https://raw.githubusercontent.com/K13G/Sabby-Optimizer/main/update/SabbyOptimizer-Stable-manifest.json`

This URL stays constant; each release updates the manifest contents and points it at the newest verified installer.

## Owner

**K13G** — Developer / Owner

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
