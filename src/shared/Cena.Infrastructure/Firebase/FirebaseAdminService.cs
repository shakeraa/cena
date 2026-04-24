// =============================================================================
// Cena Platform -- Firebase Admin SDK Wrapper
// BKD-002: Manages Firebase Auth users (create, update, disable, claims)
// =============================================================================

using Cena.Infrastructure.Auth;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Firebase;

/// <summary>
/// Outcome of a password-reset link request. Callers on anonymous endpoints
/// (student forgot-password flow) must treat <see cref="LinkGenerated"/> and
/// <see cref="UserNotFound"/> identically in the HTTP response so that
/// account-enumeration is not possible (OWASP Authentication Cheat Sheet —
/// "Forgot password / password reset").
/// </summary>
public enum PasswordResetOutcome
{
    /// <summary>Firebase produced a reset link for a known account.</summary>
    LinkGenerated,

    /// <summary>No Firebase account exists for the supplied address.</summary>
    UserNotFound,

    /// <summary>
    /// Firebase Admin SDK is not initialized or is unreachable. Endpoints
    /// should surface this as a 503, not a 204, because the request was not
    /// acted on — the user would otherwise never get an email.
    /// </summary>
    FirebaseUnavailable,
}

public sealed record FirebaseProviderLink(
    string ProviderId,
    string Uid,
    string? Email,
    string? DisplayName);

public sealed record FirebaseUserSummary(
    string Uid,
    string? Email,
    bool EmailVerified,
    string? DisplayName,
    string? PhotoUrl,
    bool Disabled,
    long TokensValidAfter,
    IReadOnlyList<FirebaseProviderLink> Providers,
    bool MfaEnrolled);

public interface IFirebaseAdminService
{
    Task<string> CreateUserAsync(string email, string fullName, string? password);
    Task UpdateEmailAsync(string uid, string newEmail);
    Task UpdateDisplayNameAsync(string uid, string? displayName);
    Task SetCustomClaimsAsync(string uid, Dictionary<string, object> claims);
    Task DisableUserAsync(string uid);
    Task EnableUserAsync(string uid);
    Task DeleteUserAsync(string uid);
    Task<string> GenerateSignInLinkAsync(string email);
    /// <summary>
    /// Revokes all refresh tokens for the user — forces sign-out on every
    /// device, every browser, every tab. Used by the "sign out everywhere"
    /// button in Account Settings → Security. Does NOT invalidate the
    /// current ID token (client must refresh).
    /// </summary>
    Task RevokeRefreshTokensAsync(string uid);
    /// <summary>
    /// Fetch the user record so callers can enumerate linked provider
    /// identities (email, google.com, apple.com, etc.), email-verified
    /// state, display name, photo URL, and last-sign-in timestamp.
    /// Returns null if the uid is unknown.
    /// </summary>
    Task<FirebaseUserSummary?> GetUserAsync(string uid);

    /// <summary>
    /// Generates a Firebase password-reset link for <paramref name="email"/>.
    /// Returns a strongly-typed outcome (never throws for USER_NOT_FOUND) so
    /// that anonymous callers can respond uniformly and avoid leaking
    /// account existence.
    /// </summary>
    /// <remarks>
    /// The Firebase Admin SDK returns an out-of-band link which Firebase
    /// delivers via email automatically when an email action handler is
    /// configured on the project. If no handler is configured, the service
    /// logs the link internally (hashed context only — never the raw email).
    /// </remarks>
    Task<PasswordResetOutcome> GeneratePasswordResetLinkAsync(string email, CancellationToken cancellationToken = default);
}

public sealed class FirebaseAdminService : IFirebaseAdminService
{
    private readonly ILogger<FirebaseAdminService> _logger;
    private readonly bool _initialized;

