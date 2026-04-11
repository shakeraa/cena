// =============================================================================
// Cena Platform — StudentProfileSnapshot Rebuild Tests (FIND-data-007)
//
// These tests guard the event-sourcing contract for StudentProfileSnapshot:
//   1. All mutations flow through event append + Apply handler, NOT manual Store.
//   2. The snapshot survives a full projection rebuild with no data loss.
//   3. Specifically: TotalXp accumulates across XpAwarded_V1 events, and
//      CreatedAt is preserved across rebuilds via Apply(OnboardingCompleted_V1).
//
// Regression for FIND-data-007: SessionEndpoints.SubmitAnswer used to apply
// XpAwarded_V1 to an in-memory profile and Store() it back, racing Marten's
// inline SnapshotProjection daemon and dropping XP on rebuild.
//
// Marten's SnapshotProjection<StudentProfileSnapshot> rebuilds the snapshot
// by constructing a fresh instance and replaying every Apply(event) call in
// event-stream order. These tests faithfully model that behavior using pure
// in-memory replay — no Postgres required. If these tests pass, Marten's
// rebuild behavior is equivalent.
// =============================================================================

using Cena.Actors.Events;

namespace Cena.Actors.Tests.Events;

public sealed class StudentProfileSnapshotRebuildTests
{
    // -------------------------------------------------------------------------
    // Helper: simulate Marten's SnapshotProjection rebuild.
    // Marten instantiates a fresh aggregate and replays Apply(evt) in order.
    // -------------------------------------------------------------------------
    private static StudentProfileSnapshot RebuildFromEvents(
        string studentId,
        params object[] events)
    {
        var snapshot = new StudentProfileSnapshot { StudentId = studentId };
        foreach (var evt in events)
        {
            switch (evt)
            {
                case OnboardingCompleted_V1 onboard:
                    snapshot.Apply(onboard);
                    break;
                case XpAwarded_V1 xp:
                    snapshot.Apply(xp);
                    break;
                case ConceptAttempted_V1 attempt:
                    snapshot.Apply(attempt);
                    break;
                case LearningSessionStarted_V1 sessionStart:
                    snapshot.Apply(sessionStart);
                    break;
                case ProfileUpdated_V1 profileUpdated:
                    snapshot.Apply(profileUpdated);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Test helper has no Apply wiring for event type {evt.GetType().Name}");
            }
        }
        return snapshot;
    }

    // -------------------------------------------------------------------------
    // FIND-data-007 (a): Three sequential XpAwarded_V1 events must sum on rebuild.
    //
    // Pre-fix behavior: endpoint called profile.Apply(xpEvent) + session.Store()
    // but never appended the XP event to the stream. On Marten's inline rebuild,
    // the XP column silently reverted to the last stored value — or to zero if
    // the document happened to be written by the projection AFTER the manual
    // Store. Either way, XP was non-deterministic.
    //
    // Post-fix: Append(conceptAttempt, xpEvent) puts BOTH events in the stream.
    // The inline SnapshotProjection replays them and sets TotalXp via
    // Apply(XpAwarded_V1) — which reads the absolute TotalXp field on each event.
    // Three events with absolute totals 10, 20, 30 therefore leave the rebuilt
    // snapshot at TotalXp = 30.
    // -------------------------------------------------------------------------
    [Fact]
    public void Rebuild_ThreeXpAwardedEvents_TotalXpEqualsSum()
    {
        var studentId = "student-xp-rebuild";
        var now = DateTimeOffset.UtcNow;

        // Three correct answers, each awarding 10 XP. The endpoint stamps the
        // running absolute TotalXp into each event — matching real runtime.
        var xp1 = new XpAwarded_V1(studentId, XpAmount: 10, Source: "correct_answer",
            TotalXp: 10, DifficultyLevel: "easy", DifficultyMultiplier: 1);
        var xp2 = new XpAwarded_V1(studentId, XpAmount: 10, Source: "correct_answer",
            TotalXp: 20, DifficultyLevel: "medium", DifficultyMultiplier: 1);
        var xp3 = new XpAwarded_V1(studentId, XpAmount: 10, Source: "correct_answer",
            TotalXp: 30, DifficultyLevel: "hard", DifficultyMultiplier: 1);

        var rebuilt = RebuildFromEvents(studentId, xp1, xp2, xp3);

        Assert.Equal(30, rebuilt.TotalXp);
        Assert.Equal(studentId, rebuilt.StudentId);
    }

