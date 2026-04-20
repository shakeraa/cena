// =============================================================================
// Cena Platform — Parent Digest: Unsubscribe Endpoint (prr-051).
//
// GET /unsubscribe/{token}
//
// Anonymous (AllowAnonymous) by design: the link lands in a parent's email
// or SMS, and they may click it while signed out. All authorization is
// carried by the token:
//
//   - HMAC signature — verified server-side.
//   - Nonce — single-use; a second click surfaces AlreadyUsed and returns
//     an idempotent 200 (don't surprise the parent with a 400 if they
//     double-tap).
//   - Expiry — short (14 days); expired tokens → 410.
//   - Tenant — the token's embedded institute_id must match the institute
//     currently resolved by the request (via the Host header / route
//     prefix / X-Cena-Institute header fallback). Phase 1 uses an X-header
//     that the edge proxy injects; phase 2 moves to subdomain-derived
//     tenancy.
//
// Emits:
//   - ParentDigestUnsubscribed_V1 to the student stream on first valid use.
//   - cena_parent_digest_unsubscribes_total{result=...} metric.
//
// The endpoint is deliberately NOT wired to the platform auth stack —
// there is no PARENT role check here because the parent may not hold a
// session at click time. The token itself is the bearer credential.
// =============================================================================

using Cena.Actors.ParentDigest;
using Cena.Actors.ParentDigest.Events;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;

namespace Cena.Admin.Api.Features.ParentConsole;

// ---- Wire DTOs --------------------------------------------------------------

public sealed record UnsubscribeResultDto(
    string Status,
    string? Message);

// ---- Endpoint ---------------------------------------------------------------

public static class UnsubscribeEndpoint
{
    /// <summary>
    /// Path segment the link in the digest uses. Keep short for SMS-length
    /// budget; the token segment dominates length anyway.
    /// </summary>
    public const string Route = "/unsubscribe/{token}";

    /// <summary>
    /// Configuration key for the tenant-resolution header the edge proxy
    /// injects. If not present, the endpoint falls back to the
    /// X-Cena-Institute request header directly.
    /// </summary>
    public const string TenantHeaderName = "X-Cena-Institute";

    private static readonly Meter UnsubscribeMeter =
        new("Cena.Admin.Api.ParentDigest.Unsubscribe", "1.0.0");

    private static readonly Counter<long> UnsubscribeCounter =
        UnsubscribeMeter.CreateCounter<long>(
            "cena_parent_digest_unsubscribes_total",
            description:
                "Parent digest unsubscribe-link invocations, labelled by result (prr-051).");

