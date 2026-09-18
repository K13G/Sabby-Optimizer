namespace PCTweaker.Models;

public sealed record DebloatAppInfo(
    string Name,
    string DisplayName,
    string PackageFullName,
    string Publisher,
    string WhatItIs,
    string RemovalEffect,
    string Recommendation,
    int RemoveScore,
    bool SafeForBulk,
    bool CanRemove = true)
{
    public string ScoreLabel => !CanRemove ? "WINDOWS PROTECTED" : RemoveScore >= 85 ? "SAFE TO REMOVE" : RemoveScore >= 70 ? "GOOD" : RemoveScore >= 50 ? "OPTIONAL" : "KEEP";
    public string SafetyLabel => !CanRemove ? "PROTECTED" : RemoveScore >= 90 ? "BEST" : RemoveScore >= 70 ? "GOOD" : RemoveScore >= 50 ? "MIXED" : "KEEP";
    public double SafetyGreenWidth => !CanRemove ? 0 : 96d * Math.Clamp(RemoveScore, 0, 100) / 100d;
    public double SafetyRedWidth => !CanRemove ? 96 : 96d - SafetyGreenWidth;
    public string PackageIdText => $"Package: {Name}";
}
