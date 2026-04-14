// =============================================================================
// Cena Platform — Invite Link Service (TENANCY-P3f)
// Signed JWT + short code + QR code generation for classroom invites.
// Rate-limited redemption to prevent abuse.
// =============================================================================

using System.Security.Cryptography;
using System.Text;

namespace Cena.Infrastructure.Auth;

/// <summary>
/// TENANCY-P3f: Generates and validates classroom invite links.
/// </summary>
public interface IInviteLinkService
{
    /// <summary>
    /// Creates a new invite link for a classroom.
    /// Returns a short code (6 chars) and a signed JWT for the full URL.
    /// </summary>
    InviteLink CreateInvite(CreateInviteRequest request);

    /// <summary>
    /// Validates and redeems an invite code. Returns the classroom ID if valid.
    /// </summary>
    InviteRedemptionResult Redeem(string code);
}

/// <summary>
/// Request to create a classroom invite.
/// </summary>
public sealed record CreateInviteRequest(
    string ClassroomId,
    string CreatedByMentorId,
    string? InstituteId,
    int MaxUses,
    TimeSpan? ExpiresIn);

/// <summary>
/// A generated invite link with short code for sharing.
/// </summary>
public sealed record InviteLink
{
    /// <summary>6-character alphanumeric code for short URLs and QR codes.</summary>
    public string ShortCode { get; init; } = "";

    /// <summary>Signed JWT containing classroom + metadata. For deep-link URLs.</summary>
    public string SignedToken { get; init; } = "";

    /// <summary>Full URL: https://cena.app/join/{ShortCode}</summary>
    public string JoinUrl { get; init; } = "";

    /// <summary>QR code data URL (SVG).</summary>
    public string? QrCodeSvg { get; init; }

    public string ClassroomId { get; init; } = "";
    public int MaxUses { get; init; }
    public int CurrentUses { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool IsExpired(DateTimeOffset now) => ExpiresAt.HasValue && now >= ExpiresAt.Value;
    public bool IsExhausted => MaxUses > 0 && CurrentUses >= MaxUses;
}

/// <summary>
/// Result of redeeming an invite code.
/// </summary>
public sealed record InviteRedemptionResult
{
    public bool Success { get; init; }
    public string? ClassroomId { get; init; }
    public string? InstituteId { get; init; }
    public string? Error { get; init; }
    /// <summary>PP-016: Failed attempt count on this code (for rate limit feedback).</summary>
    public int FailedAttempts { get; init; }

    public static InviteRedemptionResult Ok(string classroomId, string? instituteId) =>
        new() { Success = true, ClassroomId = classroomId, InstituteId = instituteId };

    public static InviteRedemptionResult Fail(string error) =>
        new() { Success = false, Error = error };

    public static InviteRedemptionResult RateLimited(int retryAfterSeconds) =>
        new() { Success = false, Error = $"Too many attempts. Retry after {retryAfterSeconds} seconds." };
}

/// <summary>
/// Marten document for persisting invite codes.
/// </summary>
public sealed class InviteCodeDocument
{
    public string Id { get; set; } = "";
    public string ShortCode { get; set; } = "";
    public string ClassroomId { get; set; } = "";
    public string? InstituteId { get; set; }
    public string CreatedByMentorId { get; set; } = "";
    public int MaxUses { get; set; }
    public int CurrentUses { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
}

/// <summary>
/// PP-016: In-memory invite code redemption rate limiter.
/// Enforces per-IP and per-code limits to prevent brute-force guessing.
/// Production: replace with Redis-backed counters via IRateLimitService.
/// </summary>
public sealed class InviteRedeemRateLimiter
{
    /// <summary>Max redemption attempts per IP per minute.</summary>
    public const int MaxPerIpPerMinute = 10;

    /// <summary>Max failed attempts per code before lockout.</summary>
    public const int MaxFailedPerCode = 5;

    /// <summary>Lockout duration after exceeding per-code failures.</summary>
    public static readonly TimeSpan CodeLockoutDuration = TimeSpan.FromMinutes(15);

    // Thread-safe counters (in production, use Redis)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, DateTimeOffset WindowStart)>
        _ipCounters = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int FailCount, DateTimeOffset? LockedUntil)>
        _codeCounters = new();

    /// <summary>
    /// Check if the redemption attempt is allowed.
    /// Returns null if allowed, or an error string if rate-limited.
    /// </summary>
    public string? CheckLimit(string ipAddress, string code)
    {
        var now = DateTimeOffset.UtcNow;

        // Per-code lockout
        if (_codeCounters.TryGetValue(code, out var codeState) && codeState.LockedUntil.HasValue)
        {
            if (now < codeState.LockedUntil.Value)
                return $"Code temporarily locked. Retry after {(int)(codeState.LockedUntil.Value - now).TotalSeconds} seconds.";
            // Lockout expired — reset
            _codeCounters.TryRemove(code, out _);
        }

        // Per-IP window
        var ipKey = $"invite-ip:{ipAddress}";
        var ipState = _ipCounters.GetOrAdd(ipKey, _ => (0, now));
        if ((now - ipState.WindowStart).TotalMinutes >= 1)
            ipState = (0, now);

        if (ipState.Count >= MaxPerIpPerMinute)
            return "Too many redemption attempts. Try again in a minute.";

        _ipCounters[ipKey] = (ipState.Count + 1, ipState.WindowStart);
        return null;
    }

    /// <summary>Record a failed redemption attempt for per-code lockout.</summary>
    public void RecordFailure(string code)
    {
        var state = _codeCounters.GetOrAdd(code, _ => (0, null));
        var newCount = state.FailCount + 1;
        DateTimeOffset? lockedUntil = newCount >= MaxFailedPerCode
            ? DateTimeOffset.UtcNow + CodeLockoutDuration
            : null;
        _codeCounters[code] = (newCount, lockedUntil);
    }
}

/// <summary>
/// Generates deterministic short codes from classroom + timestamp.
/// </summary>
public static class ShortCodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // No I/O/0/1 (confusable)

    public static string Generate(int length = 6)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        var sb = new StringBuilder(length);
        foreach (var b in bytes)
            sb.Append(Alphabet[b % Alphabet.Length]);
        return sb.ToString();
    }
}
