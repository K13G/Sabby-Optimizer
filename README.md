<div align="center">

# Sabby Optimizer

**Clean Windows performance tuning, latency diagnostics, rollback protection, and automatic updates.**

![Version](https://img.shields.io/badge/stable-0.23.25-b00020?style=flat-square)
![Platform](https://img.shields.io/badge/platform-Windows%2011-111827?style=flat-square)
![Owner](https://img.shields.io/badge/owner-K13G-111827?style=flat-square)

</div>

## Latest update — 0.23.25

### Deep performance cleanup
🟢 **+** Background-batched logging removes synchronous disk writes from the UI thread.  
🟢 **+** Hardware discovery starts after the first interactive frame instead of competing with startup.  
🟢 **+** Tweaks renders 24 rich cards at a time while filters/search still cover the full catalog.  
🩷 **✓** Tweak-state UI updates are coalesced instead of repeatedly recounting/rebinding the page.  
🩷 **✓** Appearance sliders debounce full-app resource refreshes while dragging.  
🟢 **+** Settings writes are coalesced, style lists recycle containers, animated gradients are reused, and optional NVIDIA sensor polling is less aggressive.  
🔴 **−** Removed unnecessary per-item hover Storyboards from navigation.

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
