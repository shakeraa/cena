// =============================================================================
// Cena Platform — NATS publish deadline guard
//
// Background (RDY / E2E-J-03):
//   NATS.NET v2's PublishAsync awaits a fully-buffered send. When the broker
//   is disconnected, the call queues internally and waits for reconnect
//   instead of throwing. Any request-path publisher that awaits PublishAsync
//   directly therefore blocks the HTTP request indefinitely on a NATS outage,
//   even though the file-banner intent is "best-effort, after persistence".
//
// Fix:
//   Race the publish task against a short deadline. If the deadline wins,
//   throw TimeoutException so the caller's existing try/catch logs+metrics
//   the failure path the same way as any other NATS error. Marten / Redis
//   remains the source of truth; the publish is fanout-only.
//
// Two overloads:
//   - PublishWithDeadlineAsync(string subject, byte[] payload, headers, ct)
//     for callers that already have serialized bytes (the common shape).
//   - A version that takes the underlying ValueTask is unnecessary — every
//     caller in this repo serializes first, then publishes.
//
// Default deadline: 2 seconds. Aligns with the 2s budget called out in the
// E2E-J-03 spec banner and short enough that a stuck publish doesn't bleed
// into user-facing latency on healthy NATS (typical publish < 10ms).
// =============================================================================

using NATS.Client.Core;

namespace Cena.Actors.Bus;

public static class NatsPublishExtensions
{
    public static readonly TimeSpan DefaultPublishDeadline = TimeSpan.FromSeconds(2);

    public static async Task PublishWithDeadlineAsync(
        this INatsConnection nats,
        string subject,
        byte[] payload,
        NatsHeaders? headers = null,
        TimeSpan? deadline = null,
        CancellationToken cancellationToken = default)
    {
        var budget = deadline ?? DefaultPublishDeadline;

        // Linked CTS so the deadline can cancel the underlying publish task,
        // freeing the buffered send if NATS later reconnects.
        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var publishTask = nats
            .PublishAsync(subject, payload, headers: headers, cancellationToken: deadlineCts.Token)
            .AsTask();

        var winner = await Task.WhenAny(publishTask, Task.Delay(budget, cancellationToken))
            .ConfigureAwait(false);

        if (winner != publishTask)
        {
            // Cancel the underlying publish so its buffered send releases.
            deadlineCts.Cancel();

            // Caller cancellation takes precedence over the deadline — preserve
            // the OperationCanceledException semantics callers expect.
            cancellationToken.ThrowIfCancellationRequested();

            throw new TimeoutException(
                $"NATS publish to '{subject}' did not complete within {budget.TotalMilliseconds:F0}ms.");
        }

        // Publish task won — observe any exception it produced.
        await publishTask.ConfigureAwait(false);
    }
}
