namespace PCTweaker.Models;

public sealed record RouteHopResult(int Hop, string Address, double RoundTripMs, bool ReachedDestination)
{
    public string Display => string.IsNullOrWhiteSpace(Address)
        ? $"{Hop}.  *"
        : $"{Hop}.  {Address}  {(RoundTripMs < 1 ? "<1" : RoundTripMs.ToString("0.0"))} ms";
}
