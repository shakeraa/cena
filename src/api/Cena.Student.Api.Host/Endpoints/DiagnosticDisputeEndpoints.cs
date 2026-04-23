// =============================================================================
// Cena Platform — Diagnostic-dispute endpoints (EPIC-PRR-J PRR-385)
//
// Routes student-initiated "this diagnosis seems wrong" submissions into
// the support queue. IDiagnosticDisputeService does the heavy lifting:
// validates the command, generates the dispute id, persists the doc,
// records the accuracy-audit signal, bumps metrics. This file is the
// thin HTTP adapter that binds the route + auth + payload mapping.
//
// Auth: authenticated student only. The student id comes from the JWT's
// NameIdentifier claim (like every other /api/me endpoint). We never
// trust a client-supplied student id on the body — that would open an
// IDOR hole allowing one student to file disputes on another's
// diagnostic.
//
// Shipgate-compliant acknowledgement copy lives in the i18n bundles on
// the UI side; the server returns a DiagnosticDisputeResponseDto with
// the persisted dispute id + submittedAt so the UI can render a short
// "thanks, we'll review" confirmation. ETA copy is static on the client.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Cena.Infrastructure.Compliance.KeyStore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Student.Api.Host.Endpoints;

/// <summary>Wire-format request/response for the dispute endpoint.</summary>
public sealed record DiagnosticDisputeRequestDto(
    string DiagnosticId,
    string Reason,        // maps to DisputeReason enum; invalid → 400
    string? Comment);     // optional; capped at MaxCommentLength server-side

public sealed record DiagnosticDisputeResponseDto(
    string DisputeId,
    string Status,         // "New" on creation
    DateTimeOffset SubmittedAt);

/// <summary>DI helpers for wiring the dispute endpoint.</summary>
public static class DiagnosticDisputeEndpoints
{
    /// <summary>Map <c>POST /api/me/diagnostic-disputes</c>.</summary>
    public static IEndpointRouteBuilder MapDiagnosticDisputeEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me")
            .WithTags("DiagnosticDisputes")
            .RequireAuthorization();

        group.MapPost("diagnostic-disputes", SubmitAsync)
            .WithName("SubmitDiagnosticDispute")
            .WithSummary(
                "File a dispute on a diagnostic result (\"this doesn't look right\").");

        return app;
    }

    private static async Task<IResult> SubmitAsync(
        HttpContext http,
        [FromBody] DiagnosticDisputeRequestDto body,
        [FromServices] IDiagnosticDisputeService service,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var logger = loggerFactory.CreateLogger("DiagnosticDisputeEndpoints");

        if (string.IsNullOrWhiteSpace(body.DiagnosticId))
        {
            return Results.BadRequest(new { error = "diagnostic_id_required" });
        }
        if (!Enum.TryParse<DisputeReason>(body.Reason, ignoreCase: true, out var reason))
        {
            return Results.BadRequest(new
            {
                error = "invalid_reason",
                allowed = Enum.GetNames<DisputeReason>(),
            });
        }

        // IDOR guard: the student id is the authenticated subject,
        // never a body field. DiagnosticDisputeService uses the hash
        // directly — we pass through the claim value the same way
        // every other /api/me endpoint does.
        var studentIdRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(studentIdRaw))
        {
            return Results.Unauthorized();
        }
        var studentSubjectIdHash = InMemorySubjectKeyStore.HashSubjectForLog(studentIdRaw);

        try
        {
            var view = await service.SubmitAsync(
                new SubmitDiagnosticDisputeCommand(
                    DiagnosticId: body.DiagnosticId,
                    StudentSubjectIdHash: studentSubjectIdHash,
                    Reason: reason,
                    StudentComment: body.Comment),
                ct).ConfigureAwait(false);

            logger.LogInformation(
                "[PRR-385] Dispute filed: diagnosticId={DiagnosticId} "
                + "student={StudentHashPrefix} reason={Reason} disputeId={DisputeId}",
                body.DiagnosticId,
                studentSubjectIdHash.Length > 8
                    ? studentSubjectIdHash[..8] : studentSubjectIdHash,
                reason,
                view.DisputeId);

            return Results.Ok(new DiagnosticDisputeResponseDto(
                DisputeId: view.DisputeId,
                Status: view.Status.ToString(),
                SubmittedAt: view.SubmittedAt));
        }
        catch (ArgumentException ex)
        {
            // Service surfaces validation failures (e.g., comment too
            // long) as ArgumentException; map to 400 with a stable
            // error code the UI can localize.
            return Results.BadRequest(new
            {
                error = "invalid_dispute",
                detail = ex.Message,
            });
        }
    }
}
