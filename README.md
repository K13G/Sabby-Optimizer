<div align="center">

# Sabby Optimizer

**Clean Windows performance tuning, latency diagnostics, rollback protection, and automatic updates.**

![Version](https://img.shields.io/badge/stable-0.23.21-b00020?style=flat-square)
![Platform](https://img.shields.io/badge/platform-Windows%2011-111827?style=flat-square)
![Owner](https://img.shields.io/badge/owner-K13G-111827?style=flat-square)

</div>

## Latest update — 0.23.21

### Update reliability
🩷 **✓** Fixed updates being blocked by Close-to-Tray or a still-running Sabby process.  
🟢 **+** Sabby stays open under the update overlay while downloading/verifying, then closes for real only when installation is ready.  
🟢 **+** Setup now replaces the files and automatically reopens the newly installed Sabby version.  
🩷 **✓** The temporary update-close marker is cleared after restart so normal tray behavior is restored.

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
