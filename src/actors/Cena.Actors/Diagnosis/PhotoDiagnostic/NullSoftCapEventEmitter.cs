// =============================================================================
// Cena Platform — NullSoftCapEventEmitter (PRR-401, EPIC-PRR-I, EPIC-PRR-J)
//
// Why this exists
// ---------------
// Legitimate no-subscription-store fallback. Mirrors the pattern used by
// <c>NullEmailSender</c> (PRR-428), <c>NullDiagnosticCreditDispatcher</c>
// (PRR-391), and <c>NullWhatsAppSender</c>: hosts that don't yet have
// <c>ISubscriptionAggregateStore</c> wired (e.g. the standalone actor
// host used for pure-CAS benchmarks, or an integration-test rig that
// only boots the photo-diagnostic slice) get a graceful no-op that
// swallows the emit and logs a debug line — instead of a DI resolution
// failure at intake time.
//
// NOT a stub
// ----------
// Production composition (Cena.Student.Api.Host) replaces this with
// <see cref="SoftCapEventEmitter"/> at AddSubscriptions + AddPhotoDiagnostic
// time. This is the same discipline every "Null*" class in this codebase
// follows: a named fallback for the absent-dependency case, with the
// real impl side-by-side in its own file. The debug log on every swallow
// gives ops + tests a breadcrumb that the telemetry path is not wired,
// without crashing the intake hot path.
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public sealed class NullSoftCapEventEmitter : ISoftCapEventEmitter
{
    private readonly ILogger<NullSoftCapEventEmitter> _logger;

    public NullSoftCapEventEmitter(ILogger<NullSoftCapEventEmitter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task EmitIfFirstInPeriodAsync(
        string studentSubjectIdHash,
        string parentSubjectIdEncrypted,
        string capType,
        int usageCount,
        int capLimit,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        _logger.LogDebug(
            "[PRR-401] NullSoftCapEventEmitter swallowed soft-cap emission "
            + "capType={CapType} usage={Usage} cap={Cap} (no ISubscriptionAggregateStore wired)",
            capType, usageCount, capLimit);
        return Task.CompletedTask;
    }
}
