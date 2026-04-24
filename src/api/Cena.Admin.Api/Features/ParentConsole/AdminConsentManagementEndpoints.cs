// =============================================================================
// Cena Platform — Admin parental-consent management endpoints (prr-096)
//
// Two admin-only routes, both tenant-scoped:
//
//   GET  /api/admin/institutes/{iid}/students/{sid}/consent-summary
//     Returns current consent posture by purpose + history count +
//     last-revocation-reason, folded from the consent stream.
//     AdminOnly (ADMIN or SUPER_ADMIN). ADMIN pinned to own institute.
//
//   POST /api/admin/institutes/{iid}/students/{sid}/consent-override
//     Emergency admin override: grant or revoke a purpose outside the
//     normal parent/student flow. Body:
//       { purpose: "Analytics", operation: "grant" | "revoke",
//         justification: "<10..500 chars>" }
//     Appends AdminConsentOverridden_V1 to consent-{sid} stream AND
//     emits a [SIEM] structured log for security monitoring.
//     AdminOnly, tenant-scoped, same constraints as the summary route.
//
// WHY these endpoints exist (prr-096):
//   Real ops scenarios require an admin to take action on a consent
//   posture outside parent/student flow — legal-hold retention, GDPR
//   access requests with verified identity, cohort policy migration.
//   The alternative is admins directly manipulating the DB, which
//   bypasses the audit trail. This endpoint forces the action through
//   the event stream + SIEM.
//
// Architecture notes:
//   - Re-uses ConsentAuditExportEndpoint.IsTenantAllowed for the
//     tenant-scope check; identical rules.
//   - Uses IConsentAggregateStore for read (LoadAsync) and write
//     (AppendAsync) — no direct Marten access.
//   - PII encryption is handled via EncryptedFieldAccessor on the
//     emitted event (subject + admin actor id are encrypted per
//     ADR-0038).
//   - No ADR-0003 misconception data is on these surfaces — consent
//     purposes are the only data type.
// =============================================================================

using System.Globalization;
using System.Security.Claims;
using Cena.Actors.Consent;
using Cena.Actors.Consent.Events;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Compliance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Features.ParentConsole;

// ---- Wire DTOs --------------------------------------------------------------

/// <summary>
/// Per-purpose posture row in the consent-summary response.
/// </summary>
/// <param name="Purpose">ConsentPurpose enum name.</param>
/// <param name="IsGranted">Current effective grant state (after expiry).</param>
/// <param name="GrantedByRole">Role that made the active grant, if any.</param>
/// <param name="GrantedAtUtc">ISO-8601 timestamp of active grant, empty if N/A.</param>
/// <param name="ExpiresAtUtc">Grant expiry (empty if indefinite or no grant).</param>
/// <param name="RevokedAtUtc">Most-recent revoke timestamp (empty if none).</param>
/// <param name="RevokedByRole">Role that revoked (empty if never revoked).</param>
/// <param name="RevocationReason">Structural reason of most-recent revoke.</param>
public sealed record ConsentPurposePostureDto(
    string Purpose,
    bool IsGranted,
    string GrantedByRole,
    string GrantedAtUtc,
    string ExpiresAtUtc,
    string RevokedAtUtc,
    string RevokedByRole,
    string RevocationReason);

/// <summary>
/// Envelope for the consent-summary response.
/// </summary>
/// <param name="StudentAnonId">Anonymised student identifier.</param>
/// <param name="InstituteId">Institute id scope.</param>
/// <param name="Posture">Per-purpose current state (all known purposes).</param>
/// <param name="HistoryEventCount">Total events in the stream (for audit hint).</param>
/// <param name="VetoedPurposes">Purposes currently vetoed by student (prr-052).</param>
public sealed record ConsentSummaryDto(
    string StudentAnonId,
    string InstituteId,
    IReadOnlyList<ConsentPurposePostureDto> Posture,
    int HistoryEventCount,
    IReadOnlyList<string> VetoedPurposes);

/// <summary>
/// Admin override request body.
/// </summary>
/// <param name="Purpose">ConsentPurpose enum name.</param>
/// <param name="Operation"><c>"grant"</c> or <c>"revoke"</c>.</param>
/// <param name="Justification">Structural justification, 10..500 chars.</param>
public sealed record AdminConsentOverrideRequest(
    string Purpose,
    string Operation,
    string Justification);

public sealed record AdminConsentOverrideResponseDto(
    string StudentAnonId,
    string InstituteId,
    string Purpose,
    string Operation,
    string AdminActorAnonId,
    string OverrideAtUtc,
    string Justification);

