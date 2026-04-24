// =============================================================================
// Cena Platform — Student Advancement Service (RDY-061 Phase 2)
//
// Owns the write side of the advancement aggregate. Responsibilities:
//   1. Initialise a student's advancement on enrollment
//   2. React to ConceptMastered signals → cascade chapter transitions
//   3. Check retention decay → emit ChapterDecayDetected
//   4. Process teacher overrides
//   5. Query-side reads (pass-through to Marten's aggregate load)
//
// Thread-safety: Marten stream writes are optimistically concurrent
// (EventVersion conflict → retry). Callers that need to batch should
// serialize per advancement id.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Events;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Advancement;

public interface IStudentAdvancementService
{
    /// <summary>
    /// Ensure an advancement aggregate exists for (studentId, trackId).
    /// Idempotent — returns the existing state if already started.
    /// Loads the syllabus to snapshot chapter ids in the initial event.
    /// </summary>
    Task<StudentAdvancementState> EnsureStartedAsync(
        string studentId, string trackId, CancellationToken ct = default);

    /// <summary>
    /// Apply a concept-mastery signal. Examines the syllabus, finds the
    /// chapter this concept lives in, and cascades:
    ///   - first attempt in chapter → ChapterStarted_V1
    ///   - all concepts in chapter mastered → ChapterMastered_V1
    ///   - all prereqs of a locked chapter now Mastered → ChapterUnlocked_V1
    /// Returns the count of events emitted (0 if no-op).
    /// </summary>
    Task<int> ApplyConceptMasteryAsync(
        string studentId, string trackId, string conceptId,
        DateTimeOffset at, CancellationToken ct = default);

    /// <summary>Query-side read.</summary>
    Task<StudentAdvancementState?> GetAsync(
        string studentId, string trackId, CancellationToken ct = default);

    /// <summary>Teacher / admin override. Must be audited upstream.</summary>
    Task OverrideChapterStatusAsync(
        string studentId, string trackId, string chapterId,
        ChapterStatus newStatus, string overriddenBy, string rationale,
        CancellationToken ct = default);

    /// <summary>Retention decay check — called by a scheduled job.</summary>
    Task<int> CheckDecayAsync(
        string studentId, string trackId,
        Func<string, float> currentRetentionForChapter,
        float decayThreshold = 0.5f, CancellationToken ct = default);
}

public sealed class StudentAdvancementService : IStudentAdvancementService
{
    private readonly IDocumentStore _store;
    private readonly ILogger<StudentAdvancementService> _logger;

    public StudentAdvancementService(IDocumentStore store, ILogger<StudentAdvancementService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public static string AdvancementIdFor(string studentId, string trackId)
        => $"advancement-{studentId}-{trackId}";

    public async Task<StudentAdvancementState> EnsureStartedAsync(
        string studentId, string trackId, CancellationToken ct = default)
    {
        var advId = AdvancementIdFor(studentId, trackId);
        await using var session = _store.LightweightSession();

        var existing = await session.Events.AggregateStreamAsync<StudentAdvancementState>(advId, token: ct);
        if (existing is not null) return existing;

        // Fetch the syllabus for this track
        var syllabus = await session.Query<SyllabusDocument>()
            .Where(s => s.TrackId == trackId)
            .FirstOrDefaultAsync(ct);
        if (syllabus is null)
            throw new InvalidOperationException(
                $"Cannot start advancement: no syllabus ingested for track {trackId}. " +
                $"Run: Cena.Tools.DbAdmin syllabus-ingest --manifest config/syllabi/<track>.yaml");

        if (syllabus.ChapterIds.Count == 0)
            throw new InvalidOperationException(
                $"Syllabus {syllabus.Id} has no chapters. Manifest is empty or ingest failed.");

        var firstChapterId = syllabus.ChapterIds[0];
        var startedEvent = new AdvancementStarted_V1(
            AdvancementId: advId,
            StudentId: studentId,
            TrackId: trackId,
            SyllabusId: syllabus.Id,
            SyllabusVersion: syllabus.Version,
            ChapterIds: syllabus.ChapterIds.ToArray(),
            FirstChapterId: firstChapterId,
            StartedAt: DateTimeOffset.UtcNow);

        session.Events.StartStream<StudentAdvancementState>(advId, startedEvent);
        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[ADVANCEMENT_STARTED] id={Id} track={TrackId} syllabus={SyllabusId} chapters={Count}",
            advId, trackId, syllabus.Id, syllabus.ChapterIds.Count);

        // Re-aggregate so the caller gets the applied state
        var fresh = await session.Events.AggregateStreamAsync<StudentAdvancementState>(advId, token: ct);
        return fresh ?? throw new InvalidOperationException("advancement stream write did not land");
    }

