namespace PCTweaker.Models;

public sealed record BenchmarkMetric(
    string Id,
    string Name,
    string Category,
    double Value,
    string Unit,
    bool HigherIsBetter,
    double NoiseBandPercent);

public sealed record BenchmarkSnapshot(
    string Stage,
    DateTimeOffset CreatedAt,
    string HardwareSummary,
    IReadOnlyList<BenchmarkMetric> Metrics);

public sealed record BenchmarkProgress(double Percent, string Status);
