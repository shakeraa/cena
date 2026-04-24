// =============================================================================
// Cena Platform — Tutor-handoff report endpoint (EPIC-PRR-I PRR-325)
//
// POST /api/me/tutor-handoff-report
//
// Authenticated parent only. Premium feature fence
// (TierFeature.TutorHandoffPdf via SkuFeatureAuthorizer.CheckParent).
// Returns text/html with the self-contained handoff document; the
// consumer prints to PDF client-side, or a follow-up commit layers a
// server-side PDF wrap on the same endpoint without changing this
// controller.
//
// IDOR guard:
//   The request's StudentSubjectIdEncrypted must appear in the calling
//   parent's SubscriptionState.LinkedStudents. If it does not, 403 with
//   error=student_not_linked (the server does NOT confirm or deny the
//   existence of the id elsewhere — returning 404 here would be an
//   oracle for id-enumeration).
//
// Feature fence:
//   SkuFeatureAuthorizer.CheckParent(state, TierFeature.TutorHandoffPdf)
//   gates access. On deny, 403 with error=tier_required + requiredTier
//   so the UI can upsell. Consistent with the PRR-320 parent-dashboard
//   endpoint wire shape.
//
// Scope note:
//   This endpoint ships the HTML form of the artefact (PRR-325-backend).
//   Server-side PDF rendering is an explicit follow-up (license-review
//   decision on QuestPDF / iText / PdfSharp). The HTML is already the
//   parent-facing deliverable — they can print-to-PDF from any browser
//   or email the .html forward. Rationale documented in
//   TutorHandoffReportDto file banner.
// =============================================================================

using System.Security.Claims;
using System.Text;
using Cena.Actors.Subscriptions;
using Cena.Api.Contracts.Parenting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Student.Api.Host.Endpoints;

/// <summary>Minimal-API endpoint for the tutor-handoff HTML report.</summary>
public static class TutorHandoffEndpoints
{
    /// <summary>Register <c>POST /api/me/tutor-handoff-report</c>.</summary>
    public static IEndpointRouteBuilder MapTutorHandoffEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/me/tutor-handoff-report", PostReport)
            .WithTags("Subscriptions", "Parenting")
            .RequireAuthorization()
            .WithName("PostTutorHandoffReport");
        return app;
    }

    private static async Task<IResult> PostReport(
        HttpContext http,
        [FromBody] TutorHandoffReportRequestDto request,
        [FromServices] ISubscriptionAggregateStore store,
        [FromServices] ITutorHandoffCardSource cardSource,
        [FromServices] ITutorHandoffHtmlRenderer renderer,
        [FromServices] TimeProvider clock,
        CancellationToken ct)
    {
        // ── 1. AuthN: identify the calling parent ───────────────────────────
        var parentId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? http.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(parentId))
        {
            return Results.Unauthorized();
        }

        // ── 2. Validate request shape up-front ──────────────────────────────
        //     (Assembler will re-validate too; fail-fast at the boundary so
        //      the client receives a clear 400 instead of an opaque 500.)
        if (request is null)
        {
            return Results.BadRequest(new { error = "invalid_request" });
        }
        if (string.IsNullOrWhiteSpace(request.StudentSubjectIdEncrypted))
        {
            return Results.BadRequest(new { error = "missing_student_id" });
        }
        if (string.IsNullOrWhiteSpace(request.Locale))
        {
            return Results.BadRequest(new { error = "missing_locale" });
        }

        // ── 3. Load the parent subscription + feature-fence the caller ──────
        var aggregate = await store.LoadAsync(parentId, ct);
        var state = aggregate.State;

        var decision = SkuFeatureAuthorizer.CheckParent(
            state, TierFeature.TutorHandoffPdf);
        if (!decision.Allowed)
        {
            return Results.Json(
                new
                {
                    error = "tier_required",
                    reason = decision.ReasonCode,
                    requiredTier = "Premium",
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        // ── 4. IDOR guard: student must be linked to THIS parent ────────────
        var linked = state.LinkedStudents.FirstOrDefault(s =>
            string.Equals(
                s.StudentSubjectIdEncrypted,
                request.StudentSubjectIdEncrypted,
                StringComparison.Ordinal));
        if (linked is null)
        {
            // 403 (not 404) so the endpoint does not become an enumeration
            // oracle: a caller cannot distinguish "id does not exist" from
            // "id exists but is on someone else's subscription".
            return Results.Json(
                new { error = "student_not_linked" },
                statusCode: StatusCodes.Status403Forbidden);
        }

        // ── 5. Build the card bundle + assemble the DTO + render HTML ───────
        var now = clock.GetUtcNow();
        // Normalise a null WindowStart here in addition to the assembler's
        // default so the card source sees a concrete window to query.
        var windowStart = request.WindowStart
                          ?? request.WindowEnd - TutorHandoffReportAssembler.DefaultWindow;

        var cards = await cardSource.BuildCardsAsync(
            linked, windowStart, request.WindowEnd, request.Locale, ct);

        var report = TutorHandoffReportAssembler.Assemble(request, cards, now);
        var html = renderer.RenderHtml(report);

        // UTF-8 for Hebrew / Arabic rendering correctness.
        return Results.Text(
            content: html,
            contentType: "text/html; charset=utf-8",
            contentEncoding: Encoding.UTF8);
    }
}