    public static IEndpointRouteBuilder MapUnsubscribeEndpoint(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleUnsubscribeAsync)
            .WithName("ParentDigestUnsubscribe")
            .WithTags("Parent Console", "Digest", "Unsubscribe")
            .AllowAnonymous();
        return app;
    }

    // Public static so the architecture ratchet test can regex-scan it as a
    // digest-dispatch *consumer* of preferences and confirm the guard shape.
    public static async Task<IResult> HandleUnsubscribeAsync(
        string token,
        HttpContext http,
        IUnsubscribeTokenService tokenService,
        IParentDigestPreferencesStore preferencesStore,
        IDocumentStore documentStore,
        ILogger<UnsubscribeEndpointMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            RecordResult("malformed");
            return Results.BadRequest(new UnsubscribeResultDto(
                Status: "malformed",
                Message: "Missing unsubscribe token."));
        }

        // Resolve the current tenant. Edge proxy injects X-Cena-Institute;
        // without it we fail closed rather than defaulting to a wildcard.
        var institute = ResolveInstituteIdFromRequest(http);
        if (string.IsNullOrWhiteSpace(institute))
        {
            RecordResult("no_tenant");
            logger.LogWarning(
                "[prr-051] Unsubscribe click rejected: no tenant on request.");
            return Results.BadRequest(new UnsubscribeResultDto(
                Status: "no_tenant",
                Message: "Cannot resolve tenant for the current request."));
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var verification = await tokenService.VerifyAndConsumeAsync(token, institute, nowUtc, ct)
            .ConfigureAwait(false);

        switch (verification.Outcome)
        {
            case UnsubscribeTokenOutcome.Valid:
            {
                var payload = verification.Payload!;
                await preferencesStore.ApplyUnsubscribeAllAsync(
                    payload.ParentActorId,
                    payload.StudentSubjectId,
                    payload.InstituteId,
                    nowUtc,
                    ct).ConfigureAwait(false);

                var ev = new ParentDigestUnsubscribed_V1(
                    ParentActorId: payload.ParentActorId,
                    StudentSubjectId: payload.StudentSubjectId,
                    InstituteId: payload.InstituteId,
                    UnsubscribedAtUtc: nowUtc,
                    TokenFingerprint: verification.Fingerprint);

                await using var session = documentStore.LightweightSession();
                session.Events.Append(payload.StudentSubjectId, ev);
                await session.SaveChangesAsync(ct);

                logger.LogInformation(
                    "[prr-051] Parent digest unsubscribed for student={StudentAnonId} "
                    + "parent={ParentAnonId} institute={InstituteId} fingerprint={Fingerprint}",
                    payload.StudentSubjectId,
                    payload.ParentActorId,
                    payload.InstituteId,
                    verification.Fingerprint);

                RecordResult("valid");
                return Results.Ok(new UnsubscribeResultDto(
                    Status: "unsubscribed",
                    Message: "You have been unsubscribed from all parent digests for this child."));
            }

            case UnsubscribeTokenOutcome.AlreadyUsed:
            {
                // Idempotent success. The parent clicked twice; the
                // preferences row is already opt-out. We emit a separate
                // metric label so we can track "already_used" volume
                // without it appearing as a failure.
                RecordResult("already_used");
                return Results.Ok(new UnsubscribeResultDto(
                    Status: "already_unsubscribed",
                    Message: "This link has already been used. You remain unsubscribed."));
            }

            case UnsubscribeTokenOutcome.Expired:
            {
                RecordResult("expired");
                logger.LogInformation(
                    "[prr-051] Unsubscribe link expired fingerprint={Fingerprint}",
                    verification.Fingerprint);
                return Results.StatusCode(StatusCodes.Status410Gone);
            }

            case UnsubscribeTokenOutcome.CrossTenant:
            {
                // Explicit tenant mismatch — log as a warning for ops
                // (could be a replayed token from a different tenant).
                RecordResult("cross_tenant");
                logger.LogWarning(
                    "[prr-051] Cross-tenant unsubscribe rejected fingerprint={Fingerprint} "
                    + "expectedInstitute={Expected}",
                    verification.Fingerprint, institute);
                return Results.Forbid();
            }

            case UnsubscribeTokenOutcome.Tampered:
            {
                RecordResult("tampered");
                logger.LogWarning(
                    "[prr-051] Tampered unsubscribe token rejected fingerprint={Fingerprint}",
                    verification.Fingerprint);
                return Results.Forbid();
            }

            case UnsubscribeTokenOutcome.Malformed:
            default:
            {
                RecordResult("malformed");
                return Results.BadRequest(new UnsubscribeResultDto(
                    Status: "malformed",
                    Message: "Invalid unsubscribe token."));
            }
        }
    }

    internal static string ResolveInstituteIdFromRequest(HttpContext http)
    {
        if (http.Request.Headers.TryGetValue(TenantHeaderName, out var headerValues))
        {
            foreach (var value in headerValues)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
        }

        // Fallback for fixtures + tests: a configured default-institute
        // value (NEVER used in production — registration asserts the
        // header is present in prod via the startup check).
        var configured = http.RequestServices
            .GetService<IConfiguration>()?["Cena:ParentDigest:FallbackInstituteId"];
        return string.IsNullOrWhiteSpace(configured) ? string.Empty : configured.Trim();
    }

    private static void RecordResult(string result)
    {
        UnsubscribeCounter.Add(
            1, new KeyValuePair<string, object?>("result", result));
    }

    public sealed class UnsubscribeEndpointMarker { }
}
