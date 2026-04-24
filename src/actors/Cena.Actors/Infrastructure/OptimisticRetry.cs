// =============================================================================
// Cena Platform -- Optimistic Concurrency Retry Helper
// Layer: Actor Infrastructure | Runtime: .NET 9
//
// DATA-010: Simple retry helper for event append operations that may fail
// due to optimistic concurrency conflicts. Used by actors when appending
// events to Marten streams under contention.
//
// Strategy: exponential backoff (100ms, 200ms, 400ms) with max 3 attempts.
// =============================================================================

using Marten.Events.Dcb;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Infrastructure;

/// <summary>
/// Retry helper for Marten event append operations that may throw
/// <see cref="DcbConcurrencyException"/> under concurrent access.
///
/// Actors call <see cref="ExecuteAsync{T}"/> to wrap event appends with
/// automatic retry + exponential backoff. After exhausting retries, the
/// original exception propagates to the actor's supervision strategy.
/// DATA-010
/// </summary>
public static class OptimisticRetry
{
    /// <summary>Maximum number of attempts before giving up.</summary>
    public const int MaxAttempts = 3;

    /// <summary>Base delay for exponential backoff (doubles each retry).</summary>
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Executes <paramref name="operation"/> with retry on
    /// <see cref="DcbConcurrencyException"/>. Returns the operation result.
    /// </summary>
    /// <typeparam name="T">Return type of the operation.</typeparam>
    /// <param name="operation">
    /// The event append operation to execute. Must be idempotent or safe to retry
    /// (Marten rejects duplicate appends via stream version checks).
    /// </param>
    /// <param name="logger">Logger for retry diagnostics.</param>
    /// <param name="operationName">
    /// Human-readable name for log messages (e.g. "AppendConceptAttempt").
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of a successful operation attempt.</returns>
    /// <exception cref="DcbConcurrencyException">
    /// Thrown after all retry attempts are exhausted.
    /// </exception>
    public static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        ILogger logger,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (DcbConcurrencyException ex) when (attempt < MaxAttempts)
            {
                var delay = BaseDelay * (1 << (attempt - 1)); // 100ms, 200ms, 400ms

                logger.LogWarning(
                    ex,
                    "Optimistic concurrency conflict on {Operation}, " +
                    "attempt {Attempt}/{MaxAttempts}. Retrying in {DelayMs}ms.",
                    operationName,
                    attempt,
                    MaxAttempts,
                    delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
        }

        // Unreachable in normal flow — the last attempt either succeeds or
        // throws DcbConcurrencyException (not caught by the when-filter above).
        throw new InvalidOperationException(
            $"OptimisticRetry.ExecuteAsync reached unreachable code for {operationName}.");
    }

    /// <summary>
    /// Executes a void <paramref name="operation"/> with retry on
    /// <see cref="DcbConcurrencyException"/>.
    /// </summary>
    /// <param name="operation">
    /// The event append operation to execute. Must be idempotent or safe to retry.
    /// </param>
    /// <param name="logger">Logger for retry diagnostics.</param>
    /// <param name="operationName">
    /// Human-readable name for log messages (e.g. "AppendSessionStarted").
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="DcbConcurrencyException">
    /// Thrown after all retry attempts are exhausted.
    /// </exception>
    public static async Task ExecuteAsync(
        Func<Task> operation,
        ILogger logger,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            async () =>
            {
                await operation();
                return true; // dummy return for generic overload
            },
            logger,
            operationName,
            cancellationToken);
    }
}
