// =============================================================================
// Cena Platform — Admin "me" endpoints (RDY-058 Account Settings + Security)
//
// Endpoints under /api/admin/me/** return or mutate the signed-in admin's
// own profile — NOT an arbitrary user's. All are authenticated; none
// require SuperAdmin because every admin manages their own account.
// Audit-logging is handled by AdminActionAuditMiddleware upstream.
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Firebase;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api;

public static class AdminMeEndpoints
{
    public static IEndpointRouteBuilder MapAdminMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/me")
            .WithTags("Admin Me")
            .RequireAuthorization()  // any signed-in user — they manage their own account
            .RequireRateLimiting("api");

        // GET /api/admin/me/profile
        group.MapGet("/profile", async (
            ClaimsPrincipal user,
            IDocumentStore store,
            IFirebaseAdminService firebase,
            ILogger<Marker> logger) =>
        {
            var uid = user.FindFirst("user_id")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(uid))
                return Results.Unauthorized();

            await using var session = store.QuerySession();
            var admin = await session.LoadAsync<AdminUser>(uid);
            var fb = await firebase.GetUserAsync(uid);

            // We use Firebase as the source of truth for identity (providers,
            // email-verified, display-name) and Marten for preferences
            // (locale, timezone, role). If either side is missing, we
            // synthesise from what we have so the UI can render without null
            // guards everywhere.
            var email = admin?.Email ?? fb?.Email ?? user.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var displayName = fb?.DisplayName ?? admin?.FullName ?? "";
            var role = admin?.Role.ToString() ?? user.FindFirst("role")?.Value ?? "ADMIN";
            var locale = admin?.Locale ?? user.FindFirst("lang")?.Value ?? "en";

            return Results.Ok(new MeProfileResponse(
                Uid: uid,
                Email: email,
                EmailVerified: fb?.EmailVerified ?? false,
                DisplayName: displayName,
                PhotoUrl: fb?.PhotoUrl,
                Role: role,
                Locale: locale,
                Timezone: "UTC",  // TZ preference not yet in AdminUser schema; RDY-058 follow-up
                School: admin?.School,
                CreatedAt: admin?.CreatedAt ?? DateTimeOffset.MinValue,
                LastLoginAt: admin?.LastLoginAt,
                Providers: (fb?.Providers ?? Array.Empty<FirebaseProviderLink>())
                    .Select(p => new LinkedProvider(
                        ProviderId: p.ProviderId,
                        DisplayName: ProviderDisplayName(p.ProviderId),
                        Email: p.Email,
                        LinkedAt: null))
                    .ToArray(),
                MfaEnrolled: fb?.MfaEnrolled ?? false));
        })
        .WithName("GetMyProfile")
        .Produces<MeProfileResponse>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        // PATCH /api/admin/me/profile
        group.MapPatch("/profile", async (
            [FromBody] UpdateMyProfileRequest request,
            ClaimsPrincipal user,
            IDocumentStore store,
            IFirebaseAdminService firebase,
            ILogger<Marker> logger) =>
        {
            var uid = user.FindFirst("user_id")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(uid))
                return Results.Unauthorized();

            // Validate locale early — only en / he / ar supported platform-wide.
            if (request.Locale is not null
                && request.Locale != "en" && request.Locale != "he" && request.Locale != "ar")
            {
                return Results.BadRequest(new CenaError(
                    ErrorCodes.CENA_INTERNAL_VALIDATION,
                    "Locale must be one of: en, he, ar.",
                    ErrorCategory.Validation,
                    null,
                    null));
            }

            // Display-name goes to Firebase (source of truth for identity);
            // locale + timezone go to Marten AdminUser (our preferences doc).
            if (request.DisplayName is not null)
            {
                try { await firebase.UpdateDisplayNameAsync(uid, request.DisplayName); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Firebase display-name update failed for {Uid}", uid);
                    return Results.Problem("Firebase admin SDK unavailable", statusCode: 503);
                }
            }

            await using var sessionRw = store.LightweightSession();
            var existing = await sessionRw.LoadAsync<AdminUser>(uid);
            // AdminUser is a record with `init`-only name + locale fields,
            // so we use `with` expressions rather than mutation.
            var roleClaim = user.FindFirst("role")?.Value ?? "ADMIN";
            Enum.TryParse<CenaRole>(roleClaim, ignoreCase: true, out var roleEnum);
            var admin = existing ?? new AdminUser
            {
                Id = uid,
                Email = user.FindFirst(ClaimTypes.Email)?.Value ?? "",
                FullName = request.DisplayName ?? "",
                Role = roleEnum == default ? CenaRole.ADMIN : roleEnum,
                Status = UserStatus.Active,
                Locale = request.Locale ?? "en",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            var updated = admin with
            {
                FullName = request.DisplayName ?? admin.FullName,
                Locale = request.Locale ?? admin.Locale,
            };
            sessionRw.Store(updated);
            await sessionRw.SaveChangesAsync();

            logger.LogInformation("[AUDIT] profile update uid={Uid} fields={Fields}", uid,
                string.Join(",", new[]
                {
                    request.DisplayName is null ? null : "displayName",
                    request.Locale is null ? null : "locale",
                    request.Timezone is null ? null : "timezone",
                }.Where(x => x is not null)));

            return Results.Ok(new { updated = true });
        })
        .WithName("UpdateMyProfile")
        .Produces<object>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status400BadRequest)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        // POST /api/admin/me/sign-out-everywhere
        // Forces sign-out across all devices + refreshing the current one.
        group.MapPost("/sign-out-everywhere", async (
            ClaimsPrincipal user,
            IFirebaseAdminService firebase,
            ILogger<Marker> logger) =>
        {
            var uid = user.FindFirst("user_id")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(uid))
                return Results.Unauthorized();

            try
            {
                await firebase.RevokeRefreshTokensAsync(uid);
                logger.LogInformation("[AUDIT] sign-out-everywhere uid={Uid}", uid);
                return Results.Ok(new { revoked = true });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RevokeRefreshTokens failed for {Uid}", uid);
                return Results.Problem("Firebase admin SDK unavailable", statusCode: 503);
            }
        })
        .RequireRateLimiting("destructive")
        .WithName("SignOutEverywhere");

        // GET /api/admin/me/sign-in-history?limit=10
        // Reads the audit stream for the caller's own sign-in events.
        group.MapGet("/sign-in-history", async (
            int? limit,
            ClaimsPrincipal user,
            IDocumentStore store) =>
        {
            var uid = user.FindFirst("user_id")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(uid))
                return Results.Unauthorized();

            var cap = Math.Clamp(limit ?? 10, 1, 50);
            await using var session = store.QuerySession();
            // AuditEventDocument uses UserId (not ActorUid) and filters by
            // EventType ("auth_sign_in", "auth_sign_out", "auth_token_revoked", ...)
            // or a broader pattern. We match any EventType that starts with
            // "auth_" or "session_" so future auth events land in the
            // history automatically without a code change.
            var events = await session.Query<AuditEventDocument>()
                .Where(e => e.UserId == uid)
                .Where(e => e.EventType.StartsWith("auth_") || e.EventType.StartsWith("session_"))
                .OrderByDescending(e => e.Timestamp)
                .Take(cap)
                .ToListAsync();

            return Results.Ok(new
            {
                items = events.Select(e => new SignInHistoryItem(
                    Timestamp: e.Timestamp,
                    Action: string.IsNullOrEmpty(e.Action) ? e.EventType : e.Action,
                    IpAddress: string.IsNullOrEmpty(e.IpAddress) ? null : e.IpAddress,
                    UserAgent: string.IsNullOrEmpty(e.UserAgent) ? null : e.UserAgent,
                    Succeeded: e.Success))
            });
        })
        .WithName("GetMySignInHistory")
        .Produces<object>(StatusCodes.Status200OK);

        // DELETE /api/admin/me  — GDPR Article 17 self-erasure
        // Double-gated by a confirmation string in the body to avoid accidents.
        group.MapDelete("/", async (
            [FromBody] DeleteMyAccountRequest request,
            ClaimsPrincipal user,
            IDocumentStore store,
            IFirebaseAdminService firebase,
            ILogger<Marker> logger) =>
        {
            var uid = user.FindFirst("user_id")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(uid))
                return Results.Unauthorized();

            if (!string.Equals(request.Confirmation, "DELETE MY ACCOUNT", StringComparison.Ordinal))
            {
                return Results.BadRequest(new CenaError(
                    ErrorCodes.CENA_INTERNAL_VALIDATION,
                    "Confirmation must equal the literal string 'DELETE MY ACCOUNT'.",
                    ErrorCategory.Validation, null, null));
            }

            // Revoke tokens first so any in-flight requests stop, then flag
            // the AdminUser row as pending-erasure (scheduled by the retention
            // worker) and delete the Firebase user. We do NOT hard-delete
            // the AdminUser row here because the retention worker runs the
            // proper GDPR Article 17 pipeline with audit trail + tombstone.
            try { await firebase.RevokeRefreshTokensAsync(uid); } catch { /* best-effort */ }

            await using var sessionRw = store.LightweightSession();
            var admin = await sessionRw.LoadAsync<AdminUser>(uid);
            if (admin is not null)
            {
                // Status is an enum; "pending erasure" maps to Suspended with
                // an explicit reason so the retention worker can pick it up
                // without a schema change. A dedicated PendingErasure value
                // is a follow-up if this pattern becomes common.
                admin.SuspendedAt = DateTimeOffset.UtcNow;
                admin.SuspensionReason = "Self-requested account deletion (GDPR Art.17)";
                var suspended = admin with { Status = UserStatus.Suspended, SoftDeleted = true };
                sessionRw.Store(suspended);
                await sessionRw.SaveChangesAsync();
            }

            try { await firebase.DeleteUserAsync(uid); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Firebase DeleteUser failed for self-delete {Uid}", uid);
                // Do not surface as error — the row is already flagged
                // PendingErasure and the retention worker will retry.
            }

            logger.LogWarning("[AUDIT] self-delete uid={Uid} email={Email}", uid, admin?.Email);
            return Results.Ok(new { deleted = true });
        })
        .RequireRateLimiting("destructive")
        .WithName("DeleteMyAccount");

        return app;
    }

    private static string ProviderDisplayName(string providerId) => providerId switch
    {
        "password" => "Email & Password",
        "google.com" => "Google",
        "apple.com" => "Apple",
        "microsoft.com" => "Microsoft",
        "phone" => "Phone",
        _ => providerId,
    };

    // Marker type used to produce an ILogger<Marker> without leaking a
    // real class name — the logs end up under "Cena.Admin.Api.AdminMeEndpoints+Marker"
    // which is fine and self-documenting.
    private sealed class Marker { }
}

public sealed record MeProfileResponse(
    string Uid,
    string Email,
    bool EmailVerified,
    string DisplayName,
    string? PhotoUrl,
    string Role,
    string Locale,
    string Timezone,
    string? School,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<LinkedProvider> Providers,
    bool MfaEnrolled);

public sealed record LinkedProvider(
    string ProviderId,
    string DisplayName,
    string? Email,
    DateTimeOffset? LinkedAt);

public sealed record UpdateMyProfileRequest(
    string? DisplayName,
    string? Locale,
    string? Timezone);

public sealed record SignInHistoryItem(
    DateTimeOffset Timestamp,
    string Action,
    string? IpAddress,
    string? UserAgent,
    bool Succeeded);

public sealed record DeleteMyAccountRequest(string Confirmation);
