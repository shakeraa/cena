// =============================================================================
// Cena Platform — Device Session Document (STB-00b)
// Marten document for student device/session tracking
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Device session document for tracking student devices and session revocation.
/// </summary>
public class DeviceSessionDocument
{
    public string Id { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string Platform { get; set; } = ""; // 'web' | 'ios' | 'android'
    public string? DeviceName { get; set; }
    public string? DeviceModel { get; set; }
    public string? OsVersion { get; set; }
    public string? AppVersion { get; set; }
    public string? Browser { get; set; }
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public string? LastIpAddress { get; set; }
    public bool IsRevoked { get; set; } = false;
    public DateTime? RevokedAt { get; set; }
}
