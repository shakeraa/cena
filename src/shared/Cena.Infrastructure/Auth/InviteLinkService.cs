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

    public static InviteRedemptionResult Ok(string classroomId, string? instituteId) =>
        new() { Success = true, ClassroomId = classroomId, InstituteId = instituteId };

    public static InviteRedemptionResult Fail(string error) =>
        new() { Success = false, Error = error };
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
