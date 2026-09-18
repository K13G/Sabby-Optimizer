using PCTweaker.Core.Services;
using PCTweaker.Core.Tweaks.Handlers;
using PCTweaker.Models;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks;

public static class TweakCatalog
{
    /// <summary>
    /// Fast startup catalog. It intentionally avoids PowerShell/CIM/driver capability probes so
    /// the real workspace can render immediately. The full capability-gated catalog replaces it
    /// silently after background discovery completes.
    /// </summary>
    public static IReadOnlyList<ITweakHandler> CreateFastStartupCatalog(IAppPaths paths)
    {
        var handlers = new List<ITweakHandler>
        {
            new InMemorySelfTestTweakHandler(),
            new GameModeTweakHandler(),
            new GameCaptureTweakHandler(),
            new HighPerformancePowerPlanTweakHandler(paths),
            new WindowsAnimationsTweakHandler(),
            new TaskOffloadRegistryTweakHandler(paths),
            new PowerThrottlingRegistryTweakHandler(paths),
            new KeyboardRepeatSpeedTweakHandler(paths),
            new MouseAccelerationTweakHandler(paths)
        };

        if (ControllerGameBarShortcutTweakHandler.IsSupported())
            handlers.Add(new ControllerGameBarShortcutTweakHandler(paths));

        // Registry-policy controls are cheap to construct and make Safety & Privacy useful from
        // the first frame. Defender-specific capability probing stays in the full catalog.
        AddPrivacyAndSafetyControls(handlers, paths, includeDefenderCapabilityProbe: false);
        return handlers;
    }

