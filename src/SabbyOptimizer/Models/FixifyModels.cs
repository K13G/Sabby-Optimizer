namespace PCTweaker.Models;

public sealed record FixifyToolDefinition(
    string Id,
    string Name,
    string Category,
    string Description,
    string WhatItDoes,
    bool RequiresRestart = false,
    bool LongRunning = false);

public sealed record FixifyRunResult(
    bool Success,
    string Summary,
    string Details,
    bool RequiresRestart = false);
