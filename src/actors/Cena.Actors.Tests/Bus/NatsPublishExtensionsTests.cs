// =============================================================================
// Tests for NatsPublishExtensions — the deadline guard that backs
// E2E-J-03 (NATS down, student writes stay 2xx). Without these
// guarantees the on-first-sign-in request hangs on broker outage.
// =============================================================================

using System.Diagnostics;
using Cena.Actors.Bus;
using NATS.Client.Core;
using NSubstitute;

namespace Cena.Actors.Tests.Bus;

public sealed class NatsPublishExtensionsTests
{
    private readonly INatsConnection _nats = Substitute.For<INatsConnection>();

    [Fact]
    public async Task PublishWithDeadlineAsync_FastPublish_CompletesWithoutTimeout()
    {
        _nats.PublishAsync<byte[]>(
                Arg.Any<string>(), Arg.Any<byte[]>(),
                headers: Arg.Any<NatsHeaders?>(),
                replyTo: Arg.Any<string?>(),
                serializer: Arg.Any<INatsSerialize<byte[]>?>(),
                opts: Arg.Any<NatsPubOpts?>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        await _nats.PublishWithDeadlineAsync(
            "cena.test", new byte[] { 1 },
            deadline: TimeSpan.FromSeconds(2));

        // No timeout, no throw.
    }

    [Fact]
    public async Task PublishWithDeadlineAsync_StalledPublish_ThrowsTimeoutWithinDeadline()
    {
        // Simulate a stalled NATS connection: the publish never completes
        // until cancelled. This mirrors NATS.NET v2's "wait for reconnect"
        // behavior on a disconnected connection.
        _nats.PublishAsync<byte[]>(
                Arg.Any<string>(), Arg.Any<byte[]>(),
                headers: Arg.Any<NatsHeaders?>(),
                replyTo: Arg.Any<string?>(),
                serializer: Arg.Any<INatsSerialize<byte[]>?>(),
                opts: Arg.Any<NatsPubOpts?>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var ct = call.Arg<CancellationToken>();
                // Honor cancellation so the deadline path can release the publish.
                return new ValueTask(Task.Delay(Timeout.Infinite, ct));
            });

        var sw = Stopwatch.StartNew();
        var deadline = TimeSpan.FromMilliseconds(150);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            _nats.PublishWithDeadlineAsync(
                "cena.test", new byte[] { 1 },
                deadline: deadline));

        sw.Stop();
        // Deadline must fire — and not bleed far past the budget. The 1s
        // upper bound leaves headroom for CI noise without admitting a
        // 30s+ stall regression.
        Assert.InRange(sw.ElapsedMilliseconds, 100, 1_000);
        Assert.Contains("did not complete within", ex.Message);
        Assert.Contains("cena.test", ex.Message);
    }

    [Fact]
    public async Task PublishWithDeadlineAsync_CallerCancellation_PropagatesAsOperationCanceled()
    {
        _nats.PublishAsync<byte[]>(
                Arg.Any<string>(), Arg.Any<byte[]>(),
                headers: Arg.Any<NatsHeaders?>(),
                replyTo: Arg.Any<string?>(),
                serializer: Arg.Any<INatsSerialize<byte[]>?>(),
                opts: Arg.Any<NatsPubOpts?>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var ct = call.Arg<CancellationToken>();
                return new ValueTask(Task.Delay(Timeout.Infinite, ct));
            });

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _nats.PublishWithDeadlineAsync(
                "cena.test", new byte[] { 1 },
                deadline: TimeSpan.FromSeconds(5),
                cancellationToken: cts.Token));
    }
}
