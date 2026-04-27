// =============================================================================
// Cena Platform — ParentBindInviteService (TASK-E2E-A-04-BE)
//
// Two responsibilities:
//   1. Issue: mint a signed HS256 JWT carrying { studentSubjectId, instituteId,
//      parentEmail, relationship, jti, iat, exp } and persist a matching
//      ParentBindInviteDocument keyed by jti.
//   2. Consume: validate signature + exp + jti-not-consumed + tenant match,
//      mark consumed in a single Marten transaction, return the verified
//      invite metadata for the endpoint to act on.
//
// Signing key: ParentBindJwt:SigningKey config (or CENA_PARENT_BIND_JWT_SIGNING_KEY
// env). Falls back to a machine-name-derived key in dev so the host boots
// without extra config — production MUST set this.
//
// Issuer / Audience: "cena-parent-bind" / "cena-parent-bind". Distinct from
// the session-cookie JWT so a session compromise doesn't let an attacker
// mint bind invites (defence in depth — different keys would be even safer
// but reusing the SessionJwt config seam was the simpler-and-safe-enough
// choice here, matching the pattern SessionExchangeEndpoint already uses).
// =============================================================================

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Cena.Actors.Parent;

public enum ParentBindIssueOutcome
{
    Issued,
    InvalidStudent,
}

public sealed record ParentBindIssueResult(
    ParentBindIssueOutcome Outcome,
    string? Token,
    string? Jti,
    DateTimeOffset? ExpiresAt);

public enum ParentBindConsumeOutcome
{
    /// <summary>Token + invite valid, consumed, ready for the endpoint to grant.</summary>
    Verified,
    /// <summary>Signature, format, or claim shape failed — treat as 401 by the endpoint.</summary>
    InvalidSignature,
    /// <summary>Token signature ok but expired.</summary>
    Expired,
    /// <summary>jti already consumed (replay attack or duplicate click).</summary>
    AlreadyConsumed,
    /// <summary>jti unknown to the store (forged or wrong server).</summary>
    Unknown,
    /// <summary>Caller's tenant doesn't match invite tenant — cross-institute attempt.</summary>
    TenantMismatch,
    /// <summary>Caller's email doesn't match invite email when an email was set.</summary>
    EmailMismatch,
}

public sealed record ParentBindConsumeResult(
    ParentBindConsumeOutcome Outcome,
    ParentBindInviteDocument? Invite);

public interface IParentBindInviteService
{
    Task<ParentBindIssueResult> IssueAsync(
        string studentSubjectId,
        string instituteId,
        string parentEmail,
        string relationship,
        TimeSpan validFor,
        CancellationToken ct = default);

    /// <summary>
    /// Validate the JWT, look up its jti, and (if all checks pass) atomically
    /// mark the invite consumed by <paramref name="callerParentUid"/>. Returns
    /// the invite metadata so the endpoint can call <c>GrantAsync</c> on the
    /// binding store + emit the bus event.
    /// </summary>
    Task<ParentBindConsumeResult> ConsumeAsync(
        string token,
        string callerParentUid,
        string callerInstituteId,
        string? callerEmail,
        CancellationToken ct = default);
}

public sealed class ParentBindInviteService : IParentBindInviteService
{
    private const string Issuer = "cena-parent-bind";
    private const string Audience = "cena-parent-bind";
    private const string SigningKeyConfigKey = "ParentBindJwt:SigningKey";
    private const string SigningKeyEnvVar = "CENA_PARENT_BIND_JWT_SIGNING_KEY";

    private readonly IDocumentStore _store;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ParentBindInviteService> _logger;
    private readonly TimeProvider _clock;

    public ParentBindInviteService(
        IDocumentStore store,
        IConfiguration configuration,
        ILogger<ParentBindInviteService> logger,
        TimeProvider? clock = null)
    {
        _store = store;
        _configuration = configuration;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<ParentBindIssueResult> IssueAsync(
        string studentSubjectId,
        string instituteId,
        string parentEmail,
        string relationship,
        TimeSpan validFor,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectId)
            || string.IsNullOrWhiteSpace(instituteId)
            || string.IsNullOrWhiteSpace(parentEmail))
        {
            return new ParentBindIssueResult(ParentBindIssueOutcome.InvalidStudent, null, null, null);
        }

        var now = _clock.GetUtcNow();
        var expiresAt = now.Add(validFor);
        var jti = Guid.NewGuid().ToString("N");
        var rel = string.IsNullOrWhiteSpace(relationship) ? "parent" : relationship.Trim();

        var token = MintJwt(jti, now, expiresAt, studentSubjectId, instituteId, parentEmail, rel);

        var doc = new ParentBindInviteDocument
        {
            Id = jti,
            StudentSubjectId = studentSubjectId,
            InstituteId = instituteId,
            ParentEmail = parentEmail.Trim().ToLowerInvariant(),
            Relationship = rel,
            IssuedAt = now,
            ExpiresAt = expiresAt,
            ConsumedAt = null,
            ConsumedByParentUid = null,
        };