    public static IReadOnlyList<ITweakHandler> CreatePhase16Catalog(IAppPaths paths, HardwareInfo hardware)
    {
        // Prime the PowerShell command capability cache in one process instead of launching a
        // separate powershell.exe for every handler's Get-Command check.
        PowerShellNetworkAccess.PrimeCommandCache(new[]
        {
            "Get-NetAdapter", "Get-NetAdapterAdvancedProperty", "Set-NetAdapterAdvancedProperty",
            "Get-NetAdapterChecksumOffload", "Set-NetAdapterChecksumOffload",
            "Get-NetAdapterPowerManagement", "Disable-NetAdapterPowerManagement", "Enable-NetAdapterPowerManagement",
            "Get-NetAdapterRsc", "Set-NetAdapterRsc", "Get-NetAdapterRss", "Set-NetAdapterRss",
            "Get-NetTCPSetting", "Set-NetTCPSetting", "Get-NetAdapterLso", "Set-NetAdapterLso",
            "Get-DnsClientServerAddress", "Set-DnsClientServerAddress", "Get-MpPreference", "Set-MpPreference"
        });

        var handlers = new List<ITweakHandler>
        {
            new InMemorySelfTestTweakHandler(),

            // Phase 8: real Windows/gaming handlers.
            new GameModeTweakHandler(),
            new GameCaptureTweakHandler(),
            new HighPerformancePowerPlanTweakHandler(paths),
            new WindowsAnimationsTweakHandler()
        };

        // Phase 9: hardware-aware options. These are added only when this PC exposes the
        // underlying Windows capability instead of showing unsupported controls as dead rows.
        if (HardwareAcceleratedGpuSchedulingTweakHandler.IsSupported(hardware))
            handlers.Add(new HardwareAcceleratedGpuSchedulingTweakHandler(paths));

        if (ProcessorBoostModeTweakHandler.IsSupported())
            handlers.Add(new ProcessorBoostModeTweakHandler(paths));

        if (ProcessorPerformanceFloorTweakHandler.IsSupported())
            handlers.Add(new ProcessorPerformanceFloorTweakHandler(paths));

        AddFpsProcessorPolicies(handlers, paths);

        // Phase 10: only expose networking controls when the Windows networking cmdlets
        // used by the handler are actually available on this PC.
        if (TcpAutoTuningTweakHandler.IsSupported())
            handlers.Add(new TcpAutoTuningTweakHandler(paths));

        if (ReceiveSideScalingTweakHandler.IsSupported())
            handlers.Add(new ReceiveSideScalingTweakHandler(paths));

        if (NetworkPowerSavingTweakHandler.IsSupported())
            handlers.Add(new NetworkPowerSavingTweakHandler(paths));

        if (ChecksumOffloadTweakHandler.IsSupported())
            handlers.Add(new ChecksumOffloadTweakHandler(paths));

        if (LargeSendOffloadTweakHandler.IsSupported())
            handlers.Add(new LargeSendOffloadTweakHandler(paths));

        if (LowLatencyRscTweakHandler.IsSupported())
            handlers.Add(new LowLatencyRscTweakHandler(paths));

        if (InterruptModerationTweakHandler.IsSupported())
            handlers.Add(new InterruptModerationTweakHandler(paths));

        if (TaskOffloadRegistryTweakHandler.IsSupported())
            handlers.Add(new TaskOffloadRegistryTweakHandler(paths));

        if (SmartDnsBenchmarkTweakHandler.IsSupported())
            handlers.Add(new SmartDnsBenchmarkTweakHandler(paths));

        if (PowerThrottlingRegistryTweakHandler.IsSupported())
            handlers.Add(new PowerThrottlingRegistryTweakHandler(paths));

        // Phase 14: hardware/driver-specific controls are only included after an explicit
        // capability check. Their handlers also implement a second compatibility gate before apply.
        if (EnergyEfficientEthernetTweakHandler.IsSupported())
            handlers.Add(new EnergyEfficientEthernetTweakHandler(paths));

        if (InputDevicePowerGuardTweakHandler.IsSupported())
            handlers.Add(new InputDevicePowerGuardTweakHandler(paths));

        // Phase 16: small, documented controls that are reversible and capability-gated.
        if (ControllerGameBarShortcutTweakHandler.IsSupported())
            handlers.Add(new ControllerGameBarShortcutTweakHandler(paths));

        if (KeyboardRepeatSpeedTweakHandler.IsSupported())
            handlers.Add(new KeyboardRepeatSpeedTweakHandler(paths));

        if (MouseAccelerationTweakHandler.IsSupported())
            handlers.Add(new MouseAccelerationTweakHandler(paths));

        if (D0PacketCoalescingTweakHandler.IsSupported())
            handlers.Add(new D0PacketCoalescingTweakHandler(paths));

        if (PcieLinkStateTweakHandler.IsSupported())
            handlers.Add(new PcieLinkStateTweakHandler(paths));

        if (UsbSelectiveSuspendTweakHandler.IsSupported())
            handlers.Add(new UsbSelectiveSuspendTweakHandler(paths));

        // Phase 22.1: expand Ethernet/network controls using standardized NDIS properties only.
        // These are deliberately NOT part of Apply Best: wake/offload behavior is situational and
        // can trade power-management functionality for a simpler always-on desktop NIC profile.
        AddAdvancedNetworkControls(handlers, paths);

        // Phase 22.1: privacy/security controls use documented Windows policies or Defender APIs.
        // They live in their own category and never get silently mixed into performance presets.
        AddPrivacyAndSafetyControls(handlers, paths);

        return handlers;
    }