    public async Task<int> ApplyConceptMasteryAsync(
        string studentId, string trackId, string conceptId,
        DateTimeOffset at, CancellationToken ct = default)
    {
        var advId = AdvancementIdFor(studentId, trackId);
        await using var session = _store.LightweightSession();

        var state = await session.Events.AggregateStreamAsync<StudentAdvancementState>(advId, token: ct);
        if (state is null)
        {
            // Auto-start if concept mastery arrives before enrollment flow seeded advancement.
            await EnsureStartedAsync(studentId, trackId, ct);
            state = await session.Events.AggregateStreamAsync<StudentAdvancementState>(advId, token: ct)
                ?? throw new InvalidOperationException("advancement failed to start");
        }

        // Find the chapter owning this concept's learning objective.
        // We resolve via: concept -> LearningObjective -> Chapter.LearningObjectiveIds
        var chapters = await session.Query<ChapterDocument>()
            .Where(c => c.SyllabusId == state.SyllabusId)
            .ToListAsync(ct);

        var objectiveForConcept = await session.Query<LearningObjectiveDocument>()
            .Where(lo => lo.ConceptIds.Contains(conceptId))
            .FirstOrDefaultAsync(ct);
        if (objectiveForConcept is null)
        {
            _logger.LogDebug(
                "[ADVANCEMENT_SKIP] concept {Concept} has no owning LO — skipping chapter cascade",
                conceptId);
            return 0;
        }

        var chapter = chapters.FirstOrDefault(c => c.LearningObjectiveIds.Contains(objectiveForConcept.Id));
        if (chapter is null)
        {
            _logger.LogDebug(
                "[ADVANCEMENT_SKIP] LO {Lo} is not in any chapter on syllabus {Syllabus}",
                objectiveForConcept.Id, state.SyllabusId);
            return 0;
        }

        var eventsEmitted = 0;

        // 1. ChapterStarted if first attempt in this chapter
        if (state.ChapterStatuses.TryGetValue(chapter.Id, out var curStatus)
            && curStatus == ChapterStatus.Unlocked)
        {
            session.Events.Append(advId, new ChapterStarted_V1(advId, chapter.Id, at));
            eventsEmitted++;
        }

        // 2. ChapterMastered if all concepts across all LOs in this chapter are mastered.
        //    We need the student's concept mastery to evaluate this — fetch profile.
        //    ConceptMastery keys are TENANCY-P2a scoped: "{enrollmentId}:{conceptId}"
        //    so we match on suffix. IsMastered on the ConceptMasteryState is
        //    authoritative rather than comparing PKnown to a threshold here.
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId, ct);
        if (profile is not null)
        {
            var chapterLos = await session.Query<LearningObjectiveDocument>()
                .Where(lo => chapter.LearningObjectiveIds.Contains(lo.Id))
                .ToListAsync(ct);
            var allConceptsInChapter = chapterLos.SelectMany(lo => lo.ConceptIds ?? new())
                .Distinct().ToHashSet();

            var masteredConcepts = new HashSet<string>();
            float masterySum = 0f;
            int masteryCount = 0;
            foreach (var kvp in profile.ConceptMastery ?? new())
            {
                // Key is either plain "conceptId" or "enrollmentId:conceptId"
                var colon = kvp.Key.IndexOf(':');
                var conceptKey = colon >= 0 ? kvp.Key[(colon + 1)..] : kvp.Key;
                if (!allConceptsInChapter.Contains(conceptKey)) continue;
                if (kvp.Value.IsMastered) masteredConcepts.Add(conceptKey);
                masterySum += (float)kvp.Value.PKnown;
                masteryCount++;
            }

            var chapterComplete = allConceptsInChapter.Count > 0
                && allConceptsInChapter.All(c => masteredConcepts.Contains(c));

            if (chapterComplete && state.ChapterStatuses[chapter.Id] != ChapterStatus.Mastered)
            {
                var avgMastery = masteryCount > 0 ? masterySum / masteryCount : 0f;
                session.Events.Append(advId, new ChapterMastered_V1(
                    advId, chapter.Id, avgMastery,
                    QuestionsAttempted: state.ChapterQuestionsAttempted.GetValueOrDefault(chapter.Id, 0),
                    MasteredAt: at));
                eventsEmitted++;

                // 3. Cascade: unlock any Locked chapters whose prereqs are now Mastered
                var willBeMasteredAfter = state.ChapterStatuses
                    .Where(kvp => kvp.Value == ChapterStatus.Mastered)
                    .Select(kvp => kvp.Key)
                    .Append(chapter.Id)
                    .ToHashSet();

                foreach (var maybeUnlock in chapters)
                {
                    if (state.ChapterStatuses.GetValueOrDefault(maybeUnlock.Id) == ChapterStatus.Locked
                        && maybeUnlock.PrerequisiteChapterIds.All(p => willBeMasteredAfter.Contains(p)))
                    {
                        session.Events.Append(advId, new ChapterUnlocked_V1(
                            advId, maybeUnlock.Id, at, Reason: "prereqs_mastered"));
                        eventsEmitted++;
                    }
                }
            }
        }

