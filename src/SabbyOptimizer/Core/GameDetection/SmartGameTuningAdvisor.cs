using System.Text.RegularExpressions;
using PCTweaker.Models;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.GameDetection;

public sealed record SmartGameTuningPlan(
    string Tier,
    string Summary,
    IReadOnlyDictionary<string, TweakStateKind> DesiredStates);

public static class SmartGameTuningAdvisor
{
    public static SmartGameTuningPlan Build(HardwareInfo hardware)
    {
        var score = 0;
        var gpu = hardware.Graphics ?? string.Empty;
        var cpu = hardware.Processor ?? string.Empty;
        var ramGb = ParseRam(hardware.Memory);

        if (ContainsAny(gpu, "RTX 5090", "RTX 5080", "RTX 5070", "RTX 4090", "RTX 4080", "RX 9090", "RX 9070", "RX 7900")) score += 4;
        else if (ContainsAny(gpu, "RTX 5060", "RTX 4070", "RTX 4060", "RTX 3090", "RTX 3080", "RTX 3070", "RX 7800", "RX 7700", "RX 6900", "RX 6800")) score += 3;
        else if (ContainsAny(gpu, "RTX", "Radeon RX", "Intel Arc")) score += 2;
        else if (!gpu.Contains("Unavailable", StringComparison.OrdinalIgnoreCase)) score += 1;

        if (ContainsAny(cpu, "i9-", "i7-14", "i7-13", "i7-12", "Ryzen 9", "Ryzen 7 9", "9800X3D", "7800X3D")) score += 3;
        else if (ContainsAny(cpu, "i7-", "i5-14", "i5-13", "Ryzen 7", "Ryzen 5 9", "Ryzen 5 8", "Ryzen 5 7")) score += 2;
        else if (!cpu.Contains("Unavailable", StringComparison.OrdinalIgnoreCase)) score += 1;

        if (ramGb >= 32) score += 2;
        else if (ramGb >= 16) score += 1;

        var desired = new Dictionary<string, TweakStateKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["gaming.game-mode"] = TweakStateKind.Applied,
            ["gaming.capture"] = TweakStateKind.NotApplied,
            ["power.active-plan"] = TweakStateKind.Applied
        };

        if (score >= 6)
        {
            desired["graphics.hags"] = TweakStateKind.Applied;
            desired["cpu.boost-mode"] = TweakStateKind.Applied;
        }

        var tier = score >= 7 ? "High-end" : score >= 4 ? "Balanced" : "Efficiency";
        var summary = tier switch
        {
            "High-end" => "Prioritizes low-overhead Windows gaming features while keeping visual quality decisions inside each game.",
            "Balanced" => "Uses conservative gaming and power choices intended to improve consistency without aggressive system changes.",
            _ => "Uses only the safest low-overhead gaming choices and avoids aggressive performance assumptions."
        };

        return new SmartGameTuningPlan(tier, summary, desired);
    }

    private static int ParseRam(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var match = Regex.Match(value, @"(?<gb>\d+(?:\.\d+)?)\s*GB", RegexOptions.IgnoreCase);
        return match.Success && double.TryParse(match.Groups["gb"].Value, out var parsed) ? (int)Math.Round(parsed) : 0;
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
