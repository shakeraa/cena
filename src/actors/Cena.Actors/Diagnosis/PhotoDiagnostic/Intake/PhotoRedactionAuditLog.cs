// =============================================================================
// Cena Platform — PhotoRedactionAuditLog (EPIC-PRR-J PRR-413)
//
// Records what the IPhotoRedactor actually did (Applied) and what it could
// not do (Deferred) for every diagnostic photo, so the incidental-PII
// compliance dashboard has a verifiable per-diagnostic trail. Mirrors the
// LoggingPhotoDiagnosticAuditLog pattern from PRR-423 — structured log
// line now, Marten-backed persistence later (follow-up not blocking PRR-413
// because the log signal is the seam: a SIEM tail on
// "[prr-413] redaction-audit" unlocks the compliance view immediately).
//
// Why both kinds of data on the record:
//   - Applied is the evidence that we DID what the consent UX told the
//     student we would do. Percentage-coordinate regions are
//     resolution-independent so the record survives re-encoding /
//     thumbnail generation by downstream stages.
//   - Deferred is the evidence that we TOLD the audit log what we could
//     not do. Face detection in particular is the PPL Amendment 13 +
//     Israeli Privacy Law concern — if the OCR vendor sees a face
//     because our redactor didn't detect it, the "face_detection_not_implemented"
//     tag in the audit record is how counsel proves we disclosed the
//     gap and didn't pretend to redact biometrics we never could.
//
// NOT a stub: the logger backend is production-safe (structured JSON via
// Serilog in Cena.Actors.Host, shipped to the same SIEM pipeline the
// rest of the platform uses). A Marten-backed implementation is a clean
// follow-up that doesn't change this interface.
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic.Intake;

/// <summary>
/// Append-only audit surface for every redactor invocation. One row per
/// diagnostic photo, matched by <paramref name="diagnosticId"/> to the
/// DiagnosticOutcome so auditors can join redaction history against any
/// downstream event.
/// </summary>
public interface IPhotoRedactionAuditLog
{
    /// <summary>
    /// Record the redactor's output. MUST be called for every diagnostic
    /// photo that hits the redactor — including the null-object no-op case,
    /// where the "redaction_not_configured" Deferred tag is the compliance
    /// signal.
    /// </summary>
    Task RecordAsync(string diagnosticId, PhotoRedactionResult result, CancellationToken ct);
}

/// <summary>
/// Logger-backed default audit sink. Emits one structured log line per
/// diagnostic with applied-kinds (summarised as a sorted de-duplicated
/// comma list), deferred-kinds (same), and the method-count histogram
/// (how many solid-fill vs how many blur regions).
/// </summary>
public sealed class LoggingPhotoRedactionAuditLog : IPhotoRedactionAuditLog
{
    private readonly ILogger<LoggingPhotoRedactionAuditLog> _logger;

    public LoggingPhotoRedactionAuditLog(ILogger<LoggingPhotoRedactionAuditLog> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task RecordAsync(string diagnosticId, PhotoRedactionResult result, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(diagnosticId))
            throw new ArgumentException("diagnosticId is required.", nameof(diagnosticId));
        ArgumentNullException.ThrowIfNull(result);

        // Summaries are stable-ordered so log-based assertions (tests + SIEM
        // dashboards) don't flap on list-enumeration order.
        var appliedKinds = result.AppliedRedactions
            .Select(r => r.Kind.ToString())
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        var deferredTags = result.DeferredRedactions
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        // Method-count histogram: how many regions used each RedactionMethod
        // value. Keeps the log compact but lets ops alert on
        // "> 0 regions with method=<unexpected>".
        var methodCounts = result.AppliedRedactions
            .GroupBy(r => r.RedactionMethod, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}={g.Count()}")
            .ToArray();

        _logger.LogInformation(
            "[prr-413] redaction-audit diagId={DiagId} appliedCount={AppliedCount} "
            + "appliedKinds={AppliedKinds} deferredCount={DeferredCount} "
            + "deferredTags={DeferredTags} methodCounts={MethodCounts}",
            diagnosticId,
            result.AppliedRedactions.Count,
            string.Join(',', appliedKinds),
            result.DeferredRedactions.Count,
            string.Join(',', deferredTags),
            string.Join(',', methodCounts));

        return Task.CompletedTask;
    }
}
