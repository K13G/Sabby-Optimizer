namespace PCTweaker.Models.Tweaks;

public sealed record TweakExplanation(
    string Name,
    string Explanation,
    string WhatChanges,
    string UndoDescription,
    TweakSafetyLevel SafetyLevel,
    bool RequiresAdministrator,
    bool RequiresRestart,
    bool IsReversible);
