// =============================================================================
// Cena Platform — Hard-cap support-ticket endpoints (EPIC-PRR-J PRR-402)
//
// Three routes wire the student-facing "contact support at hard cap" flow
// and the matching admin triage surface to the HardCapSupportService.
//
//   POST /api/me/hard-cap-support-tickets                   (authenticated student)
//   POST /api/admin/hard-cap-support-tickets/{id}/resolve   (ADMIN)
//   POST /api/admin/hard-cap-support-tickets/{id}/reject    (ADMIN)
//
// Discipline notes (ship-gate + IDOR):
//   - Student endpoint derives the student id from the JWT claim, never
//     from a body field. Same IDOR guard pattern as DiagnosticDisputeEndpoints.
//   - Response payloads carry error codes + ticket ids only — no scarcity
//     copy, no countdowns (ship-gate banned terms, GD-004 scanner). The UI
//     layer renders the localized "we'll reach out" copy from i18n bundles.
//   - Admin routes accept only validated, bounded input. ExtensionCount is
//     further constrained by HardCapSupportService (1..100 abuse window).
//   - Student endpoint refuses to open a ticket unless PhotoDiagnosticQuotaGate
//     actually reports HardCapReached for the caller right now. That keeps
//     the Open queue scoped to the cases support is meant to handle and
//     prevents a malicious client from flooding the queue.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Cena.Actors.Subscriptions;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Compliance.KeyStore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Student.Api.Host.Endpoints;

/// <summary>Wire-format request/response for the student-open endpoint.</summary>
public sealed record HardCapSupportTicketRequestDto(string? Reason);

/// <summary>Persisted response shape.</summary>
public sealed record HardCapSupportTicketResponseDto(
    string TicketId,
    string Status,
    string MonthlyWindow,
    int UploadCountAtRequest,
    DateTimeOffset RequestedAtUtc);

/// <summary>Admin-resolve request payload.</summary>
public sealed record HardCapSupportResolveRequestDto(int ExtensionCount);

public static class HardCapSupportTicketEndpoints
{
    /// <summary>
    /// Register <c>POST /api/me/hard-cap-support-tickets</c> (student) plus
    /// the two admin routes.
    /// </summary>
    public static IEndpointRouteBuilder MapHardCapSupportTicketEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/me/hard-cap-support-tickets", OpenAsStudentAsync)
            .WithTags("PhotoDiagnostic", "Support")
            .RequireAuthorization()
            .WithName("OpenHardCapSupportTicket")
            .WithSummary(
                "Open a support ticket when the Premium hard cap has been reached.");

        app.MapPost("/api/admin/hard-cap-support-tickets/{id}/resolve", ResolveAsync)
            .WithTags("Admin", "PhotoDiagnostic", "Support")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .WithName("ResolveHardCapSupportTicket")
            .WithSummary(
                "Grant a one-time month-end extension on a hard-cap support ticket.");

        app.MapPost("/api/admin/hard-cap-support-tickets/{id}/reject", RejectAsync)
            .WithTags("Admin", "PhotoDiagnostic", "Support")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .WithName("RejectHardCapSupportTicket")
            .WithSummary(
                "Reject a hard-cap support ticket (no extension granted).");

