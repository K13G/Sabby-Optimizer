namespace PCTweaker.Models;

public sealed class RouteCandidateResult
{
    public string Label { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string ProbeType { get; init; } = "ICMP";
    public double AverageMs { get; init; }
    public double MedianMs { get; init; }
    public double P95Ms { get; init; }
    public double JitterMs { get; init; }
    public double LossPercent { get; init; }
    public double Score { get; init; }
    public bool IsRecommended { get; init; }

    private static string Ms(double value) => value < 1 ? "<1 ms" : $"{value:0.0} ms";

    public string Badge => IsRecommended ? "BEST MEASURED" : "MEASURED";
    public string Summary => LossPercent >= 100
        ? $"No successful {ProbeType} samples"
        : $"{ProbeType} • avg {Ms(AverageMs)} • p95 {Ms(P95Ms)} • jitter {Ms(JitterMs)} • loss {LossPercent:0.#}%";
}
