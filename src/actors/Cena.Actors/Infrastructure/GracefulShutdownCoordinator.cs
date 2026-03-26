// =============================================================================
// Cena Platform -- GracefulShutdownCoordinator (Hosted Service)
// Layer: Infrastructure | Runtime: .NET 9
//
// Registered as IHostedService. On SIGTERM/StopAsync:
//   Phase 1: Stop new activations (tell StudentActorManager)
//   Phase 2: Wait for active sessions to end (max 30s)
//   Phase 3: Flush any buffered analytics
//   Phase 4: Leave cluster
// Logs each phase with timing.
// =============================================================================

using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.Cluster;
using Cena.Actors.Management;

namespace Cena.Actors.Infrastructure;

/// <summary>
/// Coordinates graceful shutdown of the Cena actor system.
/// Implements a 4-phase shutdown sequence with timeouts and logging.
/// </summary>
public sealed class GracefulShutdownCoordinator : IHostedService
{
    private readonly ActorSystem _actorSystem;
    private readonly ILogger<GracefulShutdownCoordinator> _logger;

    // ── Configuration ──
    private static readonly TimeSpan Phase2Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Phase3Timeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Phase4Timeout = TimeSpan.FromSeconds(15);

    // ── Manager PID (set during startup) ──
    private PID? _managerPid;

