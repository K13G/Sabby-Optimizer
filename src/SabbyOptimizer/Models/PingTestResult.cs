namespace PCTweaker.Models;

public sealed record PingTestResult(
    string Target,
    string Address,
    double MinMs,
    double AverageMs,
    double MedianMs,
    double P95Ms,
    double MaxMs,
    double JitterMs,
    double LossPercent)
{
    private static string Ms(double value) => value < 1 ? "<1 ms" : $"{value:0.0} ms";

    public string Summary => LossPercent >= 100
        ? "No replies"
        : $"avg {Ms(AverageMs)} • median {Ms(MedianMs)} • p95 {Ms(P95Ms)} • jitter {Ms(JitterMs)} • loss {LossPercent:0.#}%";

    public string Rating => LossPercent > 0 ? "PACKET LOSS"
        : AverageMs <= 10 && P95Ms <= 15 && JitterMs <= 2 ? "EXCELLENT"
        : AverageMs <= 30 && JitterMs <= 5 ? "GOOD"
        : AverageMs <= 60 ? "OK"
        : "HIGH";
}
