// =============================================================================
// Cena Platform — Class Mastery Heatmap Projection (RDY-070 Phase 1A, F6)
//
// Per-classroom read model that powers the teacher console's topic × student
// heatmap. One document per (instituteId, classroomId); cells keyed by
// (studentAnonId, topicSlug). Phase 1A is a deterministic, pure fold — the
// endpoint rebuilds the document on demand from the event store, so
// correctness is "rebuild-from-events equals live state" by construction
// (Dina's ask).
//
// Inputs:
//   - Enrollment signals: student-in-classroom membership
//   - ConceptAttempted events (V1/V2/V3): per-concept attempts from a student
//   - IConceptTopicResolver: maps conceptId → Ministry topic slug
//
// Output cell = { Mastery (latest posterior, 0..1), SampleSize (count of
// attempts that landed in this cell), LastAttemptAt }. Cell opacity in the
// UI will encode SampleSize confidence (Dr. Yael's requirement) — the
// projection simply exposes the raw count; encoding is a rendering concern.
//
// Intentional non-goals (per task scope, Phase 1A):
//   - Not a live Marten MultiStreamProjection. The event streams are keyed
//     by studentId; joining them to classroomId requires either a roster
//     read model or cross-stream fan-out, both of which are Phase 1B.
//   - No misconception data in the document (ADR-0003: misconception data
//     is session-scoped, never on student-aggregate surfaces).
//   - No consecutive-day counters or loss-aversion signals (per shipgate).
// =============================================================================

using Cena.Actors.Events;

namespace Cena.Actors.Projections;

/// <summary>
/// Single cell in the heatmap. Mastery is the student's latest posterior
/// for any concept that rolled up to this topic; SampleSize is the count
/// of attempts the student has made at concepts in this topic.
/// </summary>
public sealed class HeatmapCell
{
    /// <summary>Latest posterior mastery (0..1).</summary>
    public double Mastery { get; set; }

    /// <summary>Number of attempts this student has logged in this topic.</summary>
    public int SampleSize { get; set; }

    /// <summary>Timestamp of the most-recent attempt that updated this cell.</summary>
    public DateTimeOffset LastAttemptAt { get; set; }
}

/// <summary>
/// Per-classroom heatmap read model. Id = <c>heatmap-{instituteId}-{classroomId}</c>.
/// </summary>
public sealed class ClassMasteryHeatmapDocument
{
    public string Id { get; set; } = "";
    public string InstituteId { get; set; } = "";
    public string ClassroomId { get; set; } = "";

    /// <summary>Topic slugs (Ministry chapters) that have at least one cell.</summary>
    public List<string> TopicSlugs { get; set; } = new();

    /// <summary>Student anon ids enrolled in this classroom (stable per student).</summary>
    public List<string> StudentAnonIds { get; set; } = new();

    /// <summary>Cells keyed by <see cref="CellKey(string, string)"/>.</summary>
    public Dictionary<string, HeatmapCell> Cells { get; set; } = new();

    /// <summary>Running count of attempt events that updated any cell.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Latest event timestamp applied to this document.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public static string CellKey(string studentAnonId, string topicSlug)
        => $"{studentAnonId}|{topicSlug}";
}

/// <summary>
/// Maps a concept (learning-objective) id to a Ministry topic slug. The
/// heatmap projection uses this to roll attempt events up from the concept
/// grain to the chapter (topic) grain.
/// </summary>
public interface IConceptTopicResolver
{
    string? TopicSlugFor(string conceptId);
}

/// <summary>
/// Pure fold projection for the per-classroom mastery heatmap. Deterministic
/// given the same (enrollment set, ordered attempts, resolver) triple —
/// meaning the document produced by <see cref="Rebuild"/> equals any
/// incremental apply sequence over the same events (modulo reordering of
/// events that target the same cell, which uses last-write-wins on
/// timestamp; see <see cref="ApplyAttempt"/>).
/// </summary>
public sealed class ClassMasteryHeatmapProjection
{
    /// <summary>Create an empty document for the given classroom.</summary>
    public ClassMasteryHeatmapDocument NewDocument(string instituteId, string classroomId)
    {
        if (string.IsNullOrWhiteSpace(instituteId))
            throw new ArgumentException("instituteId required", nameof(instituteId));
        if (string.IsNullOrWhiteSpace(classroomId))
            throw new ArgumentException("classroomId required", nameof(classroomId));

        return new ClassMasteryHeatmapDocument
        {
            Id = DocId(instituteId, classroomId),
            InstituteId = instituteId,
            ClassroomId = classroomId,
        };
    }

    public static string DocId(string instituteId, string classroomId)
        => $"heatmap-{instituteId}-{classroomId}";

