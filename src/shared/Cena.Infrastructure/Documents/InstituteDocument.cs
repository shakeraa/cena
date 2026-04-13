// =============================================================================
// Cena Platform — Institute Document (TENANCY-P1a)
// Marten document for multi-institute tenancy
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Type of educational institute represented by this document.
/// </summary>
public enum InstituteType
{
    Platform,
    School,
    PrivateTutor,
    CramSchool,
    NGO
}

/// <summary>
/// Institute document for multi-institute tenancy.
/// Represents a school, tutoring center, or other educational organization.
/// </summary>
public class InstituteDocument
{
    /// <summary>
    /// Stable Marten document identity. Format: <c>institute-{slug}</c>.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Domain identity alias. Always equal to <see cref="Id"/>.
    /// </summary>
    public string InstituteId { get; set; } = "";

    /// <summary>
    /// Display name of the institute.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Classification of the institute.
    /// </summary>
    public InstituteType Type { get; set; } = InstituteType.School;

    /// <summary>
    /// ISO country code or free-form country name.
    /// </summary>
    public string Country { get; set; } = "";

    /// <summary>
    /// Primary mentor or owner associated with the institute.
    /// </summary>
    public string MentorId { get; set; } = "";

    /// <summary>
    /// Timestamp when the institute record was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