    private static void AddFpsProcessorPolicies(List<ITweakHandler> handlers, IAppPaths paths)
    {
        void AddIfSupported(string id, string name, Guid setting, uint target, string shortDescription,
            string explanation, string whatChanges, TweakSafetyLevel safety, string restoreFile,
            string appliedLabel, string unitSuffix = "")
        {
            if (!ProcessorPolicyTweakHandler.IsSupported(setting)) return;
            handlers.Add(new ProcessorPolicyTweakHandler(paths, new TweakDefinition(
                id, name, shortDescription, TweakCategory.CpuPower, safety, explanation, whatChanges,
                "Undo restores the exact AC value captured before Sabby changed it.",
                false, false, true, true), setting, target, restoreFile, appliedLabel, unitSuffix));
        }

        AddIfSupported(
            "cpu.energy-performance-preference", "CPU Performance Preference",
            WindowsPowerSettingAccess.ProcessorEnergyPerformancePreference, 0,
            "Favor processor performance over energy saving while plugged in on systems that expose Windows EPP.",
            "Windows documents Processor Energy Performance Preference (PERFEPP) as a 0-100 policy where lower values favor performance. Sabby targets 0 only on AC power and keeps an exact rollback value. This can improve responsiveness on supported CPPC/HWP systems but increases power use and is not an FPS guarantee.",
            "The active plan's AC Processor Energy Performance Preference (PERFEPP) is set to 0.",
            TweakSafetyLevel.Moderate, "processor-epp-ac.txt", "Performance bias", "%");

        AddIfSupported(
            "cpu.core-parking-min", "Keep CPU Cores Unparked",
            WindowsPowerSettingAccess.CoreParkingMinimumCores, 100,
            "Set the AC core-parking minimum to 100% so Windows keeps all logical processors available for latency-sensitive workloads.",
            "Microsoft documents CPMinCores as the minimum percentage of logical processors that remain unparked. A value of 100% disables core parking. This is an advanced desktop/latency option because it increases idle power and heat and may not improve every game.",
            "The active plan's AC CPMinCores value is set to 100%.",
            TweakSafetyLevel.Advanced, "processor-core-parking-min-ac.txt", "All cores available", "%");

        AddIfSupported(
            "cpu.performance-increase-policy", "Fast CPU Ramp-Up",
            WindowsPowerSettingAccess.ProcessorPerformanceIncreasePolicy, 3,
            "Use Windows' responsiveness-oriented processor performance increase policy when the platform exposes it.",
            "Windows documents PERFINCPOL value 3 as selecting the ideal processor performance state optimized for responsiveness. It does not apply to every autonomous performance-state implementation, so Sabby exposes it only when Windows reports the setting on the active plan.",
            "The active plan's AC Processor Performance Increase Policy (PERFINCPOL) is set to 3 (optimized for responsiveness).",
            TweakSafetyLevel.Moderate, "processor-increase-policy-ac.txt", "Responsive ramp");

        AddIfSupported(
            "cpu.boost-policy", "CPU Boost Policy",
            WindowsPowerSettingAccess.ProcessorPerformanceBoostPolicy, 100,
            "Use the maximum Windows processor performance boost policy while plugged in where the legacy policy is exposed.",
            "Microsoft documents PERFBOOSTPOL as a 0-100 processor boost policy. Sabby targets 100 only when Windows exposes the setting, keeps the original value, and treats it as a performance-biased option rather than a guaranteed FPS increase.",
            "The active plan's AC Processor Performance Boost Policy (PERFBOOSTPOL) is set to 100%.",
            TweakSafetyLevel.Moderate, "processor-boost-policy-ac.txt", "100% boost policy", "%");
    }

