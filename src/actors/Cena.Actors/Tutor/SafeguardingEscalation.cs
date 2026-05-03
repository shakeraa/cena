// =============================================================================
// Cena Platform -- Safeguarding Escalation Service (FIND-privacy-008)
// Routes safeguarding alerts to the moderation queue when the classifier
// detects a concern. Suppresses the Anthropic call and returns a "talk to
// a trusted adult" response with a localized child-helpline number.
// =============================================================================

using Microsoft.Extensions.Logging;
using Marten;

namespace Cena.Actors.Tutor;

/// <summary>
/// Handles safeguarding escalation: creates an alert, appends an event,
/// and produces a student-facing response.
/// </summary>
public interface ISafeguardingEscalation
{
    /// <summary>
    /// Create a safeguarding alert and return the student-facing response.
    /// The caller must NOT store the student message and must NOT call the LLM.
    /// </summary>
    Task<SafeguardingEscalationResult> EscalateAsync(
        string studentId,
        string threadId,
        SafeguardingResult classification,
        string? market,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a safeguarding escalation. Contains the response text to show
/// the student and the alert that was created.
/// </summary>
public sealed record SafeguardingEscalationResult(
    string StudentResponse,
    SafeguardingAlert Alert);

public sealed class SafeguardingEscalation : ISafeguardingEscalation
{
    private readonly IDocumentStore _store;
    private readonly ISafeguardingClassifier _classifier;
    private readonly ILogger<SafeguardingEscalation> _logger;

    public SafeguardingEscalation(
        IDocumentStore store,
        ISafeguardingClassifier classifier,
        ILogger<SafeguardingEscalation> logger)
    {
        _store = store;
        _classifier = classifier;
        _logger = logger;
    }

    public async Task<SafeguardingEscalationResult> EscalateAsync(
        string studentId,
        string threadId,
        SafeguardingResult classification,
        string? market,
        CancellationToken ct = default)
    {
        var alertId = $"safeguard_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        var alert = new SafeguardingAlert(
            AlertId: alertId,
            StudentId: studentId,
            Timestamp: now,
            Severity: classification.Severity,
            TriggeredCategories: classification.TriggeredCategories,
            ThreadId: threadId,
            Market: market);

        // Append the domain event and store the alert document atomically.
        var domainEvent = new SafeguardingConcernRaised_V1(
            StudentId: studentId,
            ThreadId: threadId,
            Timestamp: now,
            Severity: classification.Severity,
            TriggeredCategories: classification.TriggeredCategories);

        await using var session = _store.LightweightSession();
        session.Store(new SafeguardingAlertDocument
        {
            Id = alertId,
            AlertId = alertId,
            StudentId = studentId,
            ThreadId = threadId,
            Severity = classification.Severity.ToString(),
            Categories = classification.TriggeredCategories.ToList(),
            Market = market,
            CreatedAt = now.UtcDateTime,
            Status = "open"
        });
        session.Events.Append(studentId, domainEvent);
        await session.SaveChangesAsync(ct);

        _logger.LogWarning(
            "[SAFEGUARDING] alert_created id={AlertId} student={StudentId} severity={Severity} categories=[{Categories}]",
            alertId, studentId, classification.Severity, string.Join(", ", classification.TriggeredCategories));

        var response = _classifier.GetSafeguardingResponse(market);

        return new SafeguardingEscalationResult(
            StudentResponse: response,
            Alert: alert);
    }
}

/// <summary>
/// Marten document for persisting safeguarding alerts to the moderation queue.
/// Content of the student message is intentionally NOT stored -- only metadata.
/// </summary>
public class SafeguardingAlertDocument
{
    public string Id { get; set; } = "";
    public string AlertId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string ThreadId { get; set; } = "";
    public string Severity { get; set; } = "";
    public List<string> Categories { get; set; } = new();
    public string? Market { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>open | acknowledged | resolved</summary>
    public string Status { get; set; } = "open";
}
