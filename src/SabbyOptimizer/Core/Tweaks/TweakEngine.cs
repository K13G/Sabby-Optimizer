using System.Collections.Concurrent;
using System.Security.Principal;
using PCTweaker.Core.Backups;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks;

public sealed class TweakEngine : ITweakEngine
{
    private readonly Dictionary<string, ITweakHandler> _handlers;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _operationLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly IAppLogger _logger;
    private readonly IOriginalStateStore? _originalStateStore;

    public IReadOnlyList<TweakDefinition> Definitions { get; }

    public TweakEngine(IEnumerable<ITweakHandler> handlers, IAppLogger logger, IOriginalStateStore? originalStateStore = null)
    {
        _logger = logger;
        _originalStateStore = originalStateStore;
        _handlers = new Dictionary<string, ITweakHandler>(StringComparer.OrdinalIgnoreCase);

        foreach (var handler in handlers)
        {
            if (string.IsNullOrWhiteSpace(handler.Definition.Id))
                throw new InvalidOperationException("A tweak handler has an empty ID.");

            if (!_handlers.TryAdd(handler.Definition.Id, handler))
                throw new InvalidOperationException($"Duplicate tweak ID '{handler.Definition.Id}'.");
        }

        Definitions = _handlers.Values
            .Select(handler => handler.Definition)
            .Where(definition => definition.IsVisible)
            .OrderBy(definition => definition.Category)
            .ThenBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public TweakExplanation Explain(string tweakId)
    {
        var definition = GetHandler(tweakId).Definition;
        return new TweakExplanation(
            definition.Name,
            definition.Explanation,
            definition.WhatChanges,
            definition.UndoDescription,
            definition.SafetyLevel,
            definition.RequiresAdministrator,
            definition.RequiresRestart,
            definition.IsReversible);
    }

    public async Task<TweakDetectionResult> DetectAsync(string tweakId, CancellationToken cancellationToken = default)
    {
        var handler = GetHandler(tweakId);

        try
        {
            var result = await RunHandlerAsync(() => handler.DetectAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
            _logger.Info($"Tweak detect: {tweakId} -> {result.State} ({result.DisplayText}).");
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Tweak detection failed: {tweakId}.", ex);
            return TweakDetectionResult.Error("Detection failed. The error was written to the Sabby Optimizer log.");
        }
    }

    public async Task<TweakCompatibilityResult> CheckCompatibilityAsync(string tweakId, bool applying = true, CancellationToken cancellationToken = default)
    {
        var handler = GetHandler(tweakId);
        if (handler is not ITweakCompatibilityProvider provider)
            return TweakCompatibilityResult.Compatible("This tweak passed catalog-level hardware and Windows capability checks.");

        try
        {
            return await RunHandlerAsync(() => provider.CheckCompatibilityAsync(applying, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.Error($"Compatibility check failed: {tweakId}.", ex);
            return TweakCompatibilityResult.Blocked("Compatibility validation failed, so Sabby blocked the change instead of guessing.");
        }
    }

    public async Task<IReadOnlyDictionary<string, TweakDetectionResult>> DetectAllAsync(CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, TweakDetectionResult>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in Definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results[definition.Id] = await DetectAsync(definition.Id, cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    public Task<TweakOperationResult> ApplyAsync(string tweakId, CancellationToken cancellationToken = default) =>
        ExecuteExclusiveAsync(tweakId, apply: true, cancellationToken);

    public Task<TweakOperationResult> UndoAsync(string tweakId, CancellationToken cancellationToken = default) =>
        ExecuteExclusiveAsync(tweakId, apply: false, cancellationToken);

    private async Task<TweakOperationResult> ExecuteExclusiveAsync(string tweakId, bool apply, CancellationToken cancellationToken)
    {
        var handler = GetHandler(tweakId);
        var definition = handler.Definition;
        var gate = _operationLocks.GetOrAdd(definition.Id, static _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var before = await DetectAsync(definition.Id, cancellationToken).ConfigureAwait(false);
            if (before.State is TweakStateKind.Unavailable or TweakStateKind.Error)
                return TweakOperationResult.Failed($"Safety validation blocked the operation: {before.Detail}");

            if (apply && !before.CanApply)
                return TweakOperationResult.Failed(before.State == TweakStateKind.Applied
                    ? "This tweak is already applied. No duplicate change was made."
                    : "Safety validation blocked this tweak in its current state.");

            if (!apply)
            {
                if (!definition.IsReversible)
                    return TweakOperationResult.Failed("This tweak is marked as non-reversible and cannot be undone automatically.");

                if (!before.CanUndo)
                    return TweakOperationResult.Failed(before.State == TweakStateKind.NotApplied
                        ? "There is nothing to undo."
                        : "This tweak cannot be undone in its current state.");
            }

            // Hardware/driver-specific handlers can block an incompatible operation before any Windows state is modified.
            if (handler is ITweakCompatibilityProvider compatibilityProvider)
            {
                var compatibility = await RunHandlerAsync(() => compatibilityProvider.CheckCompatibilityAsync(apply, cancellationToken), cancellationToken).ConfigureAwait(false);
                if (!compatibility.IsCompatible)
                {
                    _logger.Warning($"Compatibility check blocked {definition.Id}: {compatibility.Message}");
                    return TweakOperationResult.Failed($"Compatibility check blocked this change: {compatibility.Message}");
                }

                if (!string.IsNullOrWhiteSpace(compatibility.Warning))
                    _logger.Warning($"Compatibility warning for {definition.Id}: {compatibility.Warning}");
                else
                    _logger.Info($"Compatibility check passed: {definition.Id}. {compatibility.Message}");
            }

            if (definition.RequiresAdministrator && !IsCurrentProcessElevated())
            {
                _logger.Info($"Requesting administrator approval for tweak: {definition.Id}.");
                var elevated = await ElevatedTweakRunner.RunAsync(definition.Id, apply, cancellationToken).ConfigureAwait(false);
                if (!elevated.Success)
                {
                    _logger.Warning($"Administrator tweak helper did not complete {definition.Id}: {elevated.Message}");
                    return TweakOperationResult.Failed(elevated.Message, requiresElevation: true);
                }

                var elevatedVerified = await DetectAsync(definition.Id, cancellationToken).ConfigureAwait(false);
                if (elevatedVerified.State is TweakStateKind.Error or TweakStateKind.Unavailable)
                    return TweakOperationResult.Failed("Windows approved the change, but Sabby could not verify the resulting state.");
                var elevatedExpectedStateConfirmed = apply
                    ? elevatedVerified.State == TweakStateKind.Applied
                    : elevatedVerified.State != TweakStateKind.Applied;
                if (!elevatedExpectedStateConfirmed)
                    return TweakOperationResult.Failed($"Windows approved the operation, but read-back verification reported '{elevatedVerified.DisplayText}'.");

                return TweakOperationResult.Completed(elevated.Message, elevatedVerified, definition.RequiresRestart);
            }

            if (_originalStateStore is not null && definition.IsVisible)
                await _originalStateStore.CaptureIfMissingAsync(definition, before, cancellationToken).ConfigureAwait(false);

            _logger.Info($"Tweak {(apply ? "apply" : "undo")} started: {definition.Id}. Before={before.State}.");

            var operation = apply
                ? await RunHandlerAsync(() => handler.ApplyAsync(cancellationToken), cancellationToken).ConfigureAwait(false)
                : await RunHandlerAsync(() => handler.UndoAsync(cancellationToken), cancellationToken).ConfigureAwait(false);

            if (!operation.Success)
            {
                _logger.Warning($"Tweak {(apply ? "apply" : "undo")} rejected/failed: {definition.Id}. {operation.Message}");
                if (apply && definition.IsReversible)
                {
                    var afterFailure = await DetectAsync(definition.Id, cancellationToken).ConfigureAwait(false);
                    if (HasMeaningfullyChanged(before, afterFailure))
                    {
                        var rollback = await AttemptRollbackAsync(handler, definition, before, cancellationToken).ConfigureAwait(false);
                        return TweakOperationResult.Failed($"{operation.Message} {rollback.Message}");
                    }
                }
                return operation;
            }

            var verified = await DetectAsync(definition.Id, cancellationToken).ConfigureAwait(false);
            var expectedStateConfirmed = verified.State is not (TweakStateKind.Error or TweakStateKind.Unavailable) &&
                (apply ? verified.State == TweakStateKind.Applied : verified.State != TweakStateKind.Applied);

            if (!expectedStateConfirmed)
            {
                _logger.Warning($"Tweak post-condition mismatch after {(apply ? "apply" : "undo")}: {definition.Id} -> {verified.State}.");
                if (apply && definition.IsReversible)
                {
                    var rollback = await AttemptRollbackAsync(handler, definition, before, cancellationToken).ConfigureAwait(false);
                    return TweakOperationResult.Failed(
                        $"The change could not be verified by Windows read-back. {rollback.Message}");
                }

                return TweakOperationResult.Failed(
                    $"The operation returned successfully, but Windows read-back reported '{verified.DisplayText}' instead of the expected state.");
            }

            _logger.Info($"Tweak {(apply ? "apply" : "undo")} verified by read-back: {definition.Id} -> {verified.State} ({verified.DisplayText}).");
            return TweakOperationResult.Completed(
                operation.Message,
                verified,
                definition.RequiresRestart || operation.RequiresRestart);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Tweak {(apply ? "apply" : "undo")} crashed: {tweakId}.", ex);
            return TweakOperationResult.Failed("The operation failed safely. No additional changes were attempted and the error was written to the Sabby Optimizer log.");
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<(bool Success, string Message)> AttemptRollbackAsync(
        ITweakHandler handler,
        TweakDefinition definition,
        TweakDetectionResult before,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.Warning($"Automatic rollback started for {definition.Id} after failed validation.");
            var rollbackOperation = await RunHandlerAsync(() => handler.UndoAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
            var rollbackState = await DetectAsync(definition.Id, cancellationToken).ConfigureAwait(false);
            var restored = rollbackOperation.Success &&
                           rollbackState.State is not (TweakStateKind.Error or TweakStateKind.Unavailable) &&
                           (rollbackState.State == before.State || (before.State != TweakStateKind.Applied && rollbackState.State != TweakStateKind.Applied));
            if (restored)
            {
                _logger.Info($"Automatic rollback verified for {definition.Id}: {rollbackState.DisplayText}.");
                return (true, $"Automatic rollback succeeded and Windows read-back returned '{rollbackState.DisplayText}'.");
            }

            _logger.Warning($"Automatic rollback could not be verified for {definition.Id}. State={rollbackState.State}.");
            return (false, "Automatic rollback was attempted but could not be fully verified. Use Backups or PC Restore before making further changes.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Automatic rollback crashed for {definition.Id}.", ex);
            return (false, "Automatic rollback encountered an error. Use Backups or PC Restore before making further changes.");
        }
    }

    private static Task<T> RunHandlerAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) =>
        Task.Run(operation, cancellationToken);

    private static bool HasMeaningfullyChanged(TweakDetectionResult before, TweakDetectionResult after) =>
        after.State is not (TweakStateKind.Error or TweakStateKind.Unavailable) &&
        (before.State != after.State || !string.Equals(before.DisplayText, after.DisplayText, StringComparison.OrdinalIgnoreCase));

    private ITweakHandler GetHandler(string tweakId)
    {
        if (string.IsNullOrWhiteSpace(tweakId))
            throw new ArgumentException("Tweak ID is required.", nameof(tweakId));

        if (!_handlers.TryGetValue(tweakId, out var handler))
            throw new KeyNotFoundException($"Unknown tweak ID '{tweakId}'.");

        return handler;
    }

    private static bool IsCurrentProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