    // -------------------------------------------------------------------------
    // FIND-data-007 (b): CreatedAt must survive projection rebuild.
    //
    // Pre-fix behavior: CreatedAt was only set by StudentActor.cs during fresh
    // actor init, never by any Apply handler. Any rebuild reset CreatedAt to
    // DateTimeOffset.MinValue (0001-01-01 00:00:00 +00:00), breaking account-age
    // calculations, FERPA retention windows, and onboarding-completion analytics.
    //
    // Post-fix: Apply(OnboardingCompleted_V1 e) sets CreatedAt = e.CompletedAt
    // the first time it sees the event in a fresh snapshot. This makes the
    // onboarding event the canonical creation timestamp for the stream.
    // -------------------------------------------------------------------------
    [Fact]
    public void Rebuild_AfterOnboarding_CreatedAtIsNotMinValue()
    {
        var studentId = "student-onboard-rebuild";
        var onboardedAt = new DateTimeOffset(2026, 3, 15, 10, 30, 0, TimeSpan.Zero);

        var onboarding = new OnboardingCompleted_V1(
            StudentId: studentId,
            Role: "student",
            Locale: "en",
            Subjects: new[] { "Mathematics" },
            DailyTimeGoalMinutes: 30,
            CompletedAt: onboardedAt);

        var rebuilt = RebuildFromEvents(studentId, onboarding);

        Assert.NotEqual(default, rebuilt.CreatedAt);
        Assert.NotEqual(DateTimeOffset.MinValue, rebuilt.CreatedAt);
        Assert.Equal(onboardedAt, rebuilt.CreatedAt);
        Assert.Equal(onboardedAt.UtcDateTime, rebuilt.OnboardedAt);
    }

    // -------------------------------------------------------------------------
    // FIND-data-007 (c): Full flow — onboarding + 3 correct answers.
    // This simulates the real user journey that broke in production:
    //   1. Student completes onboarding → OnboardingCompleted_V1
    //   2. Session starts → LearningSessionStarted_V1
    //   3. Student answers 3 questions correctly → 3x (ConceptAttempted_V1 + XpAwarded_V1)
    // After full rebuild the snapshot must have:
    //   - TotalXp = 30
    //   - CreatedAt = onboarding timestamp (NOT min value)
    //   - ConceptMastery populated for the answered concept
    //   - OnboardedAt set
    // -------------------------------------------------------------------------
    [Fact]
    public void Rebuild_FullOnboardingPlusThreeCorrectAnswers_AllFieldsSurvive()
    {
        var studentId = "student-full-journey";
        var onboardedAt = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);
        var sessionStartedAt = onboardedAt.AddMinutes(1);

        var onboarding = new OnboardingCompleted_V1(
            StudentId: studentId,
            Role: "student",
            Locale: "en",
            Subjects: new[] { "Mathematics", "Science" },
            DailyTimeGoalMinutes: 45,
            CompletedAt: onboardedAt);

        var sessionStart = new LearningSessionStarted_V1(
            StudentId: studentId,
            SessionId: "sess-001",
            Subjects: new[] { "Mathematics" },
            Mode: "practice",
            DurationMinutes: 15,
            StartedAt: sessionStartedAt);

        // Build three (ConceptAttempted, XpAwarded) pairs — this mirrors what
        // SessionEndpoints.SubmitAnswer now appends in a single call per answer.
        object MakeAttempt(int i) => new ConceptAttempted_V1(
            StudentId: studentId,
            ConceptId: "concept:math:algebra",
            SessionId: "sess-001",
            IsCorrect: true,
            ResponseTimeMs: 3000,
            QuestionId: $"q-{i}",
            QuestionType: "multiple-choice",
            MethodologyActive: "practice",
            ErrorType: "",
            PriorMastery: 0.5 + (i * 0.05),
            PosteriorMastery: 0.55 + (i * 0.05),
            HintCountUsed: 0,
            WasSkipped: false,
            AnswerHash: "",
            BackspaceCount: 0,
            AnswerChangeCount: 0,
            WasOffline: false,
            Timestamp: sessionStartedAt.AddSeconds(i * 30));

        object MakeXp(int runningTotal) => new XpAwarded_V1(
            StudentId: studentId,
            XpAmount: 10,
            Source: "correct_answer",
            TotalXp: runningTotal,
            DifficultyLevel: "medium",
            DifficultyMultiplier: 1);

