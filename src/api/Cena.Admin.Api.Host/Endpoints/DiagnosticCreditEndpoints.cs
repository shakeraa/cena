// =============================================================================
// Cena Platform — DiagnosticCreditEndpoints (EPIC-PRR-J PRR-391)
//
// Admin-only REST endpoint that a support agent calls from the admin UI
// when they've reviewed a photo-diagnostic dispute and confirmed it is a
// real system error (bad narration, wrong step, OCR misread). The call
// flips the dispute to Upheld, writes a credit-ledger row (free upload-
// quota bump), and fires the apology dispatcher.
//
// Route
//   POST /api/admin/diagnostic-disputes/{disputeId}/credit
//   Body: { "uploadQuotaBumpCount": int, "reason": string? }
//
// Auth: ADMIN or SUPER_ADMIN. Matches the pattern used by
// InstitutePricingOverrideEndpoint — role-based, written as an assertion
// so both claim shapes ("role" and ClaimTypes.Role) resolve. The admin
// subject id is read off the authenticated principal and stored on the
// ledger row (CreditedBy) for the audit trail.
//
// Error shape
//   400 missing-disputeId            – empty route id
//   400 upload-bump-out-of-range     – count outside [1, 50]
//   400 reason-too-long              – reason exceeds 500 chars
//   404 dispute-not-found            – dispute id does not resolve
//   409 already-upheld               – idempotency guard; credit was
//                                      previously issued on this dispute
//
// Why the endpoint is thin: all orchestration lives in
// DiagnosticCreditService. The endpoint only translates ArgumentException /
// InvalidOperationException into HTTP codes so tests of the service pin
// behaviour without a pipeline and tests of the endpoint only pin the
// translation layer.
// =============================================================================

using System.Security.Claims;
using System.Text.Json.Serialization;
using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Host.Endpoints;

/// <summary>Request body for the credit endpoint.</summary>
public sealed record IssueDiagnosticCreditRequest(
    [property: JsonPropertyName("uploadQuotaBumpCount")]
    int UploadQuotaBumpCount,
    [property: JsonPropertyName("reason")]
    string? Reason);

/// <summary>Response envelope on a successful credit issuance.</summary>
public sealed record IssueDiagnosticCreditResponse(
    [property: JsonPropertyName("creditId")]
    string CreditId,
    [property: JsonPropertyName("disputeId")]
    string DisputeId,
    [property: JsonPropertyName("studentSubjectIdHash")]
    string StudentSubjectIdHash,
    [property: JsonPropertyName("creditKind")]
    string CreditKind,
    [property: JsonPropertyName("uploadQuotaBumpCount")]
    int UploadQuotaBumpCount,
    [property: JsonPropertyName("issuedAtUtc")]
    DateTimeOffset IssuedAtUtc,
    [property: JsonPropertyName("creditedBy")]
    string CreditedBy,
    [property: JsonPropertyName("reason")]
    string Reason);

public static class DiagnosticCreditEndpoints
{
    /// <summary>Canonical route.</summary>
    public const string Route = "/api/admin/diagnostic-disputes/{disputeId}/credit";

    internal sealed class DiagnosticCreditMarker { }

    public static IEndpointRouteBuilder MapDiagnosticCreditEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(Route, HandlePostAsync)
            .WithName("IssueDiagnosticCredit")
            .WithTags("Admin", "PhotoDiagnostic")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly);
        return app;
    }

    internal static async Task<IResult> HandlePostAsync(
        [FromRoute] string disputeId,
        [FromBody] IssueDiagnosticCreditRequest request,
        HttpContext http,
        [FromServices] IDiagnosticCreditService creditService,
        [FromServices] ILogger<DiagnosticCreditMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(disputeId))
            return Results.BadRequest(new { error = "missing-disputeId" });
        ArgumentNullException.ThrowIfNull(request);

        if (request.UploadQuotaBumpCount < DiagnosticCreditService.MinUploadQuotaBumpCount
            || request.UploadQuotaBumpCount > DiagnosticCreditService.MaxUploadQuotaBumpCount)
        {
            return Results.BadRequest(new
            {
                error = "upload-bump-out-of-range",
                min = DiagnosticCreditService.MinUploadQuotaBumpCount,
                max = DiagnosticCreditService.MaxUploadQuotaBumpCount,
                provided = request.UploadQuotaBumpCount,
            });
        }
        if (request.Reason is { } providedReason
            && providedReason.Length > DiagnosticCreditService.MaxReasonLength)
        {
            return Results.BadRequest(new
            {
                error = "reason-too-long",
                maxChars = DiagnosticCreditService.MaxReasonLength,
            });
        }

        var adminSubjectId = ResolveAdminSubjectId(http);

        DiagnosticCreditLedgerDocument ledger;
        try
        {
            ledger = await creditService.IssueCreditAsync(
                disputeId,
                adminSubjectId,
                request.UploadQuotaBumpCount,
                request.Reason ?? string.Empty,
                ct).ConfigureAwait(false);
        }
        catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "uploadQuotaBumpCount")
        {
            // Belt-and-braces: the endpoint-side check already fired, but
            // if the service bounds change we translate the service-level
            // guard the same way.
            return Results.BadRequest(new { error = "upload-bump-out-of-range", message = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "[prr-391] credit-issue 404 dispute={DisputeId} admin={Admin}",
                disputeId, adminSubjectId);
            return Results.NotFound(new { error = "dispute-not-found", disputeId });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already Upheld", StringComparison.Ordinal))
        {
            logger.LogInformation(
                "[prr-391] credit-issue 409 dispute={DisputeId} admin={Admin}",
                disputeId, adminSubjectId);
            return Results.Conflict(new { error = "already-upheld", disputeId });
        }

        return Results.Ok(new IssueDiagnosticCreditResponse(
            CreditId: ledger.Id,
            DisputeId: ledger.DisputeId,
            StudentSubjectIdHash: ledger.StudentSubjectIdHash,
            CreditKind: ledger.CreditKind.ToString(),
            UploadQuotaBumpCount: ledger.UploadQuotaBumpCount,
            IssuedAtUtc: ledger.IssuedAtUtc,
            CreditedBy: ledger.CreditedBy,
            Reason: ledger.Reason));
    }

    private static string ResolveAdminSubjectId(HttpContext http)
    {
        return http.User.FindFirst("user_id")?.Value
            ?? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? http.User.Identity?.Name
            ?? "unknown-admin";
    }
}
