using System.Windows.Input;
using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Services;
using PCTweaker.Core.Tweaks;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.ViewModels;

public sealed class TweakCardViewModel : ViewModelBase
{
    private readonly ITweakEngine _engine;
    private TweakDetectionResult _state = TweakDetectionResult.Unknown();
    private bool _isBusy;
    private bool _isExplanationOpen;
    private string _operationMessage = "Ready — Sabby will verify this control when you use it.";
    private string _verificationText = "Read-back runs on demand.";
    private bool _verificationSucceeded;
    private string _compatibilityText = "Compatibility is checked on demand.";
    private bool _compatibilityPassed = true;

    public TweakDefinition Definition { get; }

    public string Name => Definition.Name;
    public string Description => Definition.ShortDescription;
    public string CategoryLabel => Definition.Category switch
    {
        TweakCategory.CpuPower => "CPU & POWER",
        TweakCategory.Gaming => "GAMING",
        TweakCategory.Graphics => "GRAPHICS",
        TweakCategory.Network => "NETWORK",
        TweakCategory.Windows => "WINDOWS",
        TweakCategory.PrivacySafety => "SAFETY & PRIVACY",
        TweakCategory.Services => "SERVICES",
        TweakCategory.Startup => "STARTUP",
        TweakCategory.Cleanup => "CLEANUP",
        TweakCategory.Advanced => "ADVANCED",
        _ => "SYSTEM"
    };

    public string SafetyLabel => Definition.SafetyLevel switch
    {
        TweakSafetyLevel.Safe => "SAFE",
        TweakSafetyLevel.Moderate => "MODERATE",
        TweakSafetyLevel.Advanced => "ADVANCED",
        _ => "UNKNOWN"
    };

    public string RequirementsText
    {
        get
        {
            var parts = new List<string>();
            if (Definition.RequiresAdministrator)
                parts.Add("Admin");
            if (Definition.RequiresRestart)
                parts.Add("Restart");
            if (!Definition.IsReversible)
                parts.Add("No automatic undo");
            return parts.Count == 0 ? "No special requirements" : string.Join(" • ", parts);
        }
    }

    public string StateDisplay => _state.State == TweakStateKind.Unknown ? "READY" : _state.DisplayText;
    public string StateDetail => _state.State == TweakStateKind.Unknown ? "Checked on demand — no startup scan required." : _state.Detail;
    public bool CanApply => !_isBusy && _state.CanApply;
    public bool CanUndo => !_isBusy && _state.CanUndo && Definition.IsReversible;
    public bool IsActivated => _state.State == TweakStateKind.Applied;
    public bool IsStatePending => _state.State == TweakStateKind.Unknown;
    // Keep the control interactive even when Sabby cannot safely reverse a setting that was
    // already enabled before Sabby saw it. Clicking then explains the protected state instead of
    // presenting a mysterious grey switch.
    public bool CanToggle => !_isBusy;
    public bool IsProtectedActive => IsActivated && !CanUndo;
    public string ToggleActionText => IsBusy
        ? "Checking…"
        : _state.State == TweakStateKind.Unknown
            ? "Ready"
            : IsActivated
                ? (CanUndo ? "Deactivate" : "Already on")
                : _state.State is TweakStateKind.Unavailable or TweakStateKind.Error
                    ? "Not supported"
                    : (CanApply ? "Activate" : "Checked");
    public string ToggleHint => _state.State == TweakStateKind.Unknown
        ? "Click once. Sabby will detect compatibility and current state, then safely apply when appropriate."
        : IsProtectedActive
            ? "This recommended state was already active before Sabby opened. Nothing needs to be applied. Sabby will not invent a rollback value for a setting it did not change; Info shows the exact Windows control."
            : CanApply || CanUndo
                ? "Click to change this setting with read-back verification."
                : "This control is not currently changeable on this PC. Click for the detected reason.";
    public string VerificationGuide => GetVerificationGuide(Definition.Id, IsActivated);