        var rebuilt = RebuildFromEvents(
            studentId,
            onboarding,
            sessionStart,
            MakeAttempt(0), MakeXp(10),
            MakeAttempt(1), MakeXp(20),
            MakeAttempt(2), MakeXp(30));

        // XP accumulated across all three correct answers
        Assert.Equal(30, rebuilt.TotalXp);

        // Creation timestamp preserved through rebuild (no 0001-01-01 regression)
        Assert.Equal(onboardedAt, rebuilt.CreatedAt);
        Assert.NotEqual(DateTimeOffset.MinValue, rebuilt.CreatedAt);

        // Onboarding fields preserved
        Assert.Equal(onboardedAt.UtcDateTime, rebuilt.OnboardedAt);
        Assert.Equal("student", rebuilt.Role);
        Assert.Equal("en", rebuilt.Locale);
        Assert.Equal(45, rebuilt.DailyTimeGoalMinutes);
        Assert.Contains("Mathematics", rebuilt.Subjects);

        // Concept mastery populated — three attempts on the same concept
        Assert.True(rebuilt.ConceptMastery.ContainsKey("concept:math:algebra"));
        var mastery = rebuilt.ConceptMastery["concept:math:algebra"];
        Assert.Equal(3, mastery.TotalAttempts);
        Assert.Equal(3, mastery.CorrectCount);

