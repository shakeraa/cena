// =============================================================================
// Cena Platform -- Student Data Exporter (FIND-privacy-006)
// SEC-003 / GDPR Article 20: Data portability export for student records.
//
// Produces a structured JSON document containing ALL student data:
// - Profile snapshot (with PII annotations)
// - Tutoring session history
// - Learning session events
// - All domain events in the student's stream
// =============================================================================

using System.Reflection;
using System.Text.Json.Serialization;
using Cena.Actors.Events;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// A single field entry in the GDPR portability export.
/// </summary>
public sealed record ExportedField(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] object? Value,
    [property: JsonPropertyName("piiLevel")] string PiiLevel,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("requiresEncryption")] bool RequiresEncryption);

/// <summary>
/// Full portability export document.
/// FIND-privacy-006: Now includes tutor history, sessions, and events.
/// </summary>
public sealed record StudentDataExport(
    [property: JsonPropertyName("exportedAt")] DateTimeOffset ExportedAt,
    [property: JsonPropertyName("studentId")] string StudentId,
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("profile")] IReadOnlyList<ExportedField> Profile,
    [property: JsonPropertyName("tutoringSessions")] IReadOnlyList<TutoringSessionExport> TutoringSessions,
    [property: JsonPropertyName("learningSessions")] IReadOnlyList<LearningSessionExport> LearningSessions,
    [property: JsonPropertyName("events")] IReadOnlyList<StudentEventExport> Events,
    [property: JsonPropertyName("eventCount")] int EventCount,
    [property: JsonPropertyName("notice")] string Notice);

/// <summary>
/// Tutoring session export record.
/// </summary>
public sealed record TutoringSessionExport(
    string SessionId,
    string Subject,
    string ConceptId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int TurnCount,
    string Status);

/// <summary>
/// Learning session export record.
/// </summary>
public sealed record LearningSessionExport(
    string SessionId,
    string[] Subjects,
    string Mode,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int? QuestionsAttempted,
    int? QuestionsCorrect);

/// <summary>
/// Student domain event export.
/// </summary>
public sealed record StudentEventExport(
    string EventType,
    DateTimeOffset Timestamp,
    object Data);

/// <summary>
/// Exports all persisted student data for GDPR Article 20 portability requests.
/// FIND-privacy-006: Extended to include tutor history, sessions, and events.
/// </summary>
public static class StudentDataExporter
{
    private const string SchemaVersion = "2.0";
    private const string PortabilityNotice =
        "This export was generated in response to a GDPR Article 20 data portability request. " +
        "It includes your profile, tutoring sessions, learning activity, and all events. " +
        "Retain securely and share only with authorized representatives.";

    /// <summary>
    /// Builds a complete portability export including profile, tutor history, sessions, and events.
    /// </summary>
    public static async Task<StudentDataExport> ExportAsync(
        string studentId,
        IDocumentStore store,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);

        await using var session = store.QuerySession();

        // 1. Profile snapshot
        var snapshot = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        var profileFields = snapshot != null 
            ? ExtractFields(snapshot) 
            : new List<ExportedField>();

        // 2. Tutoring sessions
        var tutoringSessions = await session.Query<TutoringSessionDocument>()
            .Where(t => t.StudentId == studentId)
            .OrderByDescending(t => t.StartedAt)
            .ToListAsync();

        var tutoringExports = tutoringSessions.Select(t => new TutoringSessionExport(
            t.SessionId,
            t.Subject,
            t.ConceptId,
            t.StartedAt,
            t.EndedAt,
            t.TotalTurns,
            t.EndedAt.HasValue ? "completed" : "active"
        )).ToList();

        // 3. Learning sessions from events
        var events = await session.Events.FetchStreamAsync(studentId);
        var learningSessions = events
            .Where(e => e.Data is LearningSessionStarted_V1)
            .Select(e => (LearningSessionStarted_V1)e.Data)
            .Select(ls => new LearningSessionExport(
                ls.SessionId,
                ls.Subjects,
                ls.Mode,
                ls.StartedAt,
                null, // EndedAt from LearningSessionEnded_V1
                null,
                null))
            .ToList();

        // Match ends with starts
        var endedSessions = events
            .Where(e => e.Data is LearningSessionEnded_V1)
            .Select(e => (LearningSessionEnded_V1)e.Data)
            .ToDictionary(e => e.SessionId);

        learningSessions = learningSessions.Select(ls =>
        {
            if (endedSessions.TryGetValue(ls.SessionId, out var ended))
            {
                return ls with 
                { 
                    EndedAt = ended.EndedAt, 
                    QuestionsAttempted = ended.QuestionsAttempted,
                    QuestionsCorrect = ended.QuestionsCorrect
                };
            }
            return ls;
        }).ToList();

        // 4. All domain events
        var eventExports = events.Select(e => new StudentEventExport(
            e.Data?.GetType().Name ?? "Unknown",
            e.Timestamp,
            e.Data ?? new object()
        )).ToList();

        logger?.LogInformation(
            "FIND-privacy-006: GDPR export for {StudentId}: Profile={ProfileFields}, " +
            "Tutoring={TutoringCount}, Learning={LearningCount}, Events={EventCount}",
            studentId, profileFields.Count, tutoringExports.Count, learningSessions.Count, eventExports.Count);

        return new StudentDataExport(
            ExportedAt: DateTimeOffset.UtcNow,
            StudentId: studentId,
            SchemaVersion: SchemaVersion,
            Profile: profileFields,
            TutoringSessions: tutoringExports,
            LearningSessions: learningSessions,
            Events: eventExports,
            EventCount: eventExports.Count,
            Notice: PortabilityNotice);
    }

    /// <summary>
    /// Legacy synchronous export for backward compatibility (profile only).
    /// </summary>
    [Obsolete("Use ExportAsync for complete data export")]
    public static StudentDataExport Export(
        string studentId,
        object dataObject,
        ILogger? logger = null)
    {
        var profileFields = ExtractFields(dataObject);

        logger?.LogWarning("Using legacy Export method - tutor history, sessions, and events not included");

        return new StudentDataExport(
            ExportedAt: DateTimeOffset.UtcNow,
            StudentId: studentId,
            SchemaVersion: "1.0",
            Profile: profileFields,
            TutoringSessions: new List<TutoringSessionExport>(),
            LearningSessions: new List<LearningSessionExport>(),
            Events: new List<StudentEventExport>(),
            EventCount: 0,
            Notice: PortabilityNotice + " [Legacy export - incomplete data]");
    }

    private static List<ExportedField> ExtractFields(object dataObject)
    {
        var properties = dataObject.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var fields = new List<ExportedField>(properties.Length);

        foreach (var prop in properties)
        {
            object? rawValue;
            try
            {
                rawValue = prop.GetValue(dataObject);
            }
            catch
            {
                rawValue = null;
            }

            var piiAttr = prop.GetCustomAttribute<PiiAttribute>();

            if (piiAttr is not null)
            {
                fields.Add(new ExportedField(
                    Name: prop.Name,
                    Value: rawValue,
                    PiiLevel: piiAttr.Level.ToString(),
                    Category: piiAttr.Category,
                    RequiresEncryption: piiAttr.RequiresEncryption));
            }
            else
            {
                fields.Add(new ExportedField(
                    Name: prop.Name,
                    Value: rawValue,
                    PiiLevel: nameof(Compliance.PiiLevel.None),
                    Category: string.Empty,
                    RequiresEncryption: false));
            }
        }

        return fields;
    }
}
