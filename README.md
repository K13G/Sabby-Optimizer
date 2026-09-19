<div align="center">

# Sabby Optimizer

**Clean Windows performance tuning, latency diagnostics, rollback protection, and automatic updates.**

![Version](https://img.shields.io/badge/stable-0.23.22-b00020?style=flat-square)
![Platform](https://img.shields.io/badge/platform-Windows%2011-111827?style=flat-square)
![Owner](https://img.shields.io/badge/owner-K13G-111827?style=flat-square)

</div>

## Latest update — 0.23.22

### Installer restart hotfix
🩷 **✓** Fixed Windows Setup error **740** when Sabby tried to reopen after an install/update.  
🟢 **+** Setup now relaunches the administrator-required Sabby executable through Windows' elevated **runas** path.  
🟢 **+** App-driven updates still download and verify first, close only for replacement, then automatically reopen the newly installed build.

## Change legend

🟢 **+** Added or improved  
🔴 **−** Removed  
🩷 **✓** Bug fix

See **[CHANGELOG.md](CHANGELOG.md)** for version history.

## Automatic updates

Stable manifest:

`https://raw.githubusercontent.com/K13G/Sabby-Optimizer/main/update/SabbyOptimizer-Stable-manifest.json`

This URL stays constant; each release updates the manifest contents and points it at the newest verified installer.

## Owner

**K13G** — Developer / Owner
