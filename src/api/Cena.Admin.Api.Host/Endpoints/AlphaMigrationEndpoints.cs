// =============================================================================
// Cena Platform — AlphaMigrationEndpoints (EPIC-PRR-I PRR-344)
//
// Admin-only REST surface for the alpha-user migration workflow.
//
//   POST /api/admin/alpha-migration/seed     — overwrite the current seed
//                                               list (encrypted parent ids).
//   GET  /api/admin/alpha-migration/status   — seed size + granted markers
//                                               + pending count audit.
//   POST /api/admin/alpha-migration/run-now  — fire the worker immediately
//                                               so a fresh seed applies
//                                               without waiting for the
//                                               next cron tick.
//
// Why this exists. PRR-344 was blocked on "operator has no way to hand
// Cena the alpha-user list". AlphaUserMigrationWorker's CandidatesForGrace
// returned Array.Empty<string>() because there was no seam to ingest the
// list. This endpoint closes that gap: operators POST the list, the
// worker consumes it, the StudentEntitlementResolver honours the emitted
// grace markers during the 60-day window (ADR-0057 §alpha-migration +
// 2026-04-11 no-stubs memory).
//
// Auth: AdminOnly — matches UnitEconomicsAdminEndpoints + DisputeMetrics.
// ADMIN + SUPER_ADMIN roles may operate; anything below is rejected at the
// authorization layer. The /seed POST is audit-captured by the existing
// AdminActionAuditMiddleware on /api/admin/** so every upload is logged.
//
// Scope notes. The Vue admin page is a separate follow-up task; this
// endpoint ships the wire contract the page will consume. The request
// body carries ALREADY-encrypted parent subject ids — the admin UI is
// expected to run ids through the subject-key store client-side before
// POSTing (same contract every other admin write endpoint uses; see the
// ClassroomTargetEndpoints + StudentPlanMigrationEndpoints precedent).
// =============================================================================

using System.Security.Claims;
using System.Text.Json.Serialization;
using Cena.Actors.Subscriptions;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api.Host.Endpoints;

// ---- Wire DTOs --------------------------------------------------------------

/// <summary>Request body for POST /api/admin/alpha-migration/seed.</summary>
public sealed record AlphaMigrationSeedRequestDto(
    [property: JsonPropertyName("parentSubjectIdsEncrypted")]
    IReadOnlyList<string> ParentSubjectIdsEncrypted);

/// <summary>Response body for GET /api/admin/alpha-migration/status.</summary>
public sealed record AlphaMigrationStatusResponseDto(
    [property: JsonPropertyName("seedSize")] int SeedSize,
    [property: JsonPropertyName("grantedMarkerCount")] int GrantedMarkerCount,
    [property: JsonPropertyName("pendingGraceCount")] int PendingGraceCount,
    [property: JsonPropertyName("uploadedBy")] string? UploadedBy,
    [property: JsonPropertyName("uploadedAtUtc")] DateTimeOffset? UploadedAtUtc);

/// <summary>Response body for POST /api/admin/alpha-migration/run-now.</summary>
public sealed record AlphaMigrationRunResponseDto(
    [property: JsonPropertyName("grantedCount")] int GrantedCount);

// ---- Endpoint ---------------------------------------------------------------

/// <summary>
/// Wires the three alpha-migration admin routes. All three are
/// <see cref="CenaAuthPolicies.AdminOnly"/>.
/// </summary>
public static class AlphaMigrationEndpoints
{
    /// <summary>Canonical routes.</summary>
    public const string SeedRoute = "/api/admin/alpha-migration/seed";
    public const string StatusRoute = "/api/admin/alpha-migration/status";
    public const string RunNowRoute = "/api/admin/alpha-migration/run-now";

