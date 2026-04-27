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

        // 1. Profile snapshot — read directly from the snapshot table.
        // The owning type (Cena.Actors.Events.StudentProfileSnapshot) is in
        // Cena.Actors which would create a cycle if referenced from
        // Cena.Infrastructure, so we query the JSONB column via raw SQL
        // and pluck only the fields we need. The previous LoadAsync<T>
        // approach mapped to the wrong table (mt_doc_studentprofileref)
        // because Marten resolves the table by .NET type name.
        var snapshot = await LoadSnapshotRefAsync(session, studentId);
        var profileFields = snapshot != null
            ? ExtractFields(snapshot)
            : new List<ExportedField>();

        // 2. Tutoring sessions (query TutorThreadDocument which lives in Infrastructure)
        var tutorThreads = await session.Query<TutorThreadDocument>()
            .Where(t => t.StudentId == studentId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        var tutoringExports = tutorThreads.Select(t => new TutoringSessionExport(
            t.ThreadId,
            t.Subject ?? "",
            t.Topic ?? "",
            new DateTimeOffset(t.CreatedAt, TimeSpan.Zero),
            t.IsArchived ? new DateTimeOffset(t.UpdatedAt, TimeSpan.Zero) : null,
            t.MessageCount,
            t.IsArchived ? "completed" : "active"
        )).ToList();

        // 3. Learning sessions from events (use reflection to avoid Actors dependency)
        var events = await session.Events.FetchStreamAsync(studentId);
        var learningSessions = events
            .Where(e => e.Data?.GetType().Name == "LearningSessionStarted_V1")
            .Select(e => ExtractLearningSession(e.Data!))
            .Where(ls => ls != null)
            .Select(ls => ls!)
            .ToList();

        // Match ends with starts
        var endedMap = events
            .Where(e => e.Data?.GetType().Name == "LearningSessionEnded_V1")
            .Select(e => ExtractSessionEnd(e.Data!))
            .Where(se => se != null)
            .ToDictionary(se => se!.Value.SessionId, se => se!.Value);

        learningSessions = learningSessions.Select(ls =>
        {
            if (endedMap.TryGetValue(ls.SessionId, out var ended))
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

        // 4. All domain events — excluding [MlExcluded] types per ADR-0003 Decision 4
        var eventExports = events
            .Where(e => e.Data == null || !IsMlExcluded(e.Data.GetType()))
            .Select(e => new StudentEventExport(
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

    /// <summary>
    /// RDY-006 / ADR-0003 Decision 4: checks whether an event type carries the
    /// <see cref="MlExcludedAttribute"/>, which also signals export exclusion.
    /// Misconception events contain specific error patterns that are personally
    /// identifiable when combined with a student ID — they must not appear in
    /// GDPR portability exports.
    /// </summary>
    public static bool IsMlExcluded(Type eventType) =>
        eventType.GetCustomAttribute<MlExcludedAttribute>() != null;

    private static async Task<StudentProfileRef?> LoadSnapshotRefAsync(IQuerySession session, string studentId)
    {
        // mt_doc_studentprofilesnapshot is owned by the actors-side
        // StudentProfileSnapshot projection. We only pluck the fields
        // the GDPR exporter declares on StudentProfileRef so the JSON
        // shape stays bounded.
        const string sql = @"
            SELECT
                data->>'Id'             AS id,
                data->>'FullName'       AS full_name,
                data->>'DisplayName'    AS display_name,
                data->>'Bio'            AS bio,
                data->>'DateOfBirth'    AS date_of_birth,
                data->>'ParentEmail'    AS parent_email,
                data->>'AccountStatus'  AS account_status,
                data->>'SchoolId'       AS school_id
            FROM cena.mt_doc_studentprofilesnapshot
            WHERE id = @sid
            LIMIT 1";

        await using var cmd = session.Connection!.CreateCommand();
        cmd.CommandText = sql;
        var p = cmd.CreateParameter();
        p.ParameterName = "sid";
        p.Value = studentId;
        cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        DateOnly? dob = null;
        if (!reader.IsDBNull(4))
        {
            var raw = reader.GetString(4);
            if (DateOnly.TryParse(raw, out var parsed))
                dob = parsed;
        }

        return new StudentProfileRef
        {
            Id = reader.IsDBNull(0) ? "" : reader.GetString(0),
            FullName = reader.IsDBNull(1) ? null : reader.GetString(1),
            DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
            Bio = reader.IsDBNull(3) ? null : reader.GetString(3),
            DateOfBirth = dob,
            ParentEmail = reader.IsDBNull(5) ? null : reader.GetString(5),
            AccountStatus = reader.IsDBNull(6) ? null : reader.GetString(6),
            SchoolId = reader.IsDBNull(7) ? null : reader.GetString(7),
        };
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

    /// <summary>
    /// Extracts LearningSessionExport from an event object using reflection.
    /// Avoids compile-time dependency on Cena.Actors.Events.LearningSessionStarted_V1.
    /// </summary>
    private static LearningSessionExport? ExtractLearningSession(object data)
    {
        var type = data.GetType();
        var sessionId = type.GetProperty("SessionId")?.GetValue(data) as string;
        var subjects = type.GetProperty("Subjects")?.GetValue(data) as string[];
        var mode = type.GetProperty("Mode")?.GetValue(data) as string;
        var startedAt = type.GetProperty("StartedAt")?.GetValue(data);

        if (sessionId == null) return null;

        return new LearningSessionExport(
            sessionId,
            subjects ?? Array.Empty<string>(),
            mode ?? "unknown",
            startedAt is DateTimeOffset dto ? dto : DateTimeOffset.MinValue,
            null, null, null);
    }

    /// <summary>
    /// Extracts session-end data from an event object using reflection.
    /// </summary>
    private static (string SessionId, DateTimeOffset EndedAt, int QuestionsAttempted, int QuestionsCorrect)?
        ExtractSessionEnd(object data)
    {
        var type = data.GetType();
        var sessionId = type.GetProperty("SessionId")?.GetValue(data) as string;
        var endedAt = type.GetProperty("EndedAt")?.GetValue(data);
        var attempted = type.GetProperty("QuestionsAttempted")?.GetValue(data);
        var correct = type.GetProperty("QuestionsCorrect")?.GetValue(data);

        if (sessionId == null) return null;

        return (
            sessionId,
            endedAt is DateTimeOffset dto ? dto : DateTimeOffset.MinValue,
            attempted is int a ? a : 0,
            correct is int c ? c : 0);
    }
}
