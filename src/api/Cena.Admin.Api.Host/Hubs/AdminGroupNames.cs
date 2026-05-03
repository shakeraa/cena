// =============================================================================
// Cena Platform — Admin SignalR group naming conventions (RDY-060)
//
// Canonical group names. Centralised here so the hub, the NATS bridge,
// and future admin consumers all agree on the string keys. Avoid ad-hoc
// "school-" + id construction anywhere else in the admin-api.
// =============================================================================

namespace Cena.Admin.Api.Host.Hubs;

public static class AdminGroupNames
{
    /// <summary>System-wide events visible to SUPER_ADMIN only.</summary>
    public const string System = "admin:system";

    /// <summary>Ingestion pipeline — moderator+ access.</summary>
    public const string Ingestion = "admin:ingestion";

    public static string School(string schoolId) =>
        $"admin:school:{Guard(schoolId)}";

    public static string StudentInsights(string studentId) =>
        $"admin:student-insights:{Guard(studentId)}";

    public static string Classroom(string classroomId) =>
        $"admin:classroom:{Guard(classroomId)}";

    private static string Guard(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Group id must be non-empty", nameof(id));
        // Defensive: disallow colons + whitespace in ids so a crafted id
        // can't collide with group-name segments.
        foreach (var ch in id)
        {
            if (char.IsWhiteSpace(ch) || ch == ':')
                throw new ArgumentException(
                    "Group id must not contain whitespace or ':'", nameof(id));
        }
        return id;
    }
}
