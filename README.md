<div align="center">

# Sabby Optimizer

**Clean Windows performance tuning, latency diagnostics, rollback protection, and automatic updates.**

![Version](https://img.shields.io/badge/stable-0.23.30-b00020?style=flat-square)
![Platform](https://img.shields.io/badge/platform-Windows%2011-111827?style=flat-square)
![Owner](https://img.shields.io/badge/owner-K13G-111827?style=flat-square)

</div>

## Latest update — 0.23.30

### Stable 0.23.27 baseline

This build intentionally uses the **0.23.27 codebase as its functional baseline**. The 0.23.28/0.23.29 shell/settings changes are not included.

The release number is 0.23.30 so the updater can correctly recognize it as newer than 0.23.28/0.23.29.

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
