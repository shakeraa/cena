// =============================================================================
// Cena Platform — Diagnostic-audit admin endpoint (EPIC-PRR-J PRR-390)
//
//   GET /api/admin/diagnostic-disputes/{disputeId}/audit
//
// Admin-only detail view. Support agent triaging a disputed diagnostic
// pulls this to see every artefact Cena has retained about the disputed
// outcome: student identity hash, dispute reason + optional comment,
// lifecycle timestamps, reviewer notes, and — once the upstream capture
// pipeline lands — the photo hash + CAS chain + narration + template id.
//
// Scope boundary v1 (what this PR ships):
//   - Admin-role-guarded detail read off IDiagnosticDisputeService.GetAsync.
//   - Returns dispute metadata (DiagnosticId, StudentSubjectIdHash,
//     Reason, Comment, Status, SubmittedAt, ReviewedAt, ReviewerNote).
//   - Explicitly flags the deferred artefacts (photo hash, OCR trace,
//     CAS chain, narration, template) as null so the response shape
//     does not lie about what's available. The Vue admin diagnostic-
//     audit page can bind against every field today; values fill in
//     once the upstream capture writer lands.
//
// What is deliberately deferred (out of this PR's scope, honest):
//   - Photo hash capture at upload time. Requires a hook in the photo-
//     intake pipeline when PRR-412 photo-delete SLA integration lands.
//   - Full DiagnosticOutcome snapshot at diagnostic-completion time.
//     Requires the photo-diagnostic endpoint (blocked on PRR-350
//     StepExtractionService → EPIC-PRR-H §3.1 MSP intake) to be wired
//     so an outcome-recent cache has something to read from at dispute
//     time.
//   - 30-day retention worker. When the upstream capture adds real
//     outcome/photo-hash content, the retention knob on the dispute
//     doc gets wired and a purge worker ships — today with no
//     outcome/photo-hash captured, there is nothing to age out.
//   - Agent role vs. ADMIN role split. v1 uses CenaAuthPolicies.AdminOnly
//     matching DisputeMetricsEndpoints + DiagnosticCreditEndpoints; a
//     SUPPORT_AGENT policy below ADMIN is a follow-up when the role
//     catalogue expands.
//
// Per memory "No stubs — production grade": every field below is
// deliberately nullable when the upstream writer has not yet run — not
// fabricated with placeholder values that the UI might render as
// truth. Per memory "Honest not complimentary": the task's DoD asks
// for photo + chain + narration + template; v1 ships the enveloping
// admin read endpoint with those fields wired as null awaiting the
// upstream capture. PRR-390 closes as Partial, not Done.
// =============================================================================

using System.Security.Claims;
using System.Text.Json.Serialization;
using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using ErrorCategory = Cena.Infrastructure.Errors.ErrorCategory;

namespace Cena.Admin.Api.Host.Endpoints;

/// <summary>
/// Wire shape for GET /api/admin/diagnostic-disputes/{disputeId}/audit.
/// Fields are JSON-named with snake-ish lowerCamelCase to match the
/// neighbouring admin endpoints' style.
/// </summary>
/// <param name="DisputeId">GUID of the dispute record.</param>
/// <param name="DiagnosticId">Diagnostic-run id under dispute. Opaque pointer
///     to the upstream photo-diagnostic outcome stream.</param>
/// <param name="StudentSubjectIdHash">Hashed student id — no cleartext PII.</param>
/// <param name="Reason">Categorical reason (<c>WrongNarration</c> /
///     <c>WrongStepIdentified</c> / <c>OcrMisread</c> / <c>Other</c>).</param>
/// <param name="StudentComment">Optional free-text (server-capped at 1000 chars).</param>
/// <param name="Status">Current lifecycle status.</param>
/// <param name="SubmittedAtUtc">UTC timestamp of dispute submission.</param>
/// <param name="ReviewedAtUtc">Null until a reviewer takes action.</param>
/// <param name="ReviewerNote">Optional reviewer comment.</param>
/// <param name="PhotoHash">
/// SHA-256 of the original upload. <strong>Null in v1</strong> until the
/// upstream capture-at-upload hook lands (see file banner).
/// </param>
/// <param name="CapturedOutcomeJson">
/// Serialized <c>DiagnosticOutcome</c> captured at diagnostic completion.
/// <strong>Null in v1</strong> until the upstream outcome-recent cache
/// lands and the photo-diagnostic endpoint (blocked on PRR-350) is wired.
/// </param>
/// <param name="MatchedTemplateId">Null in v1; fills in with CapturedOutcomeJson.</param>
/// <param name="FirstWrongStepNumber">Null in v1; fills in with CapturedOutcomeJson.</param>
public sealed record DiagnosticAuditResponseDto(
    [property: JsonPropertyName("disputeId")] string DisputeId,
    [property: JsonPropertyName("diagnosticId")] string DiagnosticId,
    [property: JsonPropertyName("studentSubjectIdHash")] string StudentSubjectIdHash,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("studentComment")] string? StudentComment,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("submittedAtUtc")] DateTimeOffset SubmittedAtUtc,
    [property: JsonPropertyName("reviewedAtUtc")] DateTimeOffset? ReviewedAtUtc,
    [property: JsonPropertyName("reviewerNote")] string? ReviewerNote,
    [property: JsonPropertyName("photoHash")] string? PhotoHash,
    [property: JsonPropertyName("capturedOutcomeJson")] string? CapturedOutcomeJson,
    [property: JsonPropertyName("matchedTemplateId")] string? MatchedTemplateId,
    [property: JsonPropertyName("firstWrongStepNumber")] int? FirstWrongStepNumber,
    [property: JsonPropertyName("deferredFields")] IReadOnlyList<string> DeferredFields);