    /// <summary>Register the endpoints on the host's route builder.</summary>
    public static IEndpointRouteBuilder MapAlphaMigrationEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(SeedRoute, HandleSeedAsync)
            .WithName("PostAlphaMigrationSeed")
            .WithTags("Admin", "Subscriptions", "AlphaMigration")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden);

        app.MapGet(StatusRoute, HandleStatusAsync)
            .WithName("GetAlphaMigrationStatus")
            .WithTags("Admin", "Subscriptions", "AlphaMigration")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .Produces<AlphaMigrationStatusResponseDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden);

        app.MapPost(RunNowRoute, HandleRunNowAsync)
            .WithName("PostAlphaMigrationRunNow")
            .WithTags("Admin", "Subscriptions", "AlphaMigration")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .Produces<AlphaMigrationRunResponseDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden);

        return app;
    }

    /// <summary>
    /// Overwrite the seed list. Public so tests in sibling assemblies can
    /// drive it without a TestServer (matches the pattern of
    /// <see cref="UnitEconomicsAdminEndpoints.HandleGetAsync"/>).
    /// </summary>
    public static async Task<IResult> HandleSeedAsync(
        HttpContext ctx,
        [FromServices] IAlphaMigrationSeedSource seedSource,
        [FromBody] AlphaMigrationSeedRequestDto? request,
        CancellationToken ct)
    {
        if (request is null || request.ParentSubjectIdsEncrypted is null)
        {
            return Results.BadRequest(new { error = "parentSubjectIdsEncrypted is required" });
        }

        var uploadedBy = GetCallerSubjectId(ctx.User)
            ?? throw new InvalidOperationException(
                "Authenticated admin subject id missing — auth middleware misconfigured.");

        await seedSource.SeedAsync(
            request.ParentSubjectIdsEncrypted,
            uploadedBy,
            ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>
    /// Return the current seed size, granted-marker count, and pending
    /// grace count (ids in the seed that haven't yet been granted, i.e.
    /// await the next worker tick or /run-now).
    /// </summary>
    public static async Task<IResult> HandleStatusAsync(
        [FromServices] IAlphaMigrationSeedSource seedSource,
        [FromServices] IDocumentStore documentStore,
        CancellationToken ct)
    {
        var seed = await seedSource.GetSeedParentIdsAsync(ct).ConfigureAwait(false);

        await using var session = documentStore.QuerySession();
        var grantedMarkers = await session.Query<AlphaGraceMarker>()
            .Select(m => m.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var grantedSet = grantedMarkers.ToHashSet(StringComparer.Ordinal);

        var pending = seed.Count(id => !grantedSet.Contains(id));

        var (uploadedBy, uploadedAt) = await LoadUploadAuditAsync(
            seedSource, documentStore, ct).ConfigureAwait(false);

        return Results.Ok(new AlphaMigrationStatusResponseDto(
            SeedSize: seed.Count,
            GrantedMarkerCount: grantedMarkers.Count,
            PendingGraceCount: pending,
            UploadedBy: uploadedBy,
            UploadedAtUtc: uploadedAt));
    }

    /// <summary>
    /// Run the worker immediately. Returns how many new markers were
    /// written. Idempotent: a second call with the same seed and the same
    /// marker state returns <c>grantedCount=0</c>.
    /// </summary>
    public static async Task<IResult> HandleRunNowAsync(
        [FromServices] AlphaUserMigrationWorker worker,
        CancellationToken ct)
    {
        var count = await worker.RunMigrationOnceAsync(ct).ConfigureAwait(false);
        return Results.Ok(new AlphaMigrationRunResponseDto(GrantedCount: count));
    }

    // ---- Helpers ------------------------------------------------------------

    private static string? GetCallerSubjectId(ClaimsPrincipal user)
    {
        return user.FindFirstValue("sub")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    /// <summary>
    /// Best-effort audit lookup. The in-memory seed source exposes
    /// uploader / upload-at scalars directly; the Marten-backed variant
    /// carries them on the persisted document. Other custom impls fall
    /// back to nulls (still valid response shape).
    /// </summary>
    private static async Task<(string? UploadedBy, DateTimeOffset? UploadedAt)>
        LoadUploadAuditAsync(
            IAlphaMigrationSeedSource seedSource,
            IDocumentStore documentStore,
            CancellationToken ct)
    {
        if (seedSource is InMemoryAlphaMigrationSeedSource inMem)
        {
            var at = inMem.LastUploadedAt;
            return (string.IsNullOrEmpty(inMem.LastUploadedBy) ? null : inMem.LastUploadedBy,
                    at == default ? null : at);
        }
        if (seedSource is MartenAlphaMigrationSeedSource marten)
        {
            var doc = await marten.LoadDocumentAsync(ct).ConfigureAwait(false);
            if (doc is null) return (null, null);
            return (
                string.IsNullOrEmpty(doc.UploadedBy) ? null : doc.UploadedBy,
                doc.UploadedAtUtc == default ? null : doc.UploadedAtUtc);
        }
        // Unknown impl — no audit. Response shape still valid (null fields).
        return (null, null);
    }
}
