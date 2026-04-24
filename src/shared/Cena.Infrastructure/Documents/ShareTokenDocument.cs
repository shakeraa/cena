// =============================================================================
// Cena Platform — Share Token Document (STB-00b)
// Marten document for view-only share tokens
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Share token document for creating scoped view-only access tokens.
/// </summary>
public class ShareTokenDocument
{
    public string Id { get; set; } = "";
    public string Token { get; set; } = ""; // The actual token string (hashed in practice)
    public string StudentId { get; set; } = "";
    public string Audience { get; set; } = ""; // 'teacher' | 'parent' | 'peer'
    public string[] Scopes { get; set; } = Array.Empty<string>(); // 'progress' | 'achievements' | 'activity'
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRevoked { get; set; } = false;
    public DateTime? RevokedAt { get; set; }
}