        if (eventsEmitted > 0)
        {
            await session.SaveChangesAsync(ct);
            _logger.LogInformation(
                "[ADVANCEMENT_APPLIED] student={Student} track={Track} concept={Concept} events={Count}",
                studentId, trackId, conceptId, eventsEmitted);
        }
        return eventsEmitted;
    }

    public async Task<StudentAdvancementState?> GetAsync(
        string studentId, string trackId, CancellationToken ct = default)
    {
        var advId = AdvancementIdFor(studentId, trackId);
        await using var session = _store.QuerySession();
        return await session.Events.AggregateStreamAsync<StudentAdvancementState>(advId, token: ct);
    }

    public async Task OverrideChapterStatusAsync(
        string studentId, string trackId, string chapterId,
        ChapterStatus newStatus, string overriddenBy, string rationale,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rationale) || rationale.Trim().Length < 10)
            throw new ArgumentException("Override rationale must be at least 10 characters (audit requirement).");

        var advId = AdvancementIdFor(studentId, trackId);
        await using var session = _store.LightweightSession();

        var state = await session.Events.AggregateStreamAsync<StudentAdvancementState>(advId, token: ct)
            ?? throw new InvalidOperationException(
                $"Cannot override: advancement {advId} does not exist — start it first.");

        if (!state.ChapterStatuses.ContainsKey(chapterId))
            throw new InvalidOperationException(
                $"Chapter {chapterId} is not part of this student's advancement.");

        session.Events.Append(advId, new ChapterOverriddenByTeacher_V1(
            advId, chapterId, newStatus.ToString(),
            overriddenBy, rationale.Trim(), DateTimeOffset.UtcNow));

        await session.SaveChangesAsync(ct);

        _logger.LogWarning(
            "[ADVANCEMENT_OVERRIDE] adv={Adv} chapter={Chapter} newStatus={Status} by={By} rationale={Rationale}",
            advId, chapterId, newStatus, overriddenBy, rationale);
    }

    public async Task<int> CheckDecayAsync(
        string studentId, string trackId,
        Func<string, float> currentRetentionForChapter,
        float decayThreshold = 0.5f, CancellationToken ct = default)
    {
        var advId = AdvancementIdFor(studentId, trackId);
        await using var session = _store.LightweightSession();

        var state = await session.Events.AggregateStreamAsync<StudentAdvancementState>(advId, token: ct);
        if (state is null) return 0;

        var emitted = 0;
        foreach (var kvp in state.ChapterStatuses.ToList())
        {
            if (kvp.Value != ChapterStatus.Mastered) continue;
            var currentRetention = currentRetentionForChapter(kvp.Key);
            if (currentRetention < decayThreshold)
            {
                session.Events.Append(advId, new ChapterDecayDetected_V1(
                    advId, kvp.Key, currentRetention, DateTimeOffset.UtcNow));
                emitted++;
            }
        }
        if (emitted > 0) await session.SaveChangesAsync(ct);
        return emitted;
    }
}