// ---- Endpoint ---------------------------------------------------------------

public static class AdminConsentManagementEndpoints
{
    /// <summary>GET route for consent summary.</summary>
    public const string SummaryRoute =
        "/api/admin/institutes/{instituteId}/students/{studentAnonId}/consent-summary";

    /// <summary>POST route for admin override.</summary>
    public const string OverrideRoute =
        "/api/admin/institutes/{instituteId}/students/{studentAnonId}/consent-override";

    /// <summary>Valid operation tokens on the override POST.</summary>
    public static readonly IReadOnlyList<string> ValidOperations = new[] { "grant", "revoke" };

    private const int JustificationMinChars = 10;
    private const int JustificationMaxChars = 500;

    /// <summary>Marker for ILogger type-argument stability.</summary>
    internal sealed class AdminConsentManagementMarker { }

    public static IEndpointRouteBuilder MapAdminConsentManagementEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(SummaryRoute, HandleSummaryAsync)
            .WithName("GetAdminConsentSummary")
            .WithTags("Admin", "Consent", "Parental Consent")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly);

        app.MapPost(OverrideRoute, HandleOverrideAsync)
            .WithName("AdminConsentOverride")
            .WithTags("Admin", "Consent", "Parental Consent")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly);

        return app;
    }

    // ── GET: summary ────────────────────────────────────────────────────

    internal static async Task<IResult> HandleSummaryAsync(
        string instituteId,
        string studentAnonId,
        HttpContext http,
        IConsentAggregateStore consentStore,
        ILogger<AdminConsentManagementMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instituteId))
            return Results.BadRequest(new { error = "missing-instituteId" });
        if (string.IsNullOrWhiteSpace(studentAnonId))
            return Results.BadRequest(new { error = "missing-studentAnonId" });

        if (!ConsentAuditExportEndpoint.IsTenantAllowed(http.User, instituteId))
        {
            logger.LogWarning(
                "[prr-096] consent-summary refused cross-tenant: requested={Requested}",
                instituteId);
            return Results.Forbid();
        }

        var aggregate = await consentStore.LoadAsync(studentAnonId, ct).ConfigureAwait(false);
        var events = await consentStore.ReadEventsAsync(studentAnonId, ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var posture = new List<ConsentPurposePostureDto>();
        foreach (var purpose in AllKnownPurposes())
        {
            aggregate.State.Grants.TryGetValue(purpose, out var info);
            var granted = aggregate.State.IsEffectivelyGranted(purpose, now);
            posture.Add(new ConsentPurposePostureDto(
                Purpose: purpose.ToString(),
                IsGranted: granted,
                GrantedByRole: info?.GrantedByRole?.ToString() ?? string.Empty,
                GrantedAtUtc: info?.GrantedAt is { } ga ? FormatIso(ga) : string.Empty,
                ExpiresAtUtc: info?.ExpiresAt is { } ea ? FormatIso(ea) : string.Empty,
                RevokedAtUtc: info?.RevokedAt is { } ra ? FormatIso(ra) : string.Empty,
                RevokedByRole: info?.RevokedByRole?.ToString() ?? string.Empty,
                RevocationReason: info?.RevocationReason ?? string.Empty));
        }

        var vetoed = aggregate.State.VetoedParentVisibilityPurposes
            .Select(p => p.ToString())
            .ToList();

        logger.LogInformation(
            "[prr-096] consent-summary rendered: student={StudentAnonId} "
            + "institute={InstituteId} purposes={PurposeCount} history={EventCount} vetoed={VetoedCount}",
            studentAnonId, instituteId, posture.Count, events.Count, vetoed.Count);

        return Results.Ok(new ConsentSummaryDto(
            StudentAnonId: studentAnonId,
            InstituteId: instituteId,
            Posture: posture,
            HistoryEventCount: events.Count,
            VetoedPurposes: vetoed));
    }

    // ── POST: override ───────────────────────────────────────────────────

    internal static async Task<IResult> HandleOverrideAsync(
        string instituteId,
        string studentAnonId,
        AdminConsentOverrideRequest request,
        HttpContext http,
        IConsentAggregateStore consentStore,
        EncryptedFieldAccessor piiAccessor,
        ILogger<AdminConsentManagementMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instituteId))
            return Results.BadRequest(new { error = "missing-instituteId" });
        if (string.IsNullOrWhiteSpace(studentAnonId))
            return Results.BadRequest(new { error = "missing-studentAnonId" });
        if (request is null)
            return Results.BadRequest(new { error = "missing-body" });

        if (!ConsentAuditExportEndpoint.IsTenantAllowed(http.User, instituteId))
        {
            logger.LogWarning(
                "[prr-096] consent-override refused cross-tenant: requested={Requested}",
                instituteId);
            return Results.Forbid();
        }

        // Parse purpose. Unknown → 400 (strict; typos must not silently
        // write a no-op event with an unrecognised purpose name). Combine
        // TryParse (which accepts any integer) with IsDefined (which
        // rejects bogus numeric values) so only named members survive.
        if (!Enum.TryParse<ConsentPurpose>(request.Purpose, ignoreCase: true, out var purpose)
            || !Enum.IsDefined(typeof(ConsentPurpose), purpose))
        {
            return Results.BadRequest(new
            {
                error = "unknown-purpose",
                purpose = request.Purpose,
            });
        }

        // Operation
        var op = (request.Operation ?? string.Empty).Trim().ToLowerInvariant();
        if (!ValidOperations.Contains(op, StringComparer.Ordinal))
        {
            return Results.BadRequest(new
            {
                error = "unknown-operation",
                operation = request.Operation,
                validOperations = ValidOperations,
            });
        }

        // Justification length
        var justification = (request.Justification ?? string.Empty).Trim();
        if (justification.Length < JustificationMinChars)
        {
            return Results.BadRequest(new
            {
                error = "justification-too-short",
                minChars = JustificationMinChars,
            });
        }
        if (justification.Length > JustificationMaxChars)
        {
            return Results.BadRequest(new
            {
                error = "justification-too-long",
                maxChars = JustificationMaxChars,
            });
        }

        // Resolve admin actor id — prefer parentAnonId-style claim, fall
        // back to NameIdentifier / sub. Empty actor id is a hard deny: we
        // cannot emit an override event with no accountable party.
        var adminActorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue("sub")
            ?? http.User.FindFirstValue("adminActorId");
        if (string.IsNullOrWhiteSpace(adminActorId))
        {
            logger.LogWarning(
                "[prr-096] consent-override refused: admin actor id missing from claims "
                + "student={StudentAnonId} institute={InstituteId}",
                studentAnonId, instituteId);
            return Results.Forbid();
        }

        var now = DateTimeOffset.UtcNow;

        // Encrypt PII fields before emitting the event (ADR-0038).
        var subjectEncrypted = await piiAccessor
            .EncryptAsync(studentAnonId, studentAnonId, ct)
            .ConfigureAwait(false);
        var adminEncrypted = await piiAccessor
            .EncryptAsync(adminActorId, studentAnonId, ct)
            .ConfigureAwait(false);

        var overrideEvent = new AdminConsentOverridden_V1(
            SubjectIdEncrypted: subjectEncrypted ?? string.Empty,
            Purpose: purpose,
            Operation: op,
            AdminActorIdEncrypted: adminEncrypted ?? string.Empty,
            InstituteId: instituteId,
            OverrideAt: now,
            Justification: justification);

        await consentStore.AppendAsync(studentAnonId, overrideEvent, ct).ConfigureAwait(false);

        // SIEM notification — required by prr-096 DoD. Every admin override
        // is a high-impact action; the security-engineering SOC reads
        // [SIEM]-tagged logs and pages on-call on unusual spike patterns.
        logger.LogWarning(
            "[SIEM] [prr-096] AdminConsentOverridden: student={StudentAnonId} "
            + "institute={InstituteId} purpose={Purpose} operation={Operation} "
            + "admin={AdminActorId} justification={Justification}",
            studentAnonId, instituteId, purpose, op, adminActorId, justification);

        return Results.Ok(new AdminConsentOverrideResponseDto(
            StudentAnonId: studentAnonId,
            InstituteId: instituteId,
            Purpose: purpose.ToString(),
            Operation: op,
            AdminActorAnonId: adminActorId,
            OverrideAtUtc: FormatIso(now),
            Justification: justification));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static IEnumerable<ConsentPurpose> AllKnownPurposes()
    {
        foreach (var value in Enum.GetValues(typeof(ConsentPurpose)))
        {
            var p = (ConsentPurpose)value!;
            // Skip the Unknown sentinel if present (defensive — keeps the
            // summary shape stable even if a future Unknown member lands).
            if (p.ToString() == "Unknown") continue;
            yield return p;
        }
    }

    private static string FormatIso(DateTimeOffset ts)
        => ts.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