    // Evidence/impact meter: deliberately conservative. A low score means the tweak is
    // situational or mostly cosmetic; a high score means the underlying Windows behavior
    // has strong technical justification. It is not an FPS guarantee.
    public int EvidenceScore => GetEvidenceScore(Definition.Id);
    public int IntroducedOrder => GetIntroducedOrder(Definition.Id);
    public string EvidenceLabel => EvidenceScore switch
    {
        >= 100 => "BEST",
        >= 75 => "GOOD",
        >= 50 => "MIXED",
        >= 25 => "LOW IMPACT",
        _ => "PLACEBO"
    };
    public double EvidenceGreenWidth => 112d * EvidenceScore / 100d;
    public double EvidenceRedWidth => 112d - EvidenceGreenWidth;
    public double EvidenceMarkerOffset => Math.Clamp((112d * EvidenceScore / 100d) - 1d, 0d, 110d);

    public bool IsRecommendationBlocked =>
        Definition.Id == "power.active-plan" &&
        _state.State == TweakStateKind.Custom &&
        !CanApply;

    public string RecommendationBlockedText => IsRecommendationBlocked
        ? "SKIP — your active plan is already performance-tuned. Switching to the built-in plan is not recommended."
        : string.Empty;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;

            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanToggle));
            OnPropertyChanged(nameof(ToggleActionText));
            OnPropertyChanged(nameof(ToggleHint));
            RefreshCommand.RaiseCanExecuteChanged();
            ApplyCommand.RaiseCanExecuteChanged();
            UndoCommand.RaiseCanExecuteChanged();
            ToggleActivationCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsExplanationOpen
    {
        get => _isExplanationOpen;
        private set => SetProperty(ref _isExplanationOpen, value);
    }

    public string OperationMessage
    {
        get => _operationMessage;
        private set => SetProperty(ref _operationMessage, value);
    }

    public string ExplainButtonText => IsExplanationOpen ? "Hide details" : "Explain";

    public string VerificationText
    {
        get => _verificationText;
        private set => SetProperty(ref _verificationText, value);
    }

    public bool VerificationSucceeded
    {
        get => _verificationSucceeded;
        private set => SetProperty(ref _verificationSucceeded, value);
    }

    public string CompatibilityText
    {
        get => _compatibilityText;
        private set => SetProperty(ref _compatibilityText, value);
    }

    public bool CompatibilityPassed
    {
        get => _compatibilityPassed;
        private set => SetProperty(ref _compatibilityPassed, value);
    }


    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ApplyCommand { get; }
    public AsyncRelayCommand UndoCommand { get; }
    public AsyncRelayCommand ToggleActivationCommand { get; }
    public RelayCommand ToggleExplanationCommand { get; }

    public TweakExplanation Explanation { get; }

    public TweakCardViewModel(ITweakEngine engine, TweakDefinition definition)
    {
        _engine = engine;
        Definition = definition;
        Explanation = engine.Explain(definition.Id);

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanApply);
        UndoCommand = new AsyncRelayCommand(UndoAsync, () => CanUndo);
        ToggleActivationCommand = new AsyncRelayCommand(ToggleActivationAsync, () => !IsBusy);
        ToggleExplanationCommand = new RelayCommand(() =>
        {
            IsExplanationOpen = !IsExplanationOpen;
            OnPropertyChanged(nameof(ExplainButtonText));
        });
    }

    public Task ApplyRecommendedAsync() => CanApply ? ApplyAsync() : Task.CompletedTask;

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            OperationMessage = "Detecting current state…";
            UpdateState(await Task.Run(async () => await _engine.DetectAsync(Definition.Id).ConfigureAwait(false)));
            var compatibility = await Task.Run(async () => await _engine.CheckCompatibilityAsync(Definition.Id, true).ConfigureAwait(false));
            CompatibilityPassed = compatibility.IsCompatible;
            CompatibilityText = compatibility.IsCompatible
                ? (string.IsNullOrWhiteSpace(compatibility.Warning) ? "✓ Compatibility check passed" : $"⚠ {compatibility.Warning}")
                : $"✕ {compatibility.Message}";
            if (_state.State == TweakStateKind.Unavailable)
            {
                OperationMessage = "Unavailable on this PC or in the current Windows/network configuration.";
                VerificationSucceeded = false;
                VerificationText = "Not verifiable on this PC.";
            }
            else if (_state.State == TweakStateKind.Error)
            {
                OperationMessage = "State detection failed.";
                VerificationSucceeded = false;
                VerificationText = "✕ Read-back failed.";
            }
            else
            {
                OperationMessage = compatibility.IsCompatible
                    ? "State detected and verified."
                    : $"Compatibility blocked: {compatibility.Message}";
                VerificationSucceeded = true;
                VerificationText = $"✓ READ-BACK VERIFIED • {StateDisplay} • {DateTime.Now:h:mm:ss tt}";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyAsync()
    {
        IsBusy = true;
        try
        {
            OperationMessage = "Applying safely…";
            var result = await Task.Run(async () => await _engine.ApplyAsync(Definition.Id).ConfigureAwait(false));
            if (result.VerifiedState is not null)
                UpdateState(result.VerifiedState);
            else
                UpdateState(await Task.Run(async () => await _engine.DetectAsync(Definition.Id).ConfigureAwait(false)));

            OperationMessage = result.RequiresElevation
                ? "Administrator permission is required; nothing was partially applied."
                : result.Message;
            VerificationSucceeded = result.Success && result.VerifiedState is not null && _state.State == TweakStateKind.Applied;
            VerificationText = VerificationSucceeded
                ? $"✓ APPLY VERIFIED • Windows read-back: {StateDisplay} • {DateTime.Now:h:mm:ss tt}"
                : $"✕ APPLY NOT VERIFIED • {result.Message}";
            UiNotificationHub.Publish(Name, VerificationSucceeded ? $"Applied and verified: {StateDisplay}" : $"Could not verify the change: {result.Message}", VerificationSucceeded ? UiNotificationKind.Success : UiNotificationKind.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UndoAsync()
    {
        IsBusy = true;
        try
        {
            OperationMessage = "Undoing and verifying…";
            var result = await Task.Run(async () => await _engine.UndoAsync(Definition.Id).ConfigureAwait(false));
            if (result.VerifiedState is not null)
                UpdateState(result.VerifiedState);
            else
                UpdateState(await Task.Run(async () => await _engine.DetectAsync(Definition.Id).ConfigureAwait(false)));

            OperationMessage = result.Message;
            VerificationSucceeded = result.Success && result.VerifiedState is not null && _state.State != TweakStateKind.Applied;
            VerificationText = VerificationSucceeded
                ? $"✓ RESTORE VERIFIED • Windows read-back: {StateDisplay} • {DateTime.Now:h:mm:ss tt}"
                : $"✕ RESTORE NOT VERIFIED • {result.Message}";
            UiNotificationHub.Publish(Name, VerificationSucceeded ? $"Restored and verified: {StateDisplay}" : $"Restore could not be verified: {result.Message}", VerificationSucceeded ? UiNotificationKind.Success : UiNotificationKind.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ToggleActivationAsync()
    {
        // Unknown no longer means "Unavailable". The first click performs the targeted read-back
        // for this one control, then continues with the requested action when it is safe to do so.
        if (_state.State == TweakStateKind.Unknown)
        {
            await RefreshAsync();
            if (_state.State is TweakStateKind.Error or TweakStateKind.Unavailable)
                return;
        }

        if (IsActivated && CanUndo)
        {
            await UndoAsync();
            return;
        }

        if (CanApply)
        {
            await ApplyAsync();
            return;
        }

        // Do not grey out a detected-on switch with no explanation. A pre-existing state may not
        // have a Sabby rollback record, and guessing the old value would violate rollback safety.
        var message = IsProtectedActive
            ? "Already optimized before Sabby opened. Sabby did not change this setting, so it will not invent a rollback value. Open Info for the exact Windows control if you want to change it manually."
            : string.IsNullOrWhiteSpace(CompatibilityText) ? "This setting is not changeable on this PC." : CompatibilityText;
        OperationMessage = message;
        UiNotificationHub.Publish(Name, message, UiNotificationKind.Warning);
    }

    private static string GetVerificationGuide(string id, bool activated) => id switch
    {
        "gaming.game-mode" => activated
            ? @"VERIFY: Settings > Gaming > Game Mode should be On. Registry: HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled = DWORD 1."
            : @"VERIFY: Settings > Gaming > Game Mode should no longer be Sabby's forced On state. Registry: HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled is restored/0.",
        "gaming.capture" => activated
            ? @"VERIFY: Registry HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled = 0 AND HKCU\System\GameConfigStore\GameDVR_Enabled = 0."
            : @"VERIFY: Those two Game DVR values are restored to the values Sabby recorded before applying the tweak.",
        "graphics.hags" => activated
            ? @"VERIFY: HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode = DWORD 2. Windows restart is required before the scheduling change is fully active."
            : @"VERIFY: HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode is restored to its previous value (or removed if it did not exist).",
        "system.power-throttling" => activated
            ? @"VERIFY: HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling\PowerThrottlingOff = DWORD 1."
            : @"VERIFY: PowerThrottlingOff is restored to the exact prior value or removed if it did not exist.",
        "network.task-offload" => activated
            ? @"VERIFY: HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\DisableTaskOffload = DWORD 0."
            : @"VERIFY: DisableTaskOffload is restored to the exact prior value or removed if it did not exist.",
        "power.active-plan" => @"VERIFY: Open Control Panel > Power Options, or run: powercfg /getactivescheme. Sabby preserves custom/Ultimate plans and only changes known built-in plans.",
        "cpu.boost-mode" => activated
            ? @"VERIFY: Run: powercfg /query SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE. The Current AC Power Setting Index should report the aggressive mode Sabby selected."
            : @"VERIFY: Run the same powercfg command; the AC value should match the value Sabby recorded before applying.",
        "cpu.performance-floor" => activated
            ? @"VERIFY: Run: powercfg /query SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN. Sabby's applied AC minimum should be 100%."
            : @"VERIFY: Run the same powercfg command; the AC minimum should match the saved pre-Sabby value.",
        "network.rss" => @"VERIFY: PowerShell: Get-NetAdapterRss -Name '<adapter>'. Enabled should be True on supported connected physical adapters.",
        "network.tcp-autotuning" => @"VERIFY: PowerShell: (Get-NetTCPSetting -SettingName Internet).AutoTuningLevelLocal. Sabby's target is Normal.",
        "network.power-saving" => @"VERIFY: PowerShell: Get-NetAdapterPowerManagement -Name '<adapter>'. Sabby targets supported sleep/suspend options only; the card read-back shows the resulting adapter state.",
        "network.checksum-offload" => @"VERIFY: PowerShell: Get-NetAdapterChecksumOffload -Name '<adapter>'. Supported IPv4/IPv6 checksum fields should report RxTxEnabled.",
        "network.lso" => @"VERIFY: PowerShell: Get-NetAdapterLso -Name '<adapter>'. IPv4Enabled/IPv6Enabled should match the card's verified state.",
        "network.rsc-low-latency" => @"VERIFY: PowerShell: Get-NetAdapterRsc -Name '<adapter>'. The IPv4/IPv6 flags should match Sabby's verified low-latency state.",
        "network.interrupt-moderation" => @"VERIFY: PowerShell: Get-NetAdapterAdvancedProperty -Name '<adapter>' -RegistryKeyword '*InterruptModeration'. The registry value should match Sabby's verified state.",
        "network.smart-dns" => @"VERIFY: PowerShell: Get-DnsClientServerAddress -AddressFamily IPv4. The connected adapter should show the resolver pair named on this card after the benchmark.",
        var x when x.StartsWith("ext.", StringComparison.OrdinalIgnoreCase) => @"VERIFY: Open Explain to see the exact extension registry path/value. Sabby performs registry read-back after Apply and restores the captured pre-extension value on Undo.",
        "network.eee" => activated
            ? @"VERIFY: PowerShell: Get-NetAdapterAdvancedProperty -Name '<adapter>' -RegistryKeyword '*EEE' -AllProperties. RegistryValue should be 0 on supported active Ethernet adapters."
            : @"VERIFY: Run the same command; the *EEE RegistryValue should match the value Sabby recorded before applying.",
        "input.power-guard" => activated
            ? @"VERIFY: PowerShell: Get-CimInstance -Namespace root\wmi -ClassName MSPower_DeviceEnable. Matching present keyboard/mouse InstanceName entries managed by Sabby should report Enable=False."
            : @"VERIFY: The matching MSPower_DeviceEnable entries should be restored to their saved pre-Sabby Boolean values.",
        "gaming.controller-gamebar-shortcut" => @"VERIFY: Settings > Gaming > Game Bar > Allow your controller to open Game Bar should be Off. Registry: HKCU\Software\Microsoft\GameBar\UseNexusForGameBarEnabled = DWORD 0.",
        "input.keyboard-repeat" => @"VERIFY: Control Panel > Keyboard > Speed. Sabby also reads SPI_GETKEYBOARDSPEED; applied target is the documented maximum value 31.",
        "input.mouse-acceleration" => @"VERIFY: Windows mouse acceleration is read back through SPI_GETMOUSE. Applied target has acceleration value 0. Games using raw input may ignore this Windows pointer setting.",
        "network.d0-packet-coalescing" => @"VERIFY: PowerShell: Get-NetAdapterPowerManagement -Name '<adapter>'. D0PacketCoalescing should report Disabled on adapters where the driver supports it.",
        "network.wake-magic-packet" => @"VERIFY: PowerShell: Get-NetAdapterAdvancedProperty -Name '<adapter>' -RegistryKeyword '*WakeOnMagicPacket' -AllProperties. Applied RegistryValue should be 0.",
        "network.wake-pattern" => @"VERIFY: PowerShell: Get-NetAdapterAdvancedProperty -Name '<adapter>' -RegistryKeyword '*WakeOnPattern' -AllProperties. Applied RegistryValue should be 0.",
        "network.arp-sleep-offload" => @"VERIFY: PowerShell: Get-NetAdapterAdvancedProperty -Name '<adapter>' -RegistryKeyword '*PMARPOffload' -AllProperties. Applied RegistryValue should be 0.",
        "network.ns-sleep-offload" => @"VERIFY: PowerShell: Get-NetAdapterAdvancedProperty -Name '<adapter>' -RegistryKeyword '*PMNSOffload' -AllProperties. Applied RegistryValue should be 0.",
        "network.modern-standby-wol" => @"VERIFY: PowerShell: Get-NetAdapterAdvancedProperty -Name '<adapter>' -RegistryKeyword '*ModernStandbyWoLMagicPacket' -AllProperties. Applied RegistryValue should be 0.",
        "network.device-sleep-disconnect" => @"VERIFY: PowerShell: Get-NetAdapterAdvancedProperty -Name '<adapter>' -RegistryKeyword '*DeviceSleepOnDisconnect' -AllProperties. Applied RegistryValue should be 0.",
        "network.flow-control" => @"VERIFY: PowerShell: Get-NetAdapterAdvancedProperty -Name '<adapter>' -RegistryKeyword '*FlowControl' -AllProperties. Applied RegistryValue should be 0 (Tx & Rx Disabled).",
        "network.priority-vlan" => @"VERIFY: PowerShell: Get-NetAdapterAdvancedProperty -Name '<adapter>' -RegistryKeyword '*PriorityVLANTag' -AllProperties. Applied RegistryValue should be 0 (Packet Priority & VLAN Disabled).",
        "privacy.advertising-id" => @"VERIFY: HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo\DisabledByGroupPolicy = DWORD 1.",
        "privacy.tailored-experiences" => @"VERIFY: HKCU\Software\Policies\Microsoft\Windows\CloudContent\DisableTailoredExperiencesWithDiagnosticData = DWORD 1.",
        "privacy.third-party-suggestions" => @"VERIFY: HKCU\Software\Policies\Microsoft\Windows\CloudContent\DisableThirdPartySuggestions = DWORD 1.",
        "privacy.windows-spotlight" => @"VERIFY: HKCU\Software\Policies\Microsoft\Windows\CloudContent\DisableWindowsSpotlightFeatures = DWORD 1.",
        "privacy.activity-history" => @"VERIFY: HKLM\SOFTWARE\Policies\Microsoft\Windows\System: EnableActivityFeed, PublishUserActivities, and UploadUserActivities should all be DWORD 0.",
        "privacy.delivery-optimization-p2p" => @"VERIFY: HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization\DODownloadMode = DWORD 0 (HTTP only / no peer sharing).",
        "privacy.file-search-history" => @"VERIFY: HKCU\Software\Policies\Microsoft\Windows\Explorer\DisableSearchBoxSuggestions = DWORD 1.",
        "security.llmnr" => @"VERIFY: HKLM\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient\EnableMulticast = DWORD 0.",
        "security.remote-assistance" => @"VERIFY: HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services\fAllowToGetHelp = DWORD 0.",
        "security.voice-above-lock" => @"VERIFY: HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy\LetAppsActivateWithVoiceAboveLock = DWORD 2.",
        "security.autoplay-all-drives" => @"VERIFY: HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\NoDriveTypeAutoRun = DWORD 255 (0xFF).",
        "privacy.clipboard-history" => @"VERIFY: HKLM\SOFTWARE\Policies\Microsoft\Windows\System\AllowClipboardHistory = DWORD 0. Win+V history should be disabled by policy.",
        "privacy.cross-device-clipboard" => @"VERIFY: HKLM\SOFTWARE\Policies\Microsoft\Windows\System\AllowCrossDeviceClipboard = DWORD 0.",
        "security.remote-desktop" => @"VERIFY: HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server\fDenyTSConnections = DWORD 1. Domain policy can override this local setting.",
        "security.smb-insecure-guest" => @"VERIFY: HKLM\SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation\AllowInsecureGuestAuth = DWORD 0.",
        "security.defender-pua" => @"VERIFY: PowerShell: (Get-MpPreference).PUAProtection. Applied target is 1 (Enabled / block mode).",
        "cpu.energy-performance-preference" => @"VERIFY: Run: powercfg /query SCHEME_CURRENT SUB_PROCESSOR PERFEPP. Sabby's AC target is 0 (favor performance).",
        "cpu.core-parking-min" => @"VERIFY: Run: powercfg /query SCHEME_CURRENT SUB_PROCESSOR CPMINCORES. Sabby's AC target is 100%, which disables core parking.",
        "cpu.performance-increase-policy" => @"VERIFY: Run: powercfg /query SCHEME_CURRENT SUB_PROCESSOR PERFINCPOL. Sabby's AC target is index 3 (optimized for responsiveness).",
        "cpu.boost-policy" => @"VERIFY: Run: powercfg /query SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTPOL. Sabby's AC target is 100%.",
        "input.usb-selective-suspend" => @"VERIFY: Run: powercfg /query SCHEME_CURRENT SUB_USB USBSELECTIVE. Sabby's AC target is index 0 (Disabled).",
        "power.pcie-aspm" => @"VERIFY: Run: powercfg /query SCHEME_CURRENT SUB_PCIEXPRESS ASPM. Sabby's AC target is index 0 (None).",
        "windows.visual-effects" => @"VERIFY: Run SystemPropertiesPerformance.exe and inspect Windows visual-effects animation options. Sabby also performs an independent SystemParametersInfo read-back.",
        _ => @"VERIFY: Sabby re-reads the underlying Windows setting after every change. Open Info for the exact Windows mechanism this card controls."
    };

    private static int GetEvidenceScore(string id) => id switch
    {
        "gaming.capture" => 45,
        "gaming.game-mode" => 75,
        "gaming.controller-gamebar-shortcut" => 20,
        "cpu.boost-mode" => 70,
        "cpu.performance-floor" => 30,
        "cpu.energy-performance-preference" => 70,
        "cpu.core-parking-min" => 35,
        "cpu.performance-increase-policy" => 55,
        "cpu.boost-policy" => 50,
        "power.active-plan" => 55,
        "graphics.hags" => 55,
        "windows.visual-effects" => 25,
        "network.smart-dns" => 70,
        "network.rss" => 90,
        "network.tcp-autotuning" => 90,
        "network.power-saving" => 65,
        "network.interrupt-moderation" => 50,
        "network.checksum-offload" => 95,
        "network.lso" => 80,
        "network.rsc-low-latency" => 45,
        "network.task-offload" => 90,
        "system.power-throttling" => 60,
        "network.eee" => 55,
        "network.d0-packet-coalescing" => 45,
        "network.wake-magic-packet" => 70,
        "network.wake-pattern" => 70,
        "network.arp-sleep-offload" => 65,
        "network.ns-sleep-offload" => 65,
        "network.modern-standby-wol" => 65,
        "network.device-sleep-disconnect" => 70,
        "network.flow-control" => 75,
        "network.priority-vlan" => 75,
        "privacy.advertising-id" => 95,
        "privacy.tailored-experiences" => 95,
        "privacy.third-party-suggestions" => 90,
        "privacy.windows-spotlight" => 90,
        "privacy.activity-history" => 95,
        "privacy.delivery-optimization-p2p" => 95,
        "privacy.file-search-history" => 90,
        "security.llmnr" => 90,
        "security.remote-assistance" => 90,
        "security.voice-above-lock" => 90,
        "security.autoplay-all-drives" => 85,
        "privacy.clipboard-history" => 95,
        "privacy.cross-device-clipboard" => 95,
        "security.remote-desktop" => 95,
        "security.smb-insecure-guest" => 100,
        "security.defender-pua" => 100,
        "input.power-guard" => 50,
        "input.keyboard-repeat" => 35,
        "input.mouse-acceleration" => 55,
        "input.usb-selective-suspend" => 65,
        "power.pcie-aspm" => 45,
        "services.search-indexing" => 35,
        "services.sysmain" => 30,
        "startup.background-apps" => 55,
        "cleanup.temp-files" => 80,
        var x when x.StartsWith("ext.", StringComparison.OrdinalIgnoreCase) => 50,
        _ => 50
    };

    private static int GetIntroducedOrder(string id) => id switch
    {
        "gaming.capture" or "gaming.game-mode" or "windows.visual-effects" => 8,
        "cpu.boost-mode" or "cpu.performance-floor" or "power.active-plan" or "graphics.hags" => 9,
        "cpu.energy-performance-preference" or "cpu.core-parking-min" or "cpu.performance-increase-policy" or "cpu.boost-policy" => 24,
        "network.eee" or "input.power-guard" => 14,
        "gaming.controller-gamebar-shortcut" or "input.keyboard-repeat" or "input.mouse-acceleration" or "input.usb-selective-suspend" or "network.d0-packet-coalescing" or "power.pcie-aspm" => 23,
        "network.wake-magic-packet" or "network.wake-pattern" or "network.arp-sleep-offload" or "network.ns-sleep-offload" or "network.modern-standby-wol" or "network.device-sleep-disconnect" or "network.flow-control" or "network.priority-vlan" => 22,
        var x when x.StartsWith("privacy.", StringComparison.OrdinalIgnoreCase) || x.StartsWith("security.", StringComparison.OrdinalIgnoreCase) => 22,
        var x when x.StartsWith("ext.", StringComparison.OrdinalIgnoreCase) => 21,
        var x when x.StartsWith("network.", StringComparison.OrdinalIgnoreCase) => 10,
        "system.power-throttling" => 10,
        var x when x.StartsWith("services.", StringComparison.OrdinalIgnoreCase) => 11,
        var x when x.StartsWith("startup.", StringComparison.OrdinalIgnoreCase) => 11,
        var x when x.StartsWith("cleanup.", StringComparison.OrdinalIgnoreCase) => 11,
        _ => 3
    };

    private void UpdateState(TweakDetectionResult state)
    {
        _state = state;
        OnPropertyChanged(nameof(StateDisplay));
        OnPropertyChanged(nameof(StateDetail));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(IsActivated));
        OnPropertyChanged(nameof(IsStatePending));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(ToggleActionText));
        OnPropertyChanged(nameof(ToggleHint));
        OnPropertyChanged(nameof(IsProtectedActive));
        OnPropertyChanged(nameof(VerificationGuide));
        OnPropertyChanged(nameof(IsRecommendationBlocked));
        OnPropertyChanged(nameof(RecommendationBlockedText));
        ApplyCommand.RaiseCanExecuteChanged();
        UndoCommand.RaiseCanExecuteChanged();
        ToggleActivationCommand.RaiseCanExecuteChanged();
    }
}
