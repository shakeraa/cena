// =============================================================================
// Cena Platform -- FERPA Compliance: Student Record Access Log
// REV-013: Append-only audit document for all student data access
// =============================================================================

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Marten document recording every access to student-identifiable data endpoints.
/// Append-only by design -- no update or delete endpoints exposed.
/// Indexed on AccessedBy, StudentId, AccessedAt for compliance queries.
/// </summary>
public class StudentRecordAccessLog
{
    public Guid Id { get; set; }
    public DateTimeOffset AccessedAt { get; set; }
    public string AccessedBy { get; set; } = "";
    public string AccessorRole { get; set; } = "";
    public string? AccessorSchool { get; set; }
    public string? StudentId { get; set; }
    public string Endpoint { get; set; } = "";
    public string HttpMethod { get; set; } = "";
    public int StatusCode { get; set; }
    public string? IpAddress { get; set; }
}