    private static void AddAdvancedNetworkControls(List<ITweakHandler> handlers, IAppPaths paths)
    {
        AddStandardNetworkProperty(handlers, paths,
            "network.wake-magic-packet", "Wake on Magic Packet", "*WakeOnMagicPacket",
            "Disable magic-packet wake on supported active physical adapters when Wake-on-LAN is not wanted.",
            "This is a power/wake behavior control, not a ping tweak. Microsoft defines *WakeOnMagicPacket as the standardized NDIS property that permits a magic packet to wake the PC.",
            "The standardized *WakeOnMagicPacket property is set to Disabled (0) only on connected physical adapters that expose it.",
            "Undo restores each adapter's exact saved value.",
            TweakSafetyLevel.Moderate, "wake-magic-packet.json", "Magic-packet wake off", "Wake-on-LAN enabled");

        AddStandardNetworkProperty(handlers, paths,
            "network.wake-pattern", "Wake on Pattern Match", "*WakeOnPattern",
            "Disable packet-pattern wake on supported active physical adapters for a stricter desktop wake profile.",
            "Microsoft defines *WakeOnPattern as the standardized NDIS property that lets matching network traffic wake a sleeping PC. Disabling it can stop unwanted network-triggered wakes but removes that wake capability.",
            "The standardized *WakeOnPattern property is set to Disabled (0) on supported connected physical adapters.",
            "Undo restores each adapter's exact saved value.",
            TweakSafetyLevel.Moderate, "wake-pattern.json", "Pattern wake off", "Pattern wake enabled");

        AddStandardNetworkProperty(handlers, paths,
            "network.arp-sleep-offload", "ARP Sleep Offload", "*PMARPOffload",
            "Disable ARP offload during sleep on adapters that expose the standardized power-management property.",
            "ARP sleep offload exists so the NIC can answer selected ARP traffic while the system sleeps. This does not improve active-game latency; it is exposed for users who want explicit NIC sleep-state behavior.",
            "The standardized *PMARPOffload property is set to Disabled (0) on supported connected physical adapters.",
            "Undo restores each adapter's exact saved value.",
            TweakSafetyLevel.Advanced, "arp-sleep-offload.json", "ARP sleep offload off", "ARP sleep offload enabled");

        AddStandardNetworkProperty(handlers, paths,
            "network.ns-sleep-offload", "IPv6 NS Sleep Offload", "*PMNSOffload",
            "Disable IPv6 Neighbor Solicitation offload during sleep on adapters that expose the standardized property.",
            "NS offload allows a sleeping system's NIC to answer selected IPv6 Neighbor Solicitation traffic. It is a sleep/power feature rather than an FPS or active latency optimization.",
            "The standardized *PMNSOffload property is set to Disabled (0) on supported connected physical adapters.",
            "Undo restores each adapter's exact saved value.",
            TweakSafetyLevel.Advanced, "ns-sleep-offload.json", "NS sleep offload off", "NS sleep offload enabled");

        AddStandardNetworkProperty(handlers, paths,
            "network.modern-standby-wol", "Modern Standby Wake-on-LAN", "*ModernStandbyWoLMagicPacket",
            "Disable magic-packet wake during Modern Standby when the active NIC and driver expose that NDIS option.",
            "Microsoft defines *ModernStandbyWoLMagicPacket for NDIS 6.60+ adapters. This changes S0ix wake behavior only; it is useful for unwanted-wake control, not as a gaming-performance claim.",
            "The standardized *ModernStandbyWoLMagicPacket value is set to Disabled (0) on supported connected physical adapters.",
            "Undo restores each adapter's exact saved value.",
            TweakSafetyLevel.Advanced, "modern-standby-wol.json", "Modern-standby wake off", "Modern-standby wake enabled");

        AddStandardNetworkProperty(handlers, paths,
            "network.device-sleep-disconnect", "Sleep on Cable Disconnect", "*DeviceSleepOnDisconnect",
            "Keep supported Ethernet adapters out of their disconnect-triggered low-power state while Windows is running.",
            "Microsoft defines *DeviceSleepOnDisconnect for NDIS miniports: enabled adapters may enter a low-power state after media disconnect and wake again when link returns. Disabling it is an explicit power-behavior choice, not a guaranteed latency improvement.",
            "The standardized *DeviceSleepOnDisconnect property is set to Disabled (0) on supported connected physical adapters.",
            "Undo restores each adapter's exact saved value.",
            TweakSafetyLevel.Moderate, "device-sleep-disconnect.json", "Disconnect sleep off", "Disconnect sleep enabled");

        AddStandardNetworkProperty(handlers, paths,
            "network.flow-control", "Ethernet Flow Control", "*FlowControl",
            "Disable standardized transmit/receive Ethernet flow control when the active adapter exposes the NDIS property.",
            "Microsoft defines *FlowControl value 0 as Tx and Rx disabled. This can avoid pause-frame behavior, but disabling flow control can increase packet drops under congestion and is not universally faster, so Sabby keeps it Advanced and outside Apply Best.",
            "The standardized *FlowControl property is set to Tx & Rx Disabled (0) only on supported connected physical adapters.",
            "Undo restores each adapter's exact saved value.",
            TweakSafetyLevel.Advanced, "flow-control.json", "Flow control off", "Flow control enabled/custom");

        AddStandardNetworkProperty(handlers, paths,
            "network.priority-vlan", "Packet Priority & VLAN Tags", "*PriorityVLANTag",
            "Disable standardized 802.1p packet-priority and 802.1Q VLAN tagging on adapters where those features are not used.",
            "Microsoft defines *PriorityVLANTag value 0 as packet priority and VLAN disabled. This can break networks that intentionally use VLANs or 802.1p/QoS tagging, so it is an Advanced opt-in control and never part of Apply Best.",
            "The standardized *PriorityVLANTag property is set to Packet Priority & VLAN Disabled (0) only on supported connected physical adapters.",
            "Undo restores each adapter's exact saved value.",
            TweakSafetyLevel.Advanced, "priority-vlan.json", "Priority/VLAN tags off", "Priority/VLAN enabled/custom");
    }