        return app;
    }

    private static async Task<IResult> OpenAsStudentAsync(
        HttpContext http,
        [FromBody] HardCapSupportTicketRequestDto body,
        [FromServices] IHardCapSupportService service,
        [FromServices] IPhotoDiagnosticQuotaGate quotaGate,
        [FromServices] IStudentEntitlementResolver entitlements,
        [FromServices] IPhotoDiagnosticMonthlyUsage usage,
        [FromServices] TimeProvider clock,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var logger = loggerFactory.CreateLogger("HardCapSupportTicketEndpoints");

        // IDOR guard: student id comes from the JWT, never the body.
        var studentIdRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(studentIdRaw))
        {
            return Results.Unauthorized();
        }

        var studentSubjectIdHash = InMemorySubjectKeyStore.HashSubjectForLog(studentIdRaw);
        var now = clock.GetUtcNow();

        // Gate: we only accept the ticket when the student is actually at
        // the hard cap right now. Any other state means the UI surfaced the
        // CTA incorrectly or a client is probing the endpoint — both cases
        // get a 400 so the open queue doesn't fill with noise.
        var decision = await quotaGate.CheckAsync(studentSubjectIdHash, now, ct).ConfigureAwait(false);
        if (decision.Decision != CapDecision.HardCapReached)
        {
            return Results.BadRequest(new
            {
                error = "hard_cap_not_reached",
                decision = decision.Decision.ToString(),
            });
        }

        // Resolve the tier + parent for the persisted row. The entitlement
        // view is the source of truth for both — not the client and not a
        // cached header.
        var entitlement = await entitlements.ResolveAsync(studentSubjectIdHash, ct).ConfigureAwait(false);
        var rawUsage = await usage.GetAsync(studentSubjectIdHash, now, ct).ConfigureAwait(false);
        var monthWindow = MonthlyUsageKey.For(now);

        try
        {
            var ticket = await service.OpenTicketAsync(
                studentSubjectIdHash: studentSubjectIdHash,
                parentSubjectIdEncrypted: entitlement.SourceParentSubjectIdEncrypted,
                tier: entitlement.EffectiveTier,
                uploadCount: rawUsage,
                monthWindow: monthWindow,
                reason: body.Reason,
                ct: ct).ConfigureAwait(false);

            logger.LogInformation(
                "[PRR-402] Hard-cap support ticket opened: ticketId={TicketId} "
                + "student={StudentHashPrefix} tier={Tier} month={Month} rawUsage={Usage}",
                ticket.Id,
                studentSubjectIdHash.Length > 8
                    ? studentSubjectIdHash[..8] : studentSubjectIdHash,
                entitlement.EffectiveTier,
                monthWindow,
                rawUsage);

            return Results.Created(
                $"/api/me/hard-cap-support-tickets/{ticket.Id}",
                new HardCapSupportTicketResponseDto(
                    TicketId: ticket.Id,
                    Status: ticket.Status.ToString(),
                    MonthlyWindow: ticket.MonthlyWindow,
                    UploadCountAtRequest: ticket.UploadCountAtRequest,
                    RequestedAtUtc: ticket.RequestedAtUtc));
        }
        catch (ArgumentException ex)
        {
            // Tier-not-Premium, overlong reason, etc.
            return Results.BadRequest(new { error = "invalid_ticket", detail = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Duplicate open ticket in the same month.
            return Results.Conflict(new { error = "ticket_already_open", detail = ex.Message });
        }
    }

    private static async Task<IResult> ResolveAsync(
        HttpContext http,
        [FromRoute] string id,
        [FromBody] HardCapSupportResolveRequestDto body,
        [FromServices] IHardCapSupportService service,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var logger = loggerFactory.CreateLogger("HardCapSupportTicketEndpoints");

        if (string.IsNullOrWhiteSpace(id))
            return Results.BadRequest(new { error = "ticket_id_required" });

        var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(adminId))
            return Results.Unauthorized();

        try
        {
            var ticket = await service.ResolveWithExtensionAsync(
                ticketId: id,
                adminSubjectId: adminId,
                extensionCount: body.ExtensionCount,
                ct: ct).ConfigureAwait(false);

            logger.LogInformation(
                "[PRR-402] Hard-cap support ticket resolved: ticketId={TicketId} "
                + "admin={Admin} extension={Extension}",
                id, adminId, body.ExtensionCount);

            return Results.Ok(new HardCapSupportTicketResponseDto(
                TicketId: ticket.Id,
                Status: ticket.Status.ToString(),
                MonthlyWindow: ticket.MonthlyWindow,
                UploadCountAtRequest: ticket.UploadCountAtRequest,
                RequestedAtUtc: ticket.RequestedAtUtc));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.BadRequest(new
            {
                error = "extension_count_out_of_range",
                detail = ex.Message,
                min = HardCapSupportService.MinGrantCount,
                max = HardCapSupportService.MaxGrantCount,
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = "invalid_request", detail = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Not found OR not Open — both map to 409 because the support
            // agent's view of the world is stale either way.
            return Results.Conflict(new { error = "ticket_not_open", detail = ex.Message });
        }
    }

    private static async Task<IResult> RejectAsync(
        HttpContext http,
        [FromRoute] string id,
        [FromServices] IHardCapSupportService service,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("HardCapSupportTicketEndpoints");
        if (string.IsNullOrWhiteSpace(id))
            return Results.BadRequest(new { error = "ticket_id_required" });

        var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(adminId))
            return Results.Unauthorized();

        try
        {
            var ticket = await service.RejectAsync(
                ticketId: id,
                adminSubjectId: adminId,
                ct: ct).ConfigureAwait(false);

            logger.LogInformation(
                "[PRR-402] Hard-cap support ticket rejected: ticketId={TicketId} admin={Admin}",
                id, adminId);

            return Results.Ok(new HardCapSupportTicketResponseDto(
                TicketId: ticket.Id,
                Status: ticket.Status.ToString(),
                MonthlyWindow: ticket.MonthlyWindow,
                UploadCountAtRequest: ticket.UploadCountAtRequest,
                RequestedAtUtc: ticket.RequestedAtUtc));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = "invalid_request", detail = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = "ticket_not_open", detail = ex.Message });
        }
    }
}