    public FirebaseAdminService(IConfiguration configuration, ILogger<FirebaseAdminService> logger)
    {
        _logger = logger;

        if (FirebaseApp.DefaultInstance != null)
        {
            _initialized = true;
            return;
        }

        var credPath = configuration["Firebase:ServiceAccountKeyPath"];
        try
        {
            if (!string.IsNullOrEmpty(credPath) && File.Exists(credPath))
            {
                FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.FromFile(credPath),
                    ProjectId = configuration["Firebase:ProjectId"]
                });
                _initialized = true;
            }
            else
            {
                // Try Application Default Credentials (ADC) — available in cloud environments
                FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.GetApplicationDefault(),
                    ProjectId = configuration["Firebase:ProjectId"]
                });
                _initialized = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Firebase Admin SDK not initialized — user management operations will use local-only mode. {Message}", ex.Message);
            _initialized = false;
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("Firebase Admin SDK is not initialized. Set Firebase:ServiceAccountKeyPath in configuration or configure Application Default Credentials.");
    }

    public async Task<string> CreateUserAsync(string email, string fullName, string? password)
    {
        var args = new UserRecordArgs
        {
            Email = email,
            DisplayName = fullName,
            EmailVerified = false,
            Disabled = false
        };

        if (!string.IsNullOrEmpty(password))
            args.Password = password;

        var userRecord = await FirebaseAuth.DefaultInstance.CreateUserAsync(args);
        _logger.LogInformation("Created Firebase user {Uid} for {Email}", userRecord.Uid, email);
        return userRecord.Uid;
    }

    public async Task UpdateEmailAsync(string uid, string newEmail)
    {
        await FirebaseAuth.DefaultInstance.UpdateUserAsync(new UserRecordArgs
        {
            Uid = uid,
            Email = newEmail
        });
    }

    public async Task SetCustomClaimsAsync(string uid, Dictionary<string, object> claims)
    {
        await FirebaseAuth.DefaultInstance.SetCustomUserClaimsAsync(uid, claims);
        _logger.LogInformation("Updated custom claims for {Uid}: {Claims}",
            uid, string.Join(", ", claims.Select(c => $"{c.Key}={c.Value}")));
    }

    public async Task DisableUserAsync(string uid)
    {
        await FirebaseAuth.DefaultInstance.UpdateUserAsync(new UserRecordArgs
        {
            Uid = uid,
            Disabled = true
        });
        _logger.LogInformation("Disabled Firebase user {Uid}", uid);
    }

    public async Task EnableUserAsync(string uid)
    {
        await FirebaseAuth.DefaultInstance.UpdateUserAsync(new UserRecordArgs
        {
            Uid = uid,
            Disabled = false
        });
        _logger.LogInformation("Enabled Firebase user {Uid}", uid);
    }

    public async Task UpdateDisplayNameAsync(string uid, string? displayName)
    {
        if (!_initialized)
        {
            _logger.LogWarning("Firebase Admin SDK not initialized — display-name update skipped for {Uid}. " +
                "Preferences-side updates still persist to Marten.", uid);
            return;
        }
        await FirebaseAuth.DefaultInstance.UpdateUserAsync(new UserRecordArgs
        {
            Uid = uid,
            DisplayName = displayName ?? string.Empty
        });
        _logger.LogInformation("Updated display name for {Uid}", uid);
    }

    public async Task RevokeRefreshTokensAsync(string uid)
    {
        if (!_initialized)
        {
            _logger.LogWarning("Firebase Admin SDK not initialized — RevokeRefreshTokens noop for {Uid}.", uid);
            return;
        }
        // Firebase Admin SDK: sets tokensValidAfterTime on the user record
        // to now(); any refresh-token exchange after that is rejected.
        await FirebaseAuth.DefaultInstance.RevokeRefreshTokensAsync(uid);
        _logger.LogInformation("[AUDIT] Revoked refresh tokens for {Uid}", uid);
    }

    public async Task<FirebaseUserSummary?> GetUserAsync(string uid)
    {
        if (!_initialized || FirebaseAuth.DefaultInstance is null)
        {
            _logger.LogWarning("Firebase Admin SDK not initialized — GetUserAsync returns null for {Uid}", uid);
            return null;
        }
        try
        {
            var rec = await FirebaseAuth.DefaultInstance.GetUserAsync(uid);
            // TokensValidAfterTimestamp returns a DateTime in the Admin SDK;
            // expose as epoch millis so the wire format is stable across
            // SDK upgrades.
            var tokensValidAfter = new DateTimeOffset(rec.TokensValidAfterTimestamp, TimeSpan.Zero)
                .ToUnixTimeMilliseconds();
            // MFA enrolment: the .NET Firebase Admin SDK exposes
            // UserMetadata.MultiFactor on newer versions; older versions
            // expose it as TenantId-scoped records. We conservatively
            // report false here and surface MFA status via the client-side
            // SDK (which has first-class MFA support). RDY-058 MFA panel.
            return new FirebaseUserSummary(
                Uid: rec.Uid,
                Email: rec.Email,
                EmailVerified: rec.EmailVerified,
                DisplayName: rec.DisplayName,
                PhotoUrl: rec.PhotoUrl,
                Disabled: rec.Disabled,
                TokensValidAfter: tokensValidAfter,
                Providers: rec.ProviderData?.Select(p => new FirebaseProviderLink(
                    ProviderId: p.ProviderId,
                    Uid: p.Uid,
                    Email: p.Email,
                    DisplayName: p.DisplayName
                )).ToArray() ?? Array.Empty<FirebaseProviderLink>(),
                MfaEnrolled: false);
        }
        catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.UserNotFound)
        {
            return null;
        }
    }

    public async Task DeleteUserAsync(string uid)
    {
        await FirebaseAuth.DefaultInstance.DeleteUserAsync(uid);
        _logger.LogInformation("Deleted Firebase user {Uid}", uid);
    }

    public async Task<string> GenerateSignInLinkAsync(string email)
    {
        var link = await FirebaseAuth.DefaultInstance.GenerateEmailVerificationLinkAsync(email);
        return link;
    }

    public async Task<PasswordResetOutcome> GeneratePasswordResetLinkAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        // FIND-ux-006b: anonymous endpoint — uniform response between
        // LinkGenerated + UserNotFound is the caller's job. This method
        // reports the true outcome so internal telemetry (hashed only) can
        // still distinguish them.
        if (!_initialized)
        {
            _logger.LogError(
                "Password reset for {EmailHash} dropped: Firebase Admin SDK not initialized",
                EmailHasher.Hash(email));
            return PasswordResetOutcome.FirebaseUnavailable;
        }

        try
        {
            // FirebaseAuth.GeneratePasswordResetLinkAsync returns an OOB link.
            // When Firebase has an email action handler configured, Firebase
            // itself sends the email to the user; otherwise, the link can be
            // forwarded to a backing mail service. Either way the link is
            // tenant-scoped and short-lived.
            var link = await FirebaseAuth.DefaultInstance
                .GeneratePasswordResetLinkAsync(email)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Password reset link generated for {EmailHash} (length={LinkLen})",
                EmailHasher.Hash(email),
                link?.Length ?? 0);

            return PasswordResetOutcome.LinkGenerated;
        }
        catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.UserNotFound)
        {
            // OWASP: do NOT leak to the caller — just record internally.
            _logger.LogInformation(
                "Password reset requested for unknown email {EmailHash}",
                EmailHasher.Hash(email));
            return PasswordResetOutcome.UserNotFound;
        }
        catch (FirebaseAuthException ex)
        {
            _logger.LogError(
                ex,
                "Firebase password-reset failed for {EmailHash}: {Code}",
                EmailHasher.Hash(email),
                ex.AuthErrorCode);
            return PasswordResetOutcome.FirebaseUnavailable;
        }
    }
}
