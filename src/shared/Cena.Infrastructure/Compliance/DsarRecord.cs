// =============================================================================
// Cena Platform -- DSAR Record (Data Subject Access Request)
//
// Marten document for persisting Data Subject Access Requests under
// GDPR Article 12 + Israel PPL 13. Platform must track and respond
// within 30 days.
//
// Lives in Cena.Infrastructure.Compliance so the central Marten schema
// config (Cena.Actors.Configuration.MartenConfiguration) can register
// the document type — the actor-host is the single owner of schema
// reconciliation, and host-side document classes can't be referenced
// from there because Cena.Actors does not depend on Cena.*.Api.Host.
// =============================================================================

namespace Cena.Infrastructure.Compliance;

public sealed class DsarRecord
{
    public string Id { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string Message { get; set; } = "";
    public string? ContactEmail { get; set; }
    public string Status { get; set; } = "Submitted";
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset SlaDeadline { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
}