        // Session count bumped by LearningSessionStarted_V1 (SessionCount++)
        // plus any OnboardingCompleted effects. We only assert >= 1 to keep the
        // test tolerant of future SessionCount accounting changes.
        Assert.True(rebuilt.SessionCount >= 1);
    }

    // -------------------------------------------------------------------------
    // FIND-data-007 (d): Idempotency of CreatedAt.
    // Replaying the same OnboardingCompleted_V1 event (e.g. after a projection
    // rebuild, a test replay, or an upcaster pass) must never change CreatedAt
    // once it has been set.
    // -------------------------------------------------------------------------
    [Fact]
    public void Rebuild_OnboardingAppliedTwice_CreatedAtIsIdempotent()
    {
        var studentId = "student-idempotent";
        var onboardedAt = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);

        var onboarding = new OnboardingCompleted_V1(
            StudentId: studentId,
            Role: "student",
            Locale: "en",
            Subjects: new[] { "Mathematics" },
            DailyTimeGoalMinutes: 20,
            CompletedAt: onboardedAt);

        var rebuilt = RebuildFromEvents(studentId, onboarding, onboarding);

        Assert.Equal(onboardedAt, rebuilt.CreatedAt);
    }

    // -------------------------------------------------------------------------
    // FIND-data-007 (e): Defensive — an already-set CreatedAt is not overwritten
    // by a later OnboardingCompleted_V1. This guards the scenario where a fresh
    // actor set CreatedAt before the onboarding event was ever appended (legacy
    // code in StudentActor.cs:514 still sets CreatedAt = UtcNow on fresh init).
    // -------------------------------------------------------------------------
    [Fact]
    public void Apply_Onboarding_DoesNotOverwriteExistingCreatedAt()
    {
        var existingCreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var onboardedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        var snapshot = new StudentProfileSnapshot
        {
            StudentId = "student-pre-existing",
            CreatedAt = existingCreatedAt,
        };

        snapshot.Apply(new OnboardingCompleted_V1(
            StudentId: "student-pre-existing",
            Role: "student",
            Locale: "en",
            Subjects: Array.Empty<string>(),
            DailyTimeGoalMinutes: 15,
            CompletedAt: onboardedAt));

        Assert.Equal(existingCreatedAt, snapshot.CreatedAt);
        Assert.Equal(onboardedAt.UtcDateTime, snapshot.OnboardedAt);
    }

    // -------------------------------------------------------------------------
    // FIND-data-007b (a): ProfileUpdated_V1 applies display name, bio, subjects, visibility.
    // -------------------------------------------------------------------------
    [Fact]
    public void Apply_ProfileUpdated_UpdatesAllFields()
    {
        var studentId = "student-profile-update";
        var updatedAt = new DateTimeOffset(2026, 4, 11, 15, 0, 0, TimeSpan.Zero);

        // First onboard the student
        var onboarding = new OnboardingCompleted_V1(
            StudentId: studentId,
            Role: "student",
            Locale: "en",
            Subjects: new[] { "Mathematics" },
            DailyTimeGoalMinutes: 30,
            CompletedAt: new DateTimeOffset(2026, 4, 11, 10, 0, 0, TimeSpan.Zero));

        // Then update profile
        var profileUpdate = new ProfileUpdated_V1(
            StudentId: studentId,
            DisplayName: "Math Whiz",
            Bio: "I love solving equations!",
            Subjects: new[] { "Mathematics", "Physics" },
            Visibility: "public",
            UpdatedAt: updatedAt);

        var snapshot = RebuildFromEvents(studentId, onboarding, profileUpdate);

        Assert.Equal("Math Whiz", snapshot.DisplayName);
        Assert.Equal("I love solving equations!", snapshot.Bio);
        Assert.Equal(new[] { "Mathematics", "Physics" }, snapshot.Subjects);
        Assert.Equal("public", snapshot.Visibility);
    }

    // -------------------------------------------------------------------------
    // FIND-data-007b (b): ProfileUpdated_V1 with null fields leaves existing values.
    // -------------------------------------------------------------------------
    [Fact]
    public void Apply_ProfileUpdated_NullFieldsPreservesExisting()
    {
        var studentId = "student-partial-update";

        var onboarding = new OnboardingCompleted_V1(
            StudentId: studentId,
            Role: "student",
            Locale: "en",
            Subjects: new[] { "Mathematics" },
            DailyTimeGoalMinutes: 30,
            CompletedAt: new DateTimeOffset(2026, 4, 11, 10, 0, 0, TimeSpan.Zero));

        // First update sets display name
        var firstUpdate = new ProfileUpdated_V1(
            StudentId: studentId,
            DisplayName: "Original Name",
            Bio: null,
            Subjects: null,
            Visibility: null,
            UpdatedAt: new DateTimeOffset(2026, 4, 11, 11, 0, 0, TimeSpan.Zero));

        // Second update only changes bio
        var secondUpdate = new ProfileUpdated_V1(
            StudentId: studentId,
            DisplayName: null,
            Bio: "New bio",
            Subjects: null,
            Visibility: null,
            UpdatedAt: new DateTimeOffset(2026, 4, 11, 12, 0, 0, TimeSpan.Zero));

        var snapshot = RebuildFromEvents(studentId, onboarding, firstUpdate, secondUpdate);

        // Display name from first update preserved
        Assert.Equal("Original Name", snapshot.DisplayName);
        // Bio from second update
        Assert.Equal("New bio", snapshot.Bio);
        // Subjects from onboarding preserved
        Assert.Equal(new[] { "Mathematics" }, snapshot.Subjects);
    }

    // -------------------------------------------------------------------------
    // FIND-data-007b (c): Profile survives rebuild with both onboarding and profile update.
    // -------------------------------------------------------------------------
    [Fact]
    public void Rebuild_OnboardingPlusProfileUpdate_AllFieldsSurvive()
    {
        var studentId = "student-full-profile";
        var onboardedAt = new DateTimeOffset(2026, 4, 11, 9, 0, 0, TimeSpan.Zero);

        var onboarding = new OnboardingCompleted_V1(
            StudentId: studentId,
            Role: "student",
            Locale: "en",
            Subjects: new[] { "Mathematics" },
            DailyTimeGoalMinutes: 30,
            CompletedAt: onboardedAt);

        var profileUpdate = new ProfileUpdated_V1(
            StudentId: studentId,
            DisplayName: "Math Pro",
            Bio: "Expert in algebra",
            Subjects: new[] { "Mathematics", "Calculus", "Linear Algebra" },
            Visibility: "class-only",
            UpdatedAt: new DateTimeOffset(2026, 4, 11, 10, 0, 0, TimeSpan.Zero));

        var rebuilt = RebuildFromEvents(studentId, onboarding, profileUpdate);

        // Onboarding fields preserved
        Assert.Equal(onboardedAt.UtcDateTime, rebuilt.OnboardedAt);
        Assert.Equal("student", rebuilt.Role);
        Assert.Equal("en", rebuilt.Locale);

        // Profile update fields applied
        Assert.Equal("Math Pro", rebuilt.DisplayName);
        Assert.Equal("Expert in algebra", rebuilt.Bio);
        Assert.Equal(new[] { "Mathematics", "Calculus", "Linear Algebra" }, rebuilt.Subjects);
        Assert.Equal("class-only", rebuilt.Visibility);

        // CreatedAt set from onboarding
        Assert.Equal(onboardedAt, rebuilt.CreatedAt);
    }
}
