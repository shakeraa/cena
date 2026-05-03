// =============================================================================
// Cena Platform — Student Advancement Aggregate State (RDY-061 Phase 2)
//
// Marten event-sourced state. Stream id: "advancement-{studentId}-{trackId}".
// Projected into StudentAdvancementDocument for query-side reads.
//
// Separate aggregate from StudentProfileSnapshot (per Dina's review lens):
// different lifecycle, different consistency boundary, different write
// frequency (advancement can change every 15m during a study session;
// profile changes on enrollment / demographic updates).
// =============================================================================

using Cena.Actors.Events;

namespace Cena.Actors.Advancement;

public enum ChapterStatus
{
    Locked = 0,
    Unlocked = 1,
    InProgress = 2,
    Mastered = 3,
    NeedsReview = 4,
}

/// <summary>
/// Event-sourced aggregate state. Re-built from the stream on every load;
/// Marten projects it into the document shape below for cheap reads.
/// </summary>
public sealed class StudentAdvancementState
{
    public string Id { get; set; } = string.Empty;              // advancement id = stream id
    public string StudentId { get; set; } = string.Empty;
    public string TrackId { get; set; } = string.Empty;
    public string SyllabusId { get; set; } = string.Empty;
    public string SyllabusVersion { get; set; } = string.Empty;
    public Dictionary<string, ChapterStatus> ChapterStatuses { get; set; } = new();
    public Dictionary<string, DateTimeOffset> ChapterLastUpdated { get; set; } = new();
    public Dictionary<string, int> ChapterQuestionsAttempted { get; set; } = new();
    public Dictionary<string, float> ChapterRetention { get; set; } = new();
    public string? CurrentChapterId { get; set; }               // highest-priority in-progress
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastAdvancedAt { get; set; }
    public int EventVersion { get; set; }

    // ── Apply methods — Marten convention, stateful replay ───────────

    public void Apply(AdvancementStarted_V1 e)
    {
        Id = e.AdvancementId;
        StudentId = e.StudentId;
        TrackId = e.TrackId;
        SyllabusId = e.SyllabusId;
        SyllabusVersion = e.SyllabusVersion;
        CreatedAt = e.StartedAt;
        LastAdvancedAt = e.StartedAt;
        ChapterStatuses = e.ChapterIds.ToDictionary(
            id => id,
            id => id == e.FirstChapterId ? ChapterStatus.Unlocked : ChapterStatus.Locked);
        ChapterLastUpdated = e.ChapterIds.ToDictionary(id => id, _ => e.StartedAt);
        ChapterQuestionsAttempted = e.ChapterIds.ToDictionary(id => id, _ => 0);
        ChapterRetention = e.ChapterIds.ToDictionary(id => id, _ => 0f);
        CurrentChapterId = e.FirstChapterId;
        EventVersion++;
    }

    public void Apply(ChapterUnlocked_V1 e)
    {
        ChapterStatuses[e.ChapterId] = ChapterStatus.Unlocked;
        ChapterLastUpdated[e.ChapterId] = e.UnlockedAt;
        LastAdvancedAt = e.UnlockedAt;
        EventVersion++;
    }

    public void Apply(ChapterStarted_V1 e)
    {
        // Only move to InProgress if not already past it
        if (!ChapterStatuses.TryGetValue(e.ChapterId, out var cur) || cur == ChapterStatus.Unlocked)
            ChapterStatuses[e.ChapterId] = ChapterStatus.InProgress;
        ChapterLastUpdated[e.ChapterId] = e.FirstAttemptAt;
        LastAdvancedAt = e.FirstAttemptAt;
        CurrentChapterId = e.ChapterId;
        EventVersion++;
    }

    public void Apply(ChapterMastered_V1 e)
    {
        ChapterStatuses[e.ChapterId] = ChapterStatus.Mastered;
        ChapterLastUpdated[e.ChapterId] = e.MasteredAt;
        ChapterQuestionsAttempted[e.ChapterId] = e.QuestionsAttempted;
        ChapterRetention[e.ChapterId] = e.MasteryScore;
        LastAdvancedAt = e.MasteredAt;
        // If the current chapter was this one, advance to the next unlocked
        if (CurrentChapterId == e.ChapterId)
            CurrentChapterId = FirstInProgressOrUnlocked();
        EventVersion++;
    }

    public void Apply(ChapterDecayDetected_V1 e)
    {
        ChapterStatuses[e.ChapterId] = ChapterStatus.NeedsReview;
        ChapterRetention[e.ChapterId] = e.CurrentRetention;
        ChapterLastUpdated[e.ChapterId] = e.DetectedAt;
        EventVersion++;
    }

    public void Apply(SpiralReviewCompleted_V1 e)
    {
        // After review we assume chapter is mastered again if retention recovered.
        ChapterStatuses[e.ChapterId] = e.RetentionAfterReview >= 0.7f
            ? ChapterStatus.Mastered
            : ChapterStatus.NeedsReview;
        ChapterRetention[e.ChapterId] = e.RetentionAfterReview;
        ChapterLastUpdated[e.ChapterId] = e.ReviewedAt;
        LastAdvancedAt = e.ReviewedAt;
        EventVersion++;
    }

    public void Apply(ChapterOverriddenByTeacher_V1 e)
    {
        if (Enum.TryParse<ChapterStatus>(e.NewStatus, ignoreCase: true, out var parsed))
            ChapterStatuses[e.ChapterId] = parsed;
        ChapterLastUpdated[e.ChapterId] = e.OverriddenAt;
        LastAdvancedAt = e.OverriddenAt;
        EventVersion++;
    }

    private string? FirstInProgressOrUnlocked()
    {
        return ChapterStatuses
            .Where(kvp => kvp.Value == ChapterStatus.InProgress || kvp.Value == ChapterStatus.Unlocked)
            .Select(kvp => kvp.Key)
            .FirstOrDefault();
    }
}
