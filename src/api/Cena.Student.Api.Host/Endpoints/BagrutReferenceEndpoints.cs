// =============================================================================
// Cena Platform — Bagrut Reference Library Endpoints (PRR-267 R3)
//
// Student-facing reference-library surface for past Ministry Bagrut papers.
// All four endpoints are gated by the existing Cena:Variants:BagrutSeedToLlmEnabled
// flag (the same flag that guards the source-anchored variant generation flow);
// when off, the endpoints return 503. ADR-0059 §15.5 carve-out + PRR-249 user-
// accepted posture (memory: project_bagrut_seeded_variants_state).
//
// Routes:
//
//   POST   /api/v1/reference/consent
//          Body: { disclosureVersion: string }
//          Issues a BagrutReferenceConsentGranted_V1 on the calling student's
//          consent stream (idempotent — re-grants are no-ops at the projection
//          level; the audit row appends). Returns the 24h wire HMAC token.
//
//   DELETE /api/v1/reference/consent
//          Issues a BagrutReferenceConsentRevoked_V1 on the calling student's
//          consent stream. Returns 204 + invalidates any cached wire token
//          on the next /papers fetch (the issuance gate consults the fact).
//
//   GET    /api/v1/reference/papers
//          Lists Ministry past papers filtered to the calling student's
//          active ExamTarget.QuestionPaperCodes (ADR-0050 §1). 401 when
//          unauthenticated; 403 when no ExamTarget is configured; 503 when
//          the flag is off.
//
//   GET    /api/v1/reference/papers/{paperCode}/{year}/{moed}
//          Returns the full paper as a Reference<QuestionDto> sequence.
//          Each render emits BagrutReferenceItemRendered_V1 to the consent
//          stream for ADR-0059 §15.7 retention + RTBF cascade.
//
// Authorization: every endpoint runs ResourceOwnershipGuard.VerifyStudentAccess
// against the studentId derived from JWT claims, so callers cannot consult
// or render on behalf of another student.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Consent;
using Cena.Actors.Consent.Events;
using Cena.Actors.Content;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Student.Api.Host.Endpoints;

public static class BagrutReferenceEndpoints
{
    /// <summary>
    /// Functional retention horizon on the granted consent fact. Wire
    /// tokens are HMAC-stamped against this — when the fact is older
    /// than this, the issuance gate refuses to silently re-issue and
    /// re-prompts the student. Per ADR-0059 §15.3.
    /// </summary>
    public static readonly TimeSpan ConsentFunctionalTtl = TimeSpan.FromDays(90);

    public static IEndpointRouteBuilder MapBagrutReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reference")
            .WithTags("Bagrut Reference Library")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        group.MapPost("/consent", IssueConsentAsync).WithName("BagrutReferenceConsentGrant")
            .Produces<BagrutReferenceConsentGrantResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        group.MapDelete("/consent", RevokeConsentAsync).WithName("BagrutReferenceConsentRevoke")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/papers", ListPapersAsync).WithName("BagrutReferenceListPapers")
            .Produces<BagrutReferencePapersResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/papers/{paperCode}/{year:int}/{moed}", GetPaperAsync).WithName("BagrutReferenceGetPaper")
            .Produces<BagrutReferencePaperResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    // ---- handlers ----

    private static async Task<IResult> IssueConsentAsync(
        BagrutReferenceConsentGrantRequest request,
        HttpContext ctx,
        IConfiguration config,
        IConsentAggregateStore consents,
        IBagrutReferenceConsentTokenService tokens,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!IsFlagEnabled(config))
            return Disabled();

        var studentId = ResolveStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();
        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        var now = timeProvider.GetUtcNow();
        var grant = new BagrutReferenceConsentGranted_V1(
            StudentId: studentId,
            GrantedAt: now,
            DisclosureVersion: string.IsNullOrWhiteSpace(request.DisclosureVersion)
                ? "v1"
                : request.DisclosureVersion.Trim(),
            UserAgent: ctx.Request.Headers.UserAgent.ToString().Length > 0
                ? ctx.Request.Headers.UserAgent.ToString()
                : null,
            IpAddressHash: null /* derived elsewhere if needed; left null to avoid IP capture w/o config */);

