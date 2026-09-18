## Sabby Optimizer 0.23.16

### Update experience
🟢 **+** Added a full-screen dimmed update overlay so it is immediately obvious that Sabby is updating.  
🟢 **+** The updater now shows centered **Downloading**, **Verifying**, and **Installing** stages instead of disappearing with no visible feedback.  
🟢 **+** Download progress is shown as a real percentage when GitHub provides the installer size.  
🟢 **+** The overlay explains that Sabby will reopen automatically after installation.  
🩷 **✓** If an update fails before installation, the overlay shows a clear failure state, writes the error to the update-helper log, and reopens Sabby instead of silently leaving the app closed.  
🟢 **+** The overlay uses lightweight opacity-only fade animation and does not load the full Sabby UI while the installer downloads.

### Existing performance improvements retained
🟢 **+** Hardware discovery still begins immediately in parallel with the first frame.  
🟢 **+** Animated-theme global resource refresh work remains **50% lower** than 0.23.13.  
🟢 **+** The visible main Sabby window still closes immediately when the updater helper takes over.