    /// <summary>
    /// Mark a student as enrolled. Idempotent — enrolling the same student
    /// twice leaves the document unchanged.
    /// </summary>
    public void ApplyEnrollment(ClassMasteryHeatmapDocument doc, string studentAnonId)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId)) return;
        if (!doc.StudentAnonIds.Contains(studentAnonId, StringComparer.Ordinal))
            doc.StudentAnonIds.Add(studentAnonId);
    }

    /// <summary>
    /// Remove a student from the enrolled list. Preserves their existing
    /// cells (historical mastery is still meaningful for end-of-term
    /// review); the endpoint can choose whether to surface them.
    /// </summary>
    public void ApplyWithdrawal(ClassMasteryHeatmapDocument doc, string studentAnonId)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId)) return;
        doc.StudentAnonIds.RemoveAll(id => string.Equals(id, studentAnonId, StringComparison.Ordinal));
    }

    /// <summary>
    /// Roll an attempt into the heatmap. No-op if the concept does not map
    /// to any authored Ministry topic or the student is not enrolled.
    /// Last-write-wins on <c>LastAttemptAt</c>; SampleSize increments on
    /// every relevant attempt, so it remains the authoritative confidence
    /// signal regardless of event order.
    /// </summary>
    public void ApplyAttempt(
        ClassMasteryHeatmapDocument doc,
        string studentAnonId,
        string conceptId,
        double posteriorMastery,
        DateTimeOffset timestamp,
        IConceptTopicResolver resolver)
    {
        if (resolver is null) throw new ArgumentNullException(nameof(resolver));
        if (string.IsNullOrWhiteSpace(studentAnonId)) return;
        if (string.IsNullOrWhiteSpace(conceptId)) return;
        if (!doc.StudentAnonIds.Contains(studentAnonId, StringComparer.Ordinal)) return;

        var topicSlug = resolver.TopicSlugFor(conceptId);
        if (string.IsNullOrWhiteSpace(topicSlug)) return;

        var clamped = Math.Clamp(posteriorMastery, 0.0, 1.0);
        var key = ClassMasteryHeatmapDocument.CellKey(studentAnonId, topicSlug);

        if (!doc.Cells.TryGetValue(key, out var cell))
        {
            cell = new HeatmapCell { Mastery = clamped, SampleSize = 1, LastAttemptAt = timestamp };
            doc.Cells[key] = cell;
        }
        else
        {
            cell.SampleSize++;
            if (timestamp >= cell.LastAttemptAt)
            {
                cell.Mastery = clamped;
                cell.LastAttemptAt = timestamp;
            }
        }

        if (!doc.TopicSlugs.Contains(topicSlug, StringComparer.Ordinal))
            doc.TopicSlugs.Add(topicSlug);

        doc.AttemptCount++;
        if (timestamp > doc.UpdatedAt) doc.UpdatedAt = timestamp;
    }

    /// <summary>
    /// Typed overloads over the canonical V1/V2/V3 attempt events. Each
    /// delegates to <see cref="ApplyAttempt"/> with the event's own fields.
    /// </summary>
    public void Apply(ClassMasteryHeatmapDocument doc, ConceptAttempted_V1 e, IConceptTopicResolver resolver)
        => ApplyAttempt(doc, e.StudentId, e.ConceptId, e.PosteriorMastery, e.Timestamp, resolver);

    public void Apply(ClassMasteryHeatmapDocument doc, ConceptAttempted_V2 e, IConceptTopicResolver resolver)
        => ApplyAttempt(doc, e.StudentId, e.ConceptId, e.PosteriorMastery, e.Timestamp, resolver);

    public void Apply(ClassMasteryHeatmapDocument doc, ConceptAttempted_V3 e, IConceptTopicResolver resolver)
        => ApplyAttempt(doc, e.StudentId, e.ConceptId, e.PosteriorMastery, e.Timestamp, resolver);

    /// <summary>
    /// Build the heatmap document from scratch, given the enrolled roster
    /// and a chronologically-ordered stream of attempt events. This is the
    /// function the endpoint calls and the function the rebuild tests
    /// exercise.
    /// </summary>
    public ClassMasteryHeatmapDocument Rebuild(
        string instituteId,
        string classroomId,
        IEnumerable<string> enrolledStudentAnonIds,
        IEnumerable<AttemptSample> attempts,
        IConceptTopicResolver resolver)
    {
        if (enrolledStudentAnonIds is null) throw new ArgumentNullException(nameof(enrolledStudentAnonIds));
        if (attempts is null) throw new ArgumentNullException(nameof(attempts));

        var doc = NewDocument(instituteId, classroomId);
        foreach (var studentId in enrolledStudentAnonIds)
            ApplyEnrollment(doc, studentId);

        // Attempt order matters only for which posterior "wins" the cell;
        // sort by timestamp ascending so the most-recent attempt is the
        // last one applied, regardless of how the caller passed the stream.
        foreach (var a in attempts.OrderBy(a => a.Timestamp))
            ApplyAttempt(doc, a.StudentAnonId, a.ConceptId, a.PosteriorMastery, a.Timestamp, resolver);

        return doc;
    }
}

/// <summary>
/// Minimal attempt record consumed by <see cref="ClassMasteryHeatmapProjection.Rebuild"/>.
/// Decouples the projection from the specific event schema so the unit
/// tests can fabricate samples without materialising full
/// <see cref="ConceptAttempted_V1"/> payloads.
/// </summary>
public readonly record struct AttemptSample(
    string StudentAnonId,
    string ConceptId,
    double PosteriorMastery,
    DateTimeOffset Timestamp);