    public GracefulShutdownCoordinator(
        ActorSystem actorSystem,
        ILogger<GracefulShutdownCoordinator> logger)
    {
        _actorSystem = actorSystem ?? throw new ArgumentNullException(nameof(actorSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Called on application start. Resolves the StudentActorManager PID for later use.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GracefulShutdownCoordinator registered. Ready to handle SIGTERM.");

        // Attempt to resolve the StudentActorManager as a known cluster kind.
        // The PID is resolved lazily -- it may not be available at startup.
        // We will resolve it during shutdown if needed.
        ResolveManagerPid();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Called on SIGTERM or application stop. Executes the 4-phase shutdown.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var totalSw = Stopwatch.StartNew();
        _logger.LogInformation("=== GRACEFUL SHUTDOWN INITIATED ===");

        // Ensure we have a manager PID
        ResolveManagerPid();

        // ── Phase 1: Stop new activations ──
        await ExecutePhase1(cancellationToken);

        // ── Phase 2: Wait for active sessions to drain ──
        await ExecutePhase2(cancellationToken);

        // ── Phase 3: Flush buffered analytics ──
        await ExecutePhase3(cancellationToken);

        // ── Phase 4: Leave cluster ──
        await ExecutePhase4(cancellationToken);

        totalSw.Stop();
        _logger.LogInformation(
            "=== GRACEFUL SHUTDOWN COMPLETE === Total duration: {Duration}ms",
            totalSw.ElapsedMilliseconds);
    }

    // ══════════════════════════════════════════════════════════════════════
    // PHASE 1: Stop new activations
    // ══════════════════════════════════════════════════════════════════════

    private async Task ExecutePhase1(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[Phase 1] Stopping new student actor activations...");

        try
        {
            if (_managerPid != null)
            {
                _actorSystem.Root.Send(_managerPid, new StopNewActivations());
                // Give a brief moment for the message to be processed
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
                _logger.LogInformation(
                    "[Phase 1] StopNewActivations sent to StudentActorManager.");
            }
            else
            {
                _logger.LogWarning(
                    "[Phase 1] StudentActorManager PID not available. " +
                    "New activations may not be blocked.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[Phase 1] Error stopping new activations.");
        }

        sw.Stop();
        _logger.LogInformation("[Phase 1] Complete. Duration: {Duration}ms", sw.ElapsedMilliseconds);
    }

    // ══════════════════════════════════════════════════════════════════════
    // PHASE 2: Wait for active sessions to end
    // ══════════════════════════════════════════════════════════════════════

    private async Task ExecutePhase2(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[Phase 2] Waiting for active sessions to drain (max {Timeout}s)...",
            Phase2Timeout.TotalSeconds);

        try
        {
            if (_managerPid != null)
            {
                var drainResponse = await _actorSystem.Root.RequestAsync<DrainAllResponse>(
                    _managerPid,
                    new DrainAll(Phase2Timeout),
                    Phase2Timeout);

                _logger.LogInformation(
                    "[Phase 2] Drain result: Drained={Drained}, Remaining={Remaining}, " +
                    "Elapsed={Elapsed}ms",
                    drainResponse.DrainedCount,
                    drainResponse.RemainingCount,
                    drainResponse.Elapsed.TotalMilliseconds);

                if (drainResponse.RemainingCount > 0)
                {
                    _logger.LogWarning(
                        "[Phase 2] {Remaining} actors did not drain within timeout.",
                        drainResponse.RemainingCount);
                }
            }
            else
            {
                _logger.LogWarning(
                    "[Phase 2] StudentActorManager PID not available. " +
                    "Waiting {Timeout}s for natural passivation...",
                    Phase2Timeout.TotalSeconds);

                // Fallback: just wait the timeout period
                using var phase2Cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                phase2Cts.CancelAfter(Phase2Timeout);
                try
                {
                    await Task.Delay(Phase2Timeout, phase2Cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            }
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("[Phase 2] Drain request timed out.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[Phase 2] Error during drain.");
        }

        sw.Stop();
        _logger.LogInformation("[Phase 2] Complete. Duration: {Duration}ms", sw.ElapsedMilliseconds);
    }

    // ══════════════════════════════════════════════════════════════════════
    // PHASE 3: Flush buffered analytics
    // ══════════════════════════════════════════════════════════════════════

    private async Task ExecutePhase3(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[Phase 3] Flushing buffered analytics...");

        try
        {
            // Flush OpenTelemetry metrics and traces by giving providers time to export
            // The OTEL SDK flushes automatically on shutdown, but we add an explicit wait
            // to ensure any in-flight batches are exported
            using var phase3Cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            phase3Cts.CancelAfter(Phase3Timeout);

            // Signal any in-process metric exporters to flush
            // This relies on the OTEL SDK's built-in flush mechanism
            // which triggers on the host's ApplicationStopping token
            await Task.Delay(TimeSpan.FromSeconds(2), phase3Cts.Token);

            _logger.LogInformation("[Phase 3] Analytics flush window completed.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[Phase 3] Analytics flush interrupted by cancellation.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Phase 3] Error flushing analytics.");
        }

        sw.Stop();
        _logger.LogInformation("[Phase 3] Complete. Duration: {Duration}ms", sw.ElapsedMilliseconds);
    }

    // ══════════════════════════════════════════════════════════════════════
    // PHASE 4: Leave cluster
    // ══════════════════════════════════════════════════════════════════════

    private async Task ExecutePhase4(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[Phase 4] Leaving Proto.Actor cluster...");

        try
        {
            using var phase4Cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            phase4Cts.CancelAfter(Phase4Timeout);

            await _actorSystem.Cluster().ShutdownAsync(graceful: true);

            _logger.LogInformation("[Phase 4] Successfully left cluster. NodeId={NodeId}",
                _actorSystem.Id);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[Phase 4] Cluster leave timed out. Forcing ungraceful leave.");
            try
            {
                await _actorSystem.Cluster().ShutdownAsync(graceful: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Phase 4] Error during forced cluster leave.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Phase 4] Error leaving cluster.");
        }

        sw.Stop();
        _logger.LogInformation("[Phase 4] Complete. Duration: {Duration}ms", sw.ElapsedMilliseconds);
    }

    // ══════════════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Register the StudentActorManager PID so the coordinator can communicate
    /// with it during shutdown. Called by the host during startup after spawning the manager.
    /// </summary>
    public void RegisterManagerPid(PID managerPid)
    {
        _managerPid = managerPid;
        _logger.LogDebug("StudentActorManager PID registered: {Pid}", _managerPid);
    }

    /// <summary>
    /// Check if the StudentActorManager PID has been registered.
    /// The PID must be set via RegisterManagerPid during application startup.
    /// If not registered, shutdown proceeds without manager coordination.
    /// </summary>
    private void ResolveManagerPid()
    {
        if (_managerPid != null) return;

        _logger.LogDebug(
            "StudentActorManager PID not registered. " +
            "Call RegisterManagerPid() during startup for coordinated shutdown.");
    }
}