    private static void AddStandardNetworkProperty(
        List<ITweakHandler> handlers,
        IAppPaths paths,
        string id,
        string name,
        string keyword,
        string shortDescription,
        string explanation,
        string whatChanges,
        string undoDescription,
        TweakSafetyLevel safety,
        string restoreFile,
        string appliedLabel,
        string notAppliedLabel)
    {
        if (!StandardNetAdapterPropertyTweakHandler.IsSupported(keyword)) return;
        var definition = new TweakDefinition(
            id, name, shortDescription, TweakCategory.Network, safety, explanation, whatChanges,
            undoDescription, true, false, true, true);
        handlers.Add(new StandardNetAdapterPropertyTweakHandler(
            paths, definition, keyword, "0", restoreFile, appliedLabel, notAppliedLabel));
    }

    private static void AddPrivacyAndSafetyControls(List<ITweakHandler> handlers, IAppPaths paths, bool includeDefenderCapabilityProbe = true)
    {
        handlers.Add(Policy(paths,
            "privacy.advertising-id", "Advertising ID", "Turn off the Windows advertising ID through the documented computer policy.",
            TweakSafetyLevel.Safe,
            "Windows exposes a policy that turns off the advertising ID so apps cannot use it for cross-app advertising experiences.",
            @"HKLM\Software\Policies\Microsoft\Windows\AdvertisingInfo\DisabledByGroupPolicy = 1.",
            "Undo restores the exact previous policy value or removes it if it did not exist.", true, false,
            "advertising-id.json", "Advertising ID off", "Advertising ID allowed",
            new RegistryPolicyBundleTweakHandler.DwordTarget(RegistryPolicyBundleTweakHandler.PolicyHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1)));

        handlers.Add(Policy(paths,
            "privacy.tailored-experiences", "Tailored Diagnostic Experiences", "Stop Windows from using diagnostic data to tailor tips, recommendations, and related experiences.",
            TweakSafetyLevel.Safe,
            "Microsoft documents this user policy as 'Do not use diagnostic data for tailored experiences.' It changes personalization behavior without disabling Windows security telemetry or Defender.",
            @"HKCU\Software\Policies\Microsoft\Windows\CloudContent\DisableTailoredExperiencesWithDiagnosticData = 1.",
            "Undo restores the exact previous user policy state.", false, false,
            "tailored-experiences.json", "Tailoring off", "Tailoring allowed",
            new RegistryPolicyBundleTweakHandler.DwordTarget(RegistryPolicyBundleTweakHandler.PolicyHive.CurrentUser, @"Software\Policies\Microsoft\Windows\CloudContent", "DisableTailoredExperiencesWithDiagnosticData", 1)));

        handlers.Add(Policy(paths,
            "privacy.third-party-suggestions", "Third-Party Windows Suggestions", "Disable third-party app/content suggestions in Windows Spotlight experiences.",
            TweakSafetyLevel.Safe,
            "Microsoft exposes a user policy that prevents third-party software publishers from supplying suggestions through Windows Spotlight surfaces while leaving Windows itself functional.",
            @"HKCU\Software\Policies\Microsoft\Windows\CloudContent\DisableThirdPartySuggestions = 1.",
            "Undo restores the previous policy value exactly.", false, false,
            "third-party-suggestions.json", "Third-party suggestions off", "Suggestions allowed",
            new RegistryPolicyBundleTweakHandler.DwordTarget(RegistryPolicyBundleTweakHandler.PolicyHive.CurrentUser, @"Software\Policies\Microsoft\Windows\CloudContent", "DisableThirdPartySuggestions", 1)));

        handlers.Add(Policy(paths,
            "privacy.windows-spotlight", "Windows Spotlight Content", "Turn off Windows Spotlight content surfaces for a quieter, less promotional Windows experience.",
            TweakSafetyLevel.Moderate,
            "The Windows Cloud Content policy can disable Windows Spotlight features. This may also remove rotating/suggested content you intentionally use, so Sabby treats it as a preference rather than a performance optimization.",
            @"HKCU\Software\Policies\Microsoft\Windows\CloudContent\DisableWindowsSpotlightFeatures = 1.",
            "Undo restores the previous Spotlight policy value.", false, false,
            "windows-spotlight.json", "Spotlight content off", "Spotlight content allowed",
            new RegistryPolicyBundleTweakHandler.DwordTarget(RegistryPolicyBundleTweakHandler.PolicyHive.CurrentUser, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsSpotlightFeatures", 1)));

        handlers.Add(Policy(paths,
            "privacy.activity-history", "Activity History Sharing", "Disable publishing/uploading Windows user activities and the activity feed through documented OS policies.",
            TweakSafetyLevel.Moderate,
            "Windows activity policies control whether activities can be published, uploaded, and used in the activity feed. Disabling them improves privacy but can remove cross-device/shared activity experiences.",
            @"HKLM\Software\Policies\Microsoft\Windows\System: EnableActivityFeed=0, PublishUserActivities=0, UploadUserActivities=0.",
            "Undo restores all three exact prior policy values.", true, false,
            "activity-history.json", "Activity sharing off", "Activity sharing allowed",
            new RegistryPolicyBundleTweakHandler.DwordTarget(RegistryPolicyBundleTweakHandler.PolicyHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0),
            new RegistryPolicyBundleTweakHandler.DwordTarget(RegistryPolicyBundleTweakHandler.PolicyHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
            new RegistryPolicyBundleTweakHandler.DwordTarget(RegistryPolicyBundleTweakHandler.PolicyHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0)));

        handlers.Add(Policy(paths,
            "privacy.delivery-optimization-p2p", "Delivery Optimization Peer Sharing", "Use Delivery Optimization HTTP-only mode so Windows Update/Store content is not exchanged with peer PCs.",
            TweakSafetyLevel.Moderate,
            "Microsoft documents Delivery Optimization download mode 0 as HTTP-only: peer-to-peer caching is disabled while normal Microsoft/CDN downloads continue. This is a privacy/network preference and can increase internet bandwidth use on multi-PC networks.",
            @"HKLM\Software\Policies\Microsoft\Windows\DeliveryOptimization\DODownloadMode = 0.",
            "Undo restores the exact previous Delivery Optimization policy state.", true, false,
            "delivery-optimization-p2p.json", "Peer sharing off", "Peer sharing allowed",
            new RegistryPolicyBundleTweakHandler.DwordTarget(RegistryPolicyBundleTweakHandler.PolicyHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0)));

        handlers.Add(Policy(paths,
            "privacy.file-search-history", "File Explorer Search History", "Stop File Explorer from storing and suggesting recent search-box entries.",
            TweakSafetyLevel.Safe,
            "Microsoft documents DisableSearchBoxSuggestions as a user policy that removes recent File Explorer search suggestions and prevents those entries being stored for later suggestions.",
            @"HKCU\Software\Policies\Microsoft\Windows\Explorer\DisableSearchBoxSuggestions = 1.",
            "Undo restores the previous File Explorer search-history policy.", false, false,
            "file-search-history.json", "Search history off", "Search history allowed",
            new RegistryPolicyBundleTweakHandler.DwordTarget(RegistryPolicyBundleTweakHandler.PolicyHive.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1)));

        handlers.Add(Policy(paths,
            "security.llmnr", "Disable LLMNR", "Disable Link-Local Multicast Name Resolution on all adapters using the documented DNS Client policy.",
            TweakSafetyLevel.Moderate,
            "LLMNR is a secondary local-link name-resolution protocol. Disabling it reduces reliance on multicast name resolution, but legacy/local environments that depend on LLMNR can lose name resolution for those hosts.",
            @"HKLM\Software\Policies\Microsoft\Windows NT\DNSClient\EnableMulticast = 0.",
            "Undo restores the exact previous LLMNR policy value.", true, false,
            "llmnr.json", "LLMNR off", "LLMNR allowed",
            new RegistryPolicyBundleTweakHandler.DwordTarget(RegistryPolicyBundleTweakHandler.PolicyHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast", 0)));

        handlers.Add(Policy(paths,
            "security.remote-assistance", "Solicited Remote Assistance", "Disable Windows Solicited Remote Assistance unless you intentionally use it for remote help.",
            TweakSafetyLevel.Moderate,
            "Microsoft's Remote Assistance policy controls whether users can request a helper connection. Disabling it reduces an unused remote-access surface but prevents the built-in Remote Assistance invitation workflow.",
            @"HKLM\Software\Policies\Microsoft\Windows NT\Terminal Services\fAllowToGetHelp = 0.",
            "Undo restores the exact previous Remote Assistance policy state.", true, false,
            "remote-assistance.json", "Remote Assistance off", "Remote Assistance allowed",
            new RegistryPolicyBundleTweakHandler.DwordTarget(RegistryPolicyBundleTweakHandler.PolicyHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services", "fAllowToGetHelp", 0)));

        handlers.Add(Policy(paths,
            "security.voice-above-lock", "Voice Activation Above Lock", "Force-deny app voice activation while the PC is locked.",
            TweakSafetyLevel.Safe,
            "Windows App Privacy policy can prevent apps from activating with voice while the system is locked. This reduces lock-screen listening surfaces but can disable hands-free voice features you intentionally use from the lock screen.",
            @"HKLM\Software\Policies\Microsoft\Windows\AppPrivacy\LetAppsActivateWithVoiceAboveLock = 2 (Force Deny).",
            "Undo restores the exact previous app-privacy policy value.", true, false,
            "voice-above-lock.json", "Lock-screen voice denied", "Lock-screen voice allowed",
            new RegistryPolicyBundleTweakHandler.DwordTarget(RegistryPolicyBundleTweakHandler.PolicyHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsActivateWithVoiceAboveLock", 2)));

        handlers.Add(Policy(paths,
            "security.autoplay-all-drives", "AutoPlay/AutoRun Hardening", "Disable AutoPlay across drive types through the Windows Explorer policy value.",
            TweakSafetyLevel.Advanced,
            "Disabling AutoPlay reduces automatic handling of inserted media, which can be useful for security-conscious systems. It also removes convenience behavior for optical/removable media and some specialty hardware, so Sabby keeps it Advanced.",
            @"HKLM\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\NoDriveTypeAutoRun = 0xFF.",
            "Undo restores the exact previous NoDriveTypeAutoRun value or removes the override if it was absent.", true, false,
            "autoplay-all-drives.json", "AutoPlay off", "AutoPlay policy not hardened",
            new RegistryPolicyBundleTweakHandler.DwordTarget(RegistryPolicyBundleTweakHandler.PolicyHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDriveTypeAutoRun", 0xFF)));

        handlers.Add(Policy(paths,
            "privacy.clipboard-history", "Clipboard History", "Disable Win+V clipboard history so copied content is not retained in the clipboard-history store.",
            TweakSafetyLevel.Moderate,
            "Microsoft's OS policy can disallow clipboard history. This is useful when copied content is sensitive, but it intentionally removes the Win+V history workflow.",
            @"HKLM\Software\Policies\Microsoft\Windows\System\AllowClipboardHistory = 0.",
            "Undo restores the exact previous clipboard-history policy value.", true, false,
            "clipboard-history.json", "Clipboard history off", "Clipboard history allowed",
            new RegistryPolicyBundleTweakHandler.DwordTarget(RegistryPolicyBundleTweakHandler.PolicyHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "AllowClipboardHistory", 0)));

        handlers.Add(Policy(paths,
            "privacy.cross-device-clipboard", "Cross-Device Clipboard Sync", "Prevent Windows clipboard contents from synchronizing to other devices.",
            TweakSafetyLevel.Safe,
            "Microsoft documents a device policy that prevents clipboard contents from being shared to other devices signed into the same Microsoft or Entra account. Local copy/paste remains available.",
            @"HKLM\Software\Policies\Microsoft\Windows\System\AllowCrossDeviceClipboard = 0.",
            "Undo restores the exact previous cross-device clipboard policy value.", true, false,
            "cross-device-clipboard.json", "Clipboard sync off", "Clipboard sync allowed",
            new RegistryPolicyBundleTweakHandler.DwordTarget(RegistryPolicyBundleTweakHandler.PolicyHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "AllowCrossDeviceClipboard", 0)));

        handlers.Add(Policy(paths,
            "security.remote-desktop", "Incoming Remote Desktop", "Deny new incoming Remote Desktop connections unless you intentionally use this PC as an RDP host.",
            TweakSafetyLevel.Advanced,
            "Windows uses fDenyTSConnections to determine whether new Remote Desktop connections are accepted. This is a hardening option for PCs that do not need RDP hosting; domain policy can override the local value.",
            @"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server\fDenyTSConnections = 1.",
            "Undo restores the exact previous Remote Desktop connection value.", true, false,
            "remote-desktop.json", "Incoming RDP denied", "Incoming RDP may be allowed",
            new RegistryPolicyBundleTweakHandler.DwordTarget(RegistryPolicyBundleTweakHandler.PolicyHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", 1)));

        handlers.Add(Policy(paths,
            "security.smb-insecure-guest", "SMB Insecure Guest Logons", "Reject unauthenticated SMB guest logons through the documented Lanman Workstation policy.",
            TweakSafetyLevel.Advanced,
            "Microsoft recommends disabling insecure SMB guest logons because they do not provide normal authentication protections and also prevent SMB signing/encryption from protecting the session. Some older consumer NAS devices rely on guest access, so this remains Advanced.",
            @"HKLM\Software\Policies\Microsoft\Windows\LanmanWorkstation\AllowInsecureGuestAuth = 0.",
            "Undo restores the exact previous SMB guest-logon policy value.", true, false,
            "smb-insecure-guest.json", "Insecure SMB guest denied", "SMB guest policy not hardened",
            new RegistryPolicyBundleTweakHandler.DwordTarget(RegistryPolicyBundleTweakHandler.PolicyHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation", "AllowInsecureGuestAuth", 0)));

        if (includeDefenderCapabilityProbe && DefenderPuaProtectionTweakHandler.IsSupported())
            handlers.Add(new DefenderPuaProtectionTweakHandler(paths));
    }

    private static RegistryPolicyBundleTweakHandler Policy(
        IAppPaths paths,
        string id,
        string name,
        string shortDescription,
        TweakSafetyLevel safety,
        string explanation,
        string whatChanges,
        string undoDescription,
        bool requiresAdministrator,
        bool requiresRestart,
        string restoreFile,
        string appliedLabel,
        string notAppliedLabel,
        params RegistryPolicyBundleTweakHandler.DwordTarget[] targets)
    {
        var definition = new TweakDefinition(
            id, name, shortDescription, TweakCategory.PrivacySafety, safety, explanation, whatChanges,
            undoDescription, requiresAdministrator, requiresRestart, true, true);
        return new RegistryPolicyBundleTweakHandler(
            paths, definition, restoreFile, appliedLabel, notAppliedLabel, targets);
    }

    public static IReadOnlyList<ITweakHandler> CreatePhase14Catalog(IAppPaths paths, HardwareInfo hardware) =>
        CreatePhase16Catalog(paths, hardware);

    public static IReadOnlyList<ITweakHandler> CreatePhase12Catalog(IAppPaths paths, HardwareInfo hardware) =>
        CreatePhase16Catalog(paths, hardware);

    public static IReadOnlyList<ITweakHandler> CreatePhase10Catalog(IAppPaths paths, HardwareInfo hardware) =>
        CreatePhase16Catalog(paths, hardware);

    // Compatibility aliases for older internal call sites. New startup code uses Phase 10.
    public static IReadOnlyList<ITweakHandler> CreatePhase9Catalog(IAppPaths paths, HardwareInfo hardware) =>
        CreatePhase16Catalog(paths, hardware);

    public static IReadOnlyList<ITweakHandler> CreatePhase8Catalog(IAppPaths paths, HardwareInfo hardware) =>
        CreatePhase16Catalog(paths, hardware);

    public static IReadOnlyList<ITweakHandler> CreatePhase3Catalog(IAppPaths paths, HardwareInfo hardware) =>
        CreatePhase16Catalog(paths, hardware);

    private static UnavailableTweakHandler Placeholder(
        string id,
        string name,
        string shortDescription,
        TweakCategory category,
        TweakSafetyLevel safety,
        string explanation,
        string whatChanges,
        string undoDescription,
        bool requiresAdministrator,
        bool requiresRestart,
        string reason)
    {
        var definition = new TweakDefinition(
            id,
            name,
            shortDescription,
            category,
            safety,
            explanation,
            whatChanges,
            undoDescription,
            requiresAdministrator,
            requiresRestart,
            true,
            true);

        return new UnavailableTweakHandler(definition, reason);
    }
}
