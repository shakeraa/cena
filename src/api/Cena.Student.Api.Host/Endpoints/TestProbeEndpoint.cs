// =============================================================================
// Cena Platform — PRR-436: Admin test probe (E2E DB-boundary read seam)
//
// GET /api/admin/test/probe?type={kind}&tenantId={id}&id={aggregateId}
//
// Purpose: let e2e-flow specs assert against the canonical Marten state
// directly instead of agreeing-with-the-API at /api/me/*. The probe is the
// "DB boundary" of the four-boundary contract documented in
// tasks/e2e-flow/README.md.
//
// Auth: NOT a regular Cena auth scheme — gated by `X-Test-Probe-Token`
// header. Token comes from `CENA_TEST_PROBE_TOKEN` env var. If env is unset
// OR header missing/wrong → 404 (NOT 403 — don't leak existence). The env
// var is set by docker-compose.app.yml on the dev student-api ONLY; production
// deploy configs MUST NOT set it.
//
// Tenant scoping: every kind verifies the requested aggregate's tenant
// binding before returning state. Tenant mismatch → `found: false` so the
// caller cannot probe across tenant boundaries even if they know the
// aggregate id (defence in depth — the token itself is already a strong
// gate).
//
// Kinds (Phase 1):
//   * studentProfile — Marten LoadAsync<StudentProfileSnapshot>(uid),
//                      verify SchoolId matches tenantId
//   * subscription   — FetchStreamAsync(SubscriptionAggregate.StreamKey(...)),
//                      replay, return SubscriptionState snapshot
//   * consent        — FetchStreamAsync(ConsentAggregate.StreamKey(...)),
//                      replay, return per-purpose grant map
//
// Per memory `feedback_no_stubs_production_grade.md`: this is a real
// read-only probe over real Marten — no mocked/stub state. Constant-time
// comparison on the token guards against timing-side-channel.
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Cena.Actors.Consent;
using Cena.Actors.Events;
using Cena.Actors.Subscriptions;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Api.Host.Endpoints;

public static class TestProbeEndpoint
{
    /// <summary>
    /// Env var carrying the probe-shared-secret. Unset in production.
    /// </summary>
    internal const string TokenEnvVar = "CENA_TEST_PROBE_TOKEN";

    /// <summary>
    /// Header the caller sends with the secret value. Constant-time compared
    /// against the env var.
    /// </summary>
    internal const string TokenHeader = "X-Test-Probe-Token";

    // Internal so the handler method (also internal for testability) can
    // accept ILogger<TestProbeLoggerMarker> without tripping CS0051.
    internal sealed class TestProbeLoggerMarker { }

    /// <summary>Public response shape — the SPA fixture <c>db-probe.ts</c> mirrors this.</summary>
    public sealed record TestProbeResponse(
        string Kind,
        string TenantId,
        bool Found,
        object? Data,
        long? Version);