/// <summary>
/// Admin-only detail read for a single disputed diagnostic. Support
/// dashboard consumer.
/// </summary>
public static class DiagnosticAuditEndpoints
{
    /// <summary>Canonical route.</summary>
    public const string Route = "/api/admin/diagnostic-disputes/{disputeId}/audit";

    /// <summary>Logger-category marker; public so tests can inject a logger against it.</summary>
    public sealed class DiagnosticAuditMarker { }

    /// <summary>
    /// Fields flagged as deferred in v1 responses so the UI renders a
    /// "pending upstream capture" state rather than a fake value.
    /// Order matches the DTO field declaration order.
    /// </summary>
    public static readonly IReadOnlyList<string> V1DeferredFields = new[]
    {
        "photoHash",
        "capturedOutcomeJson",
        "matchedTemplateId",
        "firstWrongStepNumber",
    };

    /// <summary>Register the endpoint.</summary>
    public static IEndpointRouteBuilder MapDiagnosticAuditEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleGetAsync)
            .WithName("GetDiagnosticAudit")
            .WithTags("Admin", "PhotoDiagnostic", "Support")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .Produces<DiagnosticAuditResponseDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden)
            .Produces<CenaError>(StatusCodes.Status404NotFound);
        return app;
    }

    /// <summary>Exposed as public so tests can invoke the handler directly.</summary>
    public static async Task<IResult> HandleGetAsync(
        [FromRoute] string disputeId,
        HttpContext http,
        [FromServices] IDiagnosticDisputeService disputes,
        [FromServices] ILogger<DiagnosticAuditMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(disputeId))
        {
            return Results.BadRequest(new CenaError(
                Code: "missing-disputeId",
                Message: "disputeId is required.",
                Category: ErrorCategory.Validation,
                Details: null,
                CorrelationId: null));
        }

        var view = await disputes.GetAsync(disputeId, ct).ConfigureAwait(false);
        if (view is null)
        {
            return Results.Json(
                new CenaError(
                    Code: "dispute-not-found",
                    Message: $"No dispute found with id '{disputeId}'.",
                    Category: ErrorCategory.NotFound,
                    Details: null,
                    CorrelationId: null),
                statusCode: StatusCodes.Status404NotFound);
        }

        // SIEM trace so support-agent reads of disputed student diagnostics
        // are auditable end-to-end. The AdminActionAuditMiddleware on
        // /api/admin/** captures the HTTP-level read already; this
        // structured log adds the disputeId → diagnosticId mapping that
        // lets SOC correlate across layers.
        var adminId = http.User.FindFirstValue("sub")
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? "unknown";
        logger.LogInformation(
            "[SIEM] DiagnosticAuditRead: admin={AdminIdPrefix} disputeId={DisputeId} diagId={DiagId}",
            adminId.Length <= 8 ? adminId : adminId[..8] + "…",
            view.DisputeId,
            view.DiagnosticId);

        return Results.Ok(new DiagnosticAuditResponseDto(
            DisputeId: view.DisputeId,
            DiagnosticId: view.DiagnosticId,
            StudentSubjectIdHash: view.StudentSubjectIdHash,
            Reason: view.Reason.ToString(),
            StudentComment: view.StudentComment,
            Status: view.Status.ToString(),
            SubmittedAtUtc: view.SubmittedAt,
            ReviewedAtUtc: view.ReviewedAt,
            ReviewerNote: view.ReviewerNote,
            // V1 nullables — upstream capture writers fill these in later.
            // Never fabricate values here (memory "No stubs — production grade").
            PhotoHash: null,
            CapturedOutcomeJson: null,
            MatchedTemplateId: null,
            FirstWrongStepNumber: null,
            DeferredFields: V1DeferredFields));
    }
}
