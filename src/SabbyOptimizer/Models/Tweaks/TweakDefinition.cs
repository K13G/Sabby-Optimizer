namespace PCTweaker.Models.Tweaks;

public sealed record TweakDefinition(
    string Id,
    string Name,
    string ShortDescription,
    TweakCategory Category,
    TweakSafetyLevel SafetyLevel,
    string Explanation,
    string WhatChanges,
    string UndoDescription,
    bool RequiresAdministrator,
    bool RequiresRestart,
    bool IsReversible,
    bool IsVisible = true);