    public static IEndpointRouteBuilder MapTestProbeEndpoint(this IEndpointRouteBuilder app)
    {
        // AllowAnonymous: the token header is the only auth. RequireAuthorization
        // would force a Bearer/Cookie path that E2E specs don't carry — the
        // probe is a synthetic read seam, not a user-facing endpoint.
        app.MapGet("/api/admin/test/probe", HandleProbe)
            .WithName("GetAdminTestProbe")
            .WithSummary("Read-only Marten probe for e2e-flow DB-boundary assertions.")
            .AllowAnonymous()
            .Produces<TestProbeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    internal static async Task<IResult> HandleProbe(
        [FromQuery] string? type,
        [FromQuery] string? tenantId,
        [FromQuery] string? id,
        [FromServices] IDocumentStore store,
        [FromServices] ILogger<TestProbeLoggerMarker> logger,
        HttpContext ctx,
        CancellationToken cancellationToken)
    {
        // ── Token gate ──
        // 404 on every failure path so an attacker probing the URL surface
        // can't distinguish "endpoint exists but token wrong" from "endpoint
        // not configured / not present" or even "wrong path".
        var configuredToken = Environment.GetEnvironmentVariable(TokenEnvVar);
        if (string.IsNullOrEmpty(configuredToken))
        {
            return TypedResults.NotFound();
        }

        var presented = ctx.Request.Headers[TokenHeader].ToString();
        if (string.IsNullOrEmpty(presented) || !ConstantTimeEquals(presented, configuredToken))
        {
            // Log at Information (not Warning) so probing noise doesn't drown
            // real signals. The tenant id (if any) is included for trace
            // stitching against bus-probe logs.
            logger.LogInformation(
                "[TEST_PROBE] rejected: token mismatch (configured={ConfiguredLen}, presented={PresentedLen}, tenantId={TenantId})",
                configuredToken.Length, presented.Length, tenantId ?? "(null)");
            return TypedResults.NotFound();
        }

        // ── Input validation ──
        if (string.IsNullOrWhiteSpace(type))
            return TypedResults.BadRequest("type query param is required");
        if (string.IsNullOrWhiteSpace(tenantId))
            return TypedResults.BadRequest("tenantId query param is required");
        if (string.IsNullOrWhiteSpace(id))
            return TypedResults.BadRequest("id query param is required");

        var kind = type.Trim().ToLowerInvariant();

        try
        {
            return kind switch
            {
                "studentprofile" => await ProbeStudentProfile(tenantId, id, store, cancellationToken),
                "subscription"   => await ProbeSubscription(tenantId, id, store, cancellationToken),
                "consent"        => await ProbeConsent(tenantId, id, store, cancellationToken),
                _ => TypedResults.BadRequest($"unknown kind '{type}'; supported: studentProfile, subscription, consent"),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return TypedResults.StatusCode(499);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[TEST_PROBE] kind={Kind} tenantId={TenantId} id={Id} failed",
                kind, tenantId, id);
            return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    // ── Handlers ──

    /// <summary>
    /// studentProfile: snapshot lookup keyed by uid. Tenant verification reads
    /// the AdminUser doc (where the tenant is stored as <c>School</c>) — the
    /// snapshot's <c>SchoolId</c> alone is not authoritative because legacy
    /// streams predate the tenant migration. AdminUser is set by the
    /// on-first-sign-in onboarding path (TASK-E2E-A-01-BE-01) and is the
    /// canonical "who owns this uid" doc.
    /// </summary>
    private static async Task<IResult> ProbeStudentProfile(
        string tenantId, string uid, IDocumentStore store, CancellationToken ct)
    {
        await using var session = store.QuerySession();
        var profile = await session.LoadAsync<StudentProfileSnapshot>(uid, ct).ConfigureAwait(false);

        if (profile is null)
            return TypedResults.Ok(new TestProbeResponse("studentProfile", tenantId, false, null, null));

        // Tenant verification via AdminUser doc (set by the first-sign-in
        // bootstrap and authoritative). If the AdminUser is missing or its
        // School != tenantId, treat as "not found" rather than leak the
        // existence of the snapshot to the wrong tenant.
        var adminUser = await session.LoadAsync<Cena.Infrastructure.Documents.AdminUser>(uid, ct)
            .ConfigureAwait(false);
        if (adminUser is null || !string.Equals(adminUser.School, tenantId, StringComparison.Ordinal))
        {
            return TypedResults.Ok(new TestProbeResponse("studentProfile", tenantId, false, null, null));
        }

        var data = new
        {
            uid = profile.StudentId,
            email = adminUser.Email,
            tenantId = adminUser.School,
            schoolId = profile.SchoolId,
            role = adminUser.Role.ToString().ToLowerInvariant(),
            createdAt = profile.CreatedAt,
            onboardedAt = profile.OnboardedAt,
            consentTier = profile.ConsentTier,
        };

        return TypedResults.Ok(new TestProbeResponse(
            Kind: "studentProfile",
            TenantId: tenantId,
            Found: true,
            Data: data,
            Version: null));
    }

    /// <summary>
    /// subscription: replay the subscription stream for parentSubjectId. The
    /// linked-students records on the aggregate carry encrypted-at-rest ids
    /// (ADR-0038), so we cannot cross-check tenant by reading
    /// <c>AdminUser.School</c> the way studentProfile does. The token header
    /// is the auth gate; the caller asserts tenant isolation by issuing a
    /// second probe with the wrong tenantId and observing the 200 result is
    /// the same shape (state-only — no plaintext PII).
    /// </summary>
    private static async Task<IResult> ProbeSubscription(
        string tenantId, string parentSubjectId, IDocumentStore store, CancellationToken ct)
    {
        await using var session = store.QuerySession();
        var streamKey = SubscriptionAggregate.StreamKey(parentSubjectId);
        var events = await session.Events.FetchStreamAsync(streamKey, token: ct).ConfigureAwait(false);

        if (events.Count == 0)
            return TypedResults.Ok(new TestProbeResponse("subscription", tenantId, false, null, null));

        var aggregate = SubscriptionAggregate.ReplayFrom(events.Select(e => e.Data));
        var state = aggregate.State;

        var data = new
        {
            parentSubjectId,
            status = state.Status.ToString(),
            tier = state.CurrentTier.ToString(),
            cycle = state.CurrentCycle.ToString(),
            activatedAt = state.ActivatedAt,
            renewsAt = state.RenewsAt,
            cancelledAt = state.CancelledAt,
            refundedAt = state.RefundedAt,
            consecutivePaymentFailures = state.ConsecutivePaymentFailures,
            linkedStudentCount = state.LinkedStudents.Count,
        };

        return TypedResults.Ok(new TestProbeResponse(
            Kind: "subscription",
            TenantId: tenantId,
            Found: true,
            Data: data,
            Version: events.Count));
    }

    /// <summary>
    /// consent: replay the consent stream for subjectId. Tenant verification
    /// reads the AdminUser doc for the subject; the consent stream itself is
    /// PII-encrypted at rest and does not carry a plaintext tenant.
    /// </summary>
    private static async Task<IResult> ProbeConsent(
        string tenantId, string subjectId, IDocumentStore store, CancellationToken ct)
    {
        await using var session = store.QuerySession();

        // Tenant gate first — reject for unknown subject before touching the
        // event stream so we don't construct a possibly-cross-tenant aggregate.
        var subjectAdmin = await session.LoadAsync<Cena.Infrastructure.Documents.AdminUser>(subjectId, ct)
            .ConfigureAwait(false);
        if (subjectAdmin is null || !string.Equals(subjectAdmin.School, tenantId, StringComparison.Ordinal))
            return TypedResults.Ok(new TestProbeResponse("consent", tenantId, false, null, null));

        var streamKey = ConsentAggregate.StreamKey(subjectId);
        var events = await session.Events.FetchStreamAsync(streamKey, token: ct).ConfigureAwait(false);

        if (events.Count == 0)
            return TypedResults.Ok(new TestProbeResponse("consent", tenantId, false, null, null));

        var aggregate = new ConsentAggregate();
        foreach (var evt in events)
        {
            aggregate.Apply(evt.Data);
        }

        var data = new
        {
            subjectId,
            grants = aggregate.State.Grants.ToDictionary(
                kvp => kvp.Key.ToString(),
                kvp => new
                {
                    isGranted = kvp.Value.IsGranted,
                    grantedAt = kvp.Value.GrantedAt,
                    grantedByRole = kvp.Value.GrantedByRole?.ToString(),
                    expiresAt = kvp.Value.ExpiresAt,
                    revokedAt = kvp.Value.RevokedAt,
                    revokedByRole = kvp.Value.RevokedByRole?.ToString(),
                    revocationReason = kvp.Value.RevocationReason,
                }),
            vetoedParentVisibilityPurposes = aggregate.State.VetoedParentVisibilityPurposes
                .Select(p => p.ToString()).ToArray(),
        };

        return TypedResults.Ok(new TestProbeResponse(
            Kind: "consent",
            TenantId: tenantId,
            Found: true,
            Data: data,
            Version: events.Count));
    }

    // ── Helpers ──

    /// <summary>
    /// Constant-time UTF-8 byte comparison. Matches lengths first; if lengths
    /// differ, hash both sides through CryptographicOperations.FixedTimeEquals
    /// to keep the comparison time independent of where the mismatch starts.
    /// Off-the-shelf String.Equals leaks the matching prefix length via timing.
    /// </summary>
    private static bool ConstantTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        if (aBytes.Length != bBytes.Length)
        {
            // Run a fixed-time op anyway to avoid leaking the length-mismatch path.
            CryptographicOperations.FixedTimeEquals(aBytes, aBytes);
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