        await using var session = _store.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "[PARENT_BIND_INVITE] issued jti={Jti} student={StudentId} institute={Institute} expiresAt={ExpiresAt}",
            jti, studentSubjectId, instituteId, expiresAt);

        return new ParentBindIssueResult(ParentBindIssueOutcome.Issued, token, jti, expiresAt);
    }

    public async Task<ParentBindConsumeResult> ConsumeAsync(
        string token,
        string callerParentUid,
        string callerInstituteId,
        string? callerEmail,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new ParentBindConsumeResult(ParentBindConsumeOutcome.InvalidSignature, null);

        // ── Validate signature + exp + iss/aud ──
        ClaimsPrincipal principal;
        JwtSecurityToken jwt;
        try
        {
            var handler = new JwtSecurityTokenHandler();
            principal = handler.ValidateToken(token, BuildValidationParameters(), out var validatedToken);
            jwt = (JwtSecurityToken)validatedToken;
        }
        catch (SecurityTokenExpiredException)
        {
            return new ParentBindConsumeResult(ParentBindConsumeOutcome.Expired, null);
        }
        catch (SecurityTokenException)
        {
            return new ParentBindConsumeResult(ParentBindConsumeOutcome.InvalidSignature, null);
        }

        var jti = jwt.Id;
        if (string.IsNullOrWhiteSpace(jti))
            return new ParentBindConsumeResult(ParentBindConsumeOutcome.InvalidSignature, null);

        // ── Atomic check + consume in a single Marten tx ──
        await using var session = _store.LightweightSession();
        var doc = await session.LoadAsync<ParentBindInviteDocument>(jti, ct).ConfigureAwait(false);

        if (doc is null)
            return new ParentBindConsumeResult(ParentBindConsumeOutcome.Unknown, null);

        if (doc.ConsumedAt is not null)
            return new ParentBindConsumeResult(ParentBindConsumeOutcome.AlreadyConsumed, doc);

        // Tenant must match — defence in depth even though the signed JWT
        // already carries the institute claim. A signature compromise would
        // still fail at this gate because callerInstituteId comes from the
        // caller's own (Firebase-validated) custom claims, not the token.
        if (!string.Equals(doc.InstituteId, callerInstituteId, StringComparison.Ordinal))
            return new ParentBindConsumeResult(ParentBindConsumeOutcome.TenantMismatch, doc);

        // Email match when the invite recorded one. Skip when blank (test
        // bootstrap path that doesn't yet know the parent's email).
        if (!string.IsNullOrEmpty(doc.ParentEmail)
            && !string.IsNullOrWhiteSpace(callerEmail)
            && !string.Equals(doc.ParentEmail, callerEmail.Trim().ToLowerInvariant(), StringComparison.Ordinal))
        {
            return new ParentBindConsumeResult(ParentBindConsumeOutcome.EmailMismatch, doc);
        }

        var consumedDoc = doc with
        {
            ConsumedAt = _clock.GetUtcNow(),
            ConsumedByParentUid = callerParentUid,
        };
        session.Store(consumedDoc);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "[PARENT_BIND_INVITE] consumed jti={Jti} parent={ParentUid} student={StudentId} institute={Institute}",
            jti, callerParentUid, doc.StudentSubjectId, doc.InstituteId);

        return new ParentBindConsumeResult(ParentBindConsumeOutcome.Verified, consumedDoc);
    }

    // ── Helpers ──

    private string MintJwt(
        string jti, DateTimeOffset issuedAt, DateTimeOffset expiresAt,
        string studentSubjectId, string instituteId, string parentEmail, string relationship)
    {
        var key = GetSigningKey();
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, jti),
            new(JwtRegisteredClaimNames.Iat,
                issuedAt.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
            new("studentSubjectId", studentSubjectId),
            new("instituteId", instituteId),
            new("parentEmail", parentEmail.Trim().ToLowerInvariant()),
            new("relationship", relationship),
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private TokenValidationParameters BuildValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = GetSigningKey(),
        ClockSkew = TimeSpan.FromSeconds(30),
    };

    private SymmetricSecurityKey GetSigningKey()
    {
        var keyMaterial = _configuration[SigningKeyConfigKey]
            ?? Environment.GetEnvironmentVariable(SigningKeyEnvVar);
        if (string.IsNullOrWhiteSpace(keyMaterial))
        {
            // Dev-only fallback so the host boots without extra config. Same
            // pattern SessionExchangeEndpoint uses; production MUST set the
            // config value.
            keyMaterial = "cena-dev-parent-bind-jwt-key-" + Environment.MachineName;
        }
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
        return new SymmetricSecurityKey(bytes);
    }
}