        await consents.AppendAsync(studentId, grant, cancellationToken);

        // Wire token — context defaults to BrowseLibrary; a separate
        // VariantSourceCitation token is issued at variant-render time
        // by the variant flow, scoped to that context only (§15.3).
        var token = tokens.Issue(studentId, ReferenceContextKind.BrowseLibrary, now);

        return Results.Ok(new BagrutReferenceConsentGrantResponse(
            ConsentToken: token,
            FunctionalRetentionDays: (int)ConsentFunctionalTtl.TotalDays));
    }

    private static async Task<IResult> RevokeConsentAsync(
        HttpContext ctx,
        IConsentAggregateStore consents,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // Revoke is allowed even when the flag is off — students must always
        // be able to walk back consent (ADR-0059 §15.3 — "one-click revoke
        // on the reference page itself" is normative).
        var studentId = ResolveStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();
        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        var revoke = new BagrutReferenceConsentRevoked_V1(
            StudentId: studentId,
            RevokedAt: timeProvider.GetUtcNow(),
            Reason: "user-initiated");

        await consents.AppendAsync(studentId, revoke, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ListPapersAsync(
        HttpContext ctx,
        IConfiguration config,
        IConsentAggregateStore consents,
        IDocumentStore store,
        IBagrutReferenceConsentTokenService tokens,
        TimeProvider timeProvider,
        ILogger<BagrutReferenceListLogMarker> logger,
        CancellationToken cancellationToken)
    {
        if (!IsFlagEnabled(config))
            return Disabled();

        var studentId = ResolveStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();
        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        var aggregate = await consents.LoadAsync(studentId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (aggregate.State.BagrutReference is null
            || !aggregate.State.BagrutReference.IsActive(now, ConsentFunctionalTtl))
        {
            return Results.Json(
                new { error = "Bagrut reference consent required",
                      code = "BAGRUT_REFERENCE_CONSENT_REQUIRED" },
                statusCode: StatusCodes.Status403Forbidden);
        }

        // Filter to the student's active ExamTarget.QuestionPaperCodes per
        // ADR-0050 §1 / PRR-243. The active-plan reader lives on the
        // student-plan side; if no plan exists, return an empty list
        // rather than 403 — same as the BrowseLibrary spec § Q5.
        var paperCodes = await ResolveQuestionPaperCodesAsync(
            studentId, store, cancellationToken);

        if (paperCodes.Count == 0)
        {
            return Results.Ok(new BagrutReferencePapersResponse(
                Papers: Array.Empty<BagrutReferencePaperSummary>()));
        }

        await using var session = store.QuerySession();
        var papers = await session.Query<BagrutCorpusItemDocument>()
            .Where(p => p.MinistryQuestionPaperCode.IsOneOf(paperCodes.ToArray()))
            .ToListAsync(cancellationToken);

        // Group by (paperCode, year, moed) — one summary per sitting.
        var summaries = papers
            .GroupBy(p => (p.MinistryQuestionPaperCode, p.Year, p.Moed))
            .Select(g => new BagrutReferencePaperSummary(
                PaperCode: g.Key.MinistryQuestionPaperCode,
                Year: g.Key.Year,
                Moed: g.Key.Moed,
                Season: g.First().Season.ToString(),
                Track: g.First().TrackKey,
                QuestionCount: g.Count()))
            .OrderByDescending(s => s.Year)
            .ThenBy(s => s.PaperCode)
            .ToList();

        // Re-issue a fresh wire token on every list call — cheap, keeps
        // the wire TTL rolling for the active session without forcing
        // the student to re-disclose.
        var token = tokens.Issue(studentId, ReferenceContextKind.BrowseLibrary, now);

        return Results.Ok(new BagrutReferencePapersResponse(summaries) { ConsentToken = token });
    }

    private static async Task<IResult> GetPaperAsync(
        string paperCode,
        int year,
        string moed,
        HttpContext ctx,
        IConfiguration config,
        IConsentAggregateStore consents,
        IDocumentStore store,
        IBagrutReferenceConsentTokenService tokens,
        TimeProvider timeProvider,
        ILogger<BagrutReferenceListLogMarker> logger,
        CancellationToken cancellationToken)
    {
        if (!IsFlagEnabled(config))
            return Disabled();

        var studentId = ResolveStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();
        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        var aggregate = await consents.LoadAsync(studentId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (aggregate.State.BagrutReference is null
            || !aggregate.State.BagrutReference.IsActive(now, ConsentFunctionalTtl))
        {
            return Results.Json(
                new { error = "Bagrut reference consent required",
                      code = "BAGRUT_REFERENCE_CONSENT_REQUIRED" },
                statusCode: StatusCodes.Status403Forbidden);
        }

        // Filter to the student's active שאלון codes — defense-in-depth
        // against URL-tampering attempts to enumerate Ministry papers
        // outside the student's plan.
        var paperCodes = await ResolveQuestionPaperCodesAsync(
            studentId, store, cancellationToken);
        if (!paperCodes.Contains(paperCode))
        {
            return Results.Json(
                new { error = "Paper not in student's active ExamTarget",
                      code = "BAGRUT_REFERENCE_PAPER_OUT_OF_SCOPE" },
                statusCode: StatusCodes.Status403Forbidden);
        }

        await using var session = store.QuerySession();
        var items = await session.Query<BagrutCorpusItemDocument>()
            .Where(p => p.MinistryQuestionPaperCode == paperCode
                     && p.Year == year
                     && p.Moed == moed)
            .OrderBy(p => p.QuestionNumber)
            .ToListAsync(cancellationToken);

        if (items.Count == 0) return Results.NotFound();

        // Issue a fresh BrowseLibrary wire token + wrap each item in
        // Reference<T>. The wrapper's factory emits the BagrutReferenceBrowsed
        // audit log line per item (EventId 8009) and we ALSO append a
        // BagrutReferenceItemRendered_V1 event to the consent stream so
        // the retention worker (PRR-266 R2) can prune at 180d.
        var token = tokens.Issue(studentId, ReferenceContextKind.BrowseLibrary, now);
        var refs = new List<BagrutReferenceQuestion>(items.Count);
        foreach (var item in items)
        {
            var provenanceSource = StructuredProvenance(item);
            var provenance = new Provenance(
                ProvenanceKind.MinistryBagrut, now, provenanceSource);

            // Reference<QuestionDto>.From() validates token + emits 8009.
            var wrapped = Reference<BagrutReferenceQuestionDto>.From(
                value: new BagrutReferenceQuestionDto(
                    QuestionNumber: item.QuestionNumber,
                    PaperCode: item.MinistryQuestionPaperCode,
                    Year: item.Year,
                    Moed: item.Moed,
                    Season: item.Season.ToString()),
                provenance: provenance,
                consentToken: token,
                context: ReferenceContextKind.BrowseLibrary,
                auditLogger: logger,
                now: now,
                itemId: item.Id);

            // Stream-side retention event for the 180d worker.
            await consents.AppendAsync(studentId, new BagrutReferenceItemRendered_V1(
                StudentId: studentId,
                ItemId: item.Id,
                ProvenanceSource: provenanceSource,
                ContextKind: nameof(ReferenceContextKind.BrowseLibrary),
                ConsentTokenIssuedAt: token.IssuedAt.ToString("O"),
                RenderedAt: now), cancellationToken);

            refs.Add(new BagrutReferenceQuestion(
                Number: wrapped.Value.QuestionNumber,
                PaperCode: wrapped.Value.PaperCode,
                Year: wrapped.Value.Year,
                Moed: wrapped.Value.Moed,
                Season: wrapped.Value.Season,
                ProvenanceSource: provenanceSource));
        }

        return Results.Ok(new BagrutReferencePaperResponse(
            PaperCode: paperCode,
            Year: year,
            Moed: moed,
            Questions: refs,
            ConsentToken: token));
    }

    // ---- helpers ----

    private static bool IsFlagEnabled(IConfiguration config) =>
        config.GetValue<bool>("Cena:Variants:BagrutSeedToLlmEnabled");

    private static IResult Disabled() =>
        Results.Json(
            new { error = "Bagrut reference flow is disabled",
                  code = "BAGRUT_REFERENCE_DISABLED",
                  hint = "See docs/engineering/feature-flags.md" },
            statusCode: StatusCodes.Status503ServiceUnavailable);

    private static string? ResolveStudentId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.FindFirstValue("sub")
        ?? user.FindFirstValue("user_id");

    /// <summary>
    /// Slash-delimited Provenance.Source per ADR-0059 §14.2 item 5 —
    /// SIEM-tractable per-paper-code takedown form.
    /// </summary>
    private static string StructuredProvenance(BagrutCorpusItemDocument item) =>
        $"ministry-bagrut/{item.MinistryQuestionPaperCode}/{item.Year}/" +
        $"{item.Season.ToString().ToLowerInvariant()}/{item.Moed.ToLowerInvariant()}/" +
        $"q{item.QuestionNumber}";

    private static async Task<HashSet<string>> ResolveQuestionPaperCodesAsync(
        string studentId,
        IDocumentStore store,
        CancellationToken ct)
    {
        // Best-effort resolver — if the student-plan read-side isn't
        // wired in this host, fall back to "no codes" which surfaces as
        // an empty paper list (NOT 403 — see §Q5 in ADR-0059). Hosts
        // that wire IStudentPlanReader can swap this out for a
        // per-target read.
        try
        {
            await using var session = store.QuerySession();
            var plan = await session.LoadAsync<Cena.Actors.StudentPlan.StudentPlanState>(
                studentId, ct);
            if (plan is null) return new HashSet<string>();
            // ActiveTargets honours the ADR-0050 §1 lifecycle (skips
            // archived + completed targets). Falling back to Targets
            // would surface paper codes the student is no longer
            // preparing for, which §Q5 specifically forbids.
            return plan.ActiveTargets
                .SelectMany(t => t.QuestionPaperCodes ?? Array.Empty<string>())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>();
        }
    }
}

// ---- DTOs ----

public sealed record BagrutReferenceConsentGrantRequest(string DisclosureVersion);

public sealed record BagrutReferenceConsentGrantResponse(
    ConsentTokenId ConsentToken,
    int FunctionalRetentionDays);

public sealed record BagrutReferencePapersResponse(
    IReadOnlyList<BagrutReferencePaperSummary> Papers)
{
    public ConsentTokenId? ConsentToken { get; init; }
}

public sealed record BagrutReferencePaperSummary(
    string PaperCode,
    int Year,
    string Moed,
    string Season,
    string Track,
    int QuestionCount);

public sealed record BagrutReferencePaperResponse(
    string PaperCode,
    int Year,
    string Moed,
    IReadOnlyList<BagrutReferenceQuestion> Questions,
    ConsentTokenId ConsentToken);

public sealed record BagrutReferenceQuestion(
    int Number,
    string PaperCode,
    int Year,
    string Moed,
    string Season,
    string ProvenanceSource);

internal sealed record BagrutReferenceQuestionDto(
    int QuestionNumber,
    string PaperCode,
    int Year,
    string Moed,
    string Season);

/// <summary>Marker so the Reference factory's audit logger has a stable category name.</summary>
public sealed class BagrutReferenceListLogMarker { }
