// =============================================================================
// Cena Platform -- CenaRoleDefinition Marten Document
// BKD-003: Role and permission definitions stored in PostgreSQL via Marten
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Marten document for role definitions with permission matrices.
/// Id = role identifier (e.g. "SUPER_ADMIN", "custom_content_lead").
/// </summary>
public record CenaRoleDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsPredefined { get; init; }
    public Dictionary<string, List<string>> Permissions { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool SoftDeleted { get; set; }
}
