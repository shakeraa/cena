// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Marten Event Store Configuration & Projections
// Layer: Data | Runtime: .NET 9 | DB: PostgreSQL 16 + Marten v7.x
// ═══════════════════════════════════════════════════════════════════════

using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Events.Aggregation;
using Weasel.Core;

namespace Cena.Data.EventStore;

// ─────────────────────────────────────────────────────────────────────
// 1. STORE CONFIGURATION
// ─────────────────────────────────────────────────────────────────────

public static class MartenConfiguration
{
    public static void ConfigureCenaEventStore(this StoreOptions opts, string connectionString)
    {
        opts.Connection(connectionString);
        opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
        opts.DatabaseSchemaName = "cena";

        // ── Event Store Settings ──
        opts.Events.StreamIdentity = StreamIdentity.AsString;    // Use student UUID as stream key
        opts.Events.MetadataConfig.EnableAll();                   // Full metadata on all events
        opts.Events.TenancyStyle = TenancyStyle.Single;          // Single-tenant (school context handled in app)

        // ── Serialization: System.Text.Json (Marten 7.x default) ──
        opts.UseSystemTextJsonForSerialization(o =>
        {
            o.EnumStorage = EnumStorage.AsString;
            o.Casing = Casing.CamelCase;
        });

        // ── Snapshot Strategy: every 100 events per student ──
        opts.Projections.Snapshot<StudentProfileSnapshot>(SnapshotLifecycle.Inline, 100);

        // ── Register All Event Types (append-only, versioned) ──
        RegisterLearnerEvents(opts);
        RegisterPedagogyEvents(opts);
        RegisterEngagementEvents(opts);
        RegisterOutreachEvents(opts);

        // ── Register Upcasters (V1→V2 migrations) ──
        RegisterUpcasters(opts);

        // ── CQRS Inline Projections (low-latency reads) ──
        opts.Projections.Add<StudentMasteryProjection>(ProjectionLifecycle.Inline);
        opts.Projections.Add<ClassOverviewProjection>(ProjectionLifecycle.Inline);

        // ── CQRS Async Projections (analytics, dashboards) ──
        opts.Projections.Add<TeacherDashboardProjection>(ProjectionLifecycle.Async);
        opts.Projections.Add<ParentProgressProjection>(ProjectionLifecycle.Async);
        opts.Projections.Add<MethodologyEffectivenessProjection>(ProjectionLifecycle.Async);
        opts.Projections.Add<RetentionCohortProjection>(ProjectionLifecycle.Async);
    }

    private static void RegisterLearnerEvents(StoreOptions opts)
    {
        opts.Events.AddEventType<ConceptAttempted_V1>();
        opts.Events.AddEventType<ConceptMastered_V1>();
        opts.Events.AddEventType<MasteryDecayed_V1>();
        opts.Events.AddEventType<MethodologySwitched_V1>();
        opts.Events.AddEventType<StagnationDetected_V1>();
        opts.Events.AddEventType<AnnotationAdded_V1>();
        opts.Events.AddEventType<CognitiveLoadCooldownComplete_V1>();
    }

    private static void RegisterPedagogyEvents(StoreOptions opts)
    {
        opts.Events.AddEventType<SessionStarted_V1>();
        opts.Events.AddEventType<SessionEnded_V1>();
        opts.Events.AddEventType<ExercisePresented_V1>();
        opts.Events.AddEventType<HintRequested_V1>();
        opts.Events.AddEventType<QuestionSkipped_V1>();
    }

    private static void RegisterEngagementEvents(StoreOptions opts)
    {
        opts.Events.AddEventType<XpAwarded_V1>();
        opts.Events.AddEventType<StreakUpdated_V1>();
        opts.Events.AddEventType<BadgeEarned_V1>();
        opts.Events.AddEventType<StreakExpiring_V1>();
        opts.Events.AddEventType<ReviewDue_V1>();
    }

    private static void RegisterOutreachEvents(StoreOptions opts)
    {
        opts.Events.AddEventType<OutreachMessageSent_V1>();
        opts.Events.AddEventType<OutreachMessageDelivered_V1>();
        opts.Events.AddEventType<OutreachResponseReceived_V1>();
    }

    private static void RegisterUpcasters(StoreOptions opts)
    {
        // Example: when ConceptAttempted_V2 is introduced
        // opts.Events.Upcast<ConceptAttempted_V1, ConceptAttempted_V2>(
        //     v1 => new ConceptAttempted_V2 { /* map fields, add defaults for new fields */ }
        // );
    }
}

// ─────────────────────────────────────────────────────────────────────
// 2. DOMAIN EVENTS (C# records — immutable by design)
// ─────────────────────────────────────────────────────────────────────

// ── Learner Context ──

public record ConceptAttempted_V1(
    string StudentId,
    string ConceptId,
    string SessionId,
    bool IsCorrect,
    int ResponseTimeMs,
    string QuestionId,
    string QuestionType,          // "multiple_choice" | "numeric" | "expression" | "free_text" | ...
    string MethodologyActive,     // "socratic" | "spaced_repetition" | "feynman" | ...
    string ErrorType,             // "procedural" | "conceptual" | "motivational" | "none"
    double PriorMastery,
    double PosteriorMastery,
    int HintCountUsed,
    bool WasSkipped,
    string AnswerHash,
    int BackspaceCount,
    int AnswerChangeCount,
    bool WasOffline,
    DateTimeOffset Timestamp          // FIXED: explicit timestamp for deterministic Apply replay
);

public record ConceptMastered_V1(
    string StudentId,
    string ConceptId,
    string SessionId,
    double MasteryLevel,
    int TotalAttempts,
    int TotalSessions,
    string MethodologyAtMastery,
    double InitialHalfLifeHours,
    DateTimeOffset Timestamp          // FIXED: explicit timestamp for deterministic Apply replay
);

public record MasteryDecayed_V1(
    string StudentId,
    string ConceptId,
    double PredictedRecall,
    double HalfLifeHours,
    double HoursSinceLastReview
);

public record MethodologySwitched_V1(
    string StudentId,
    string ConceptId,
    string PreviousMethodology,
    string NewMethodology,
    string Trigger,               // "stagnation_detected" | "student_requested" | "mcm_recommendation"
    double StagnationScore,
    string DominantErrorType,
    double McmConfidence
);

public record StagnationDetected_V1(
    string StudentId,
    string ConceptId,
    double CompositeScore,
    double AccuracyPlateau,
    double ResponseTimeDrift,
    double SessionAbandonment,
    double ErrorRepetition,
    double AnnotationSentiment,
    int ConsecutiveStagnantSessions
);

public record AnnotationAdded_V1(
    string StudentId,
    string ConceptId,
    string AnnotationId,
    string ContentHash,           // hash, not plaintext
    double SentimentScore,        // 0.0-1.0 from NLP
    string AnnotationType         // "note" | "question" | "insight" | "confusion"
);

public record CognitiveLoadCooldownComplete_V1(
    string StudentId,
    string SessionId,
    double FatigueScoreAtEnd,
    int MinutesCooldown,
    int QuestionsCompleted
);

// ── Pedagogy Context ──

public record SessionStarted_V1(
    string StudentId,
    string SessionId,
    string DeviceType,
    string AppVersion,
    string Methodology,
    string? ExperimentCohort,
    bool IsOffline,
    DateTimeOffset ClientTimestamp
);

public record SessionEnded_V1(
    string StudentId,
    string SessionId,
    string EndReason,             // "completed" | "fatigue" | "abandoned" | "timeout" | "app_backgrounded"
    int DurationMinutes,
    int QuestionsAttempted,
    int QuestionsCorrect,
    double AvgResponseTimeMs,
    double FatigueScoreAtEnd
);

public record ExercisePresented_V1(
    string StudentId,
    string SessionId,
    string ConceptId,
    string QuestionId,
    string QuestionType,
    string DifficultyLevel,       // "recall" | "comprehension" | "application" | "analysis"
    string Methodology
);

public record HintRequested_V1(
    string StudentId,
    string SessionId,
    string ConceptId,
    string QuestionId,
    int HintLevel                 // 1=nudge, 2=scaffolded, 3=near-answer
);

public record QuestionSkipped_V1(
    string StudentId,
    string SessionId,
    string ConceptId,
    string QuestionId,
    int TimeSpentBeforeSkipMs
);

// ── Engagement Context ──

public record XpAwarded_V1(
    string StudentId,
    int XpAmount,
    string Source,                // "exercise_correct" | "mastery" | "streak_bonus" | "daily_goal"
    int TotalXp,
    string DifficultyLevel,       // FIXED: "recall" | "comprehension" | "application" | "analysis" — XP scaled by difficulty
    int DifficultyMultiplier      // FIXED: 1x recall, 2x comprehension, 3x application, 4x analysis — rewards mastery depth not volume
);

public record StreakUpdated_V1(
    string StudentId,
    int CurrentStreak,
    int LongestStreak,
    DateTimeOffset LastActivityDate
);

public record BadgeEarned_V1(
    string StudentId,
    string BadgeId,
    string BadgeName,
    string BadgeCategory          // "mastery" | "streak" | "exploration" | "methodology"
);

public record StreakExpiring_V1(
    string StudentId,
    int CurrentStreak,
    DateTimeOffset ExpiresAt,
    int HoursUntilExpiry
);

public record ReviewDue_V1(
    string StudentId,
    string ConceptId,
    double PredictedRecall,
    double HalfLifeHours,
    string Priority               // "urgent" | "standard" | "low"
);

// ── Outreach Context ──

public record OutreachMessageSent_V1(
    string StudentId,
    string MessageId,
    string Channel,               // "whatsapp" | "telegram" | "push" | "voice"
    string TriggerType,           // "StreakExpiring" | "ReviewDue" | "StagnationDetected" | ...
    string ContentHash
);

public record OutreachMessageDelivered_V1(
    string StudentId,
    string MessageId,
    string Channel,
    DateTimeOffset DeliveredAt
);

public record OutreachResponseReceived_V1(
    string StudentId,
    string MessageId,
    string ResponseType,          // "quiz_answer" | "dismissed" | "clicked" | "replied"
    string? ResponseContentHash
);

// ─────────────────────────────────────────────────────────────────────
// 3. SNAPSHOT (aggregate state rebuilt from events)
// ─────────────────────────────────────────────────────────────────────

public class StudentProfileSnapshot
{
    public string StudentId { get; set; } = "";
    public Dictionary<string, ConceptMasteryState> ConceptMastery { get; set; } = new();
    public Dictionary<string, string> ActiveMethodologyMap { get; set; } = new();        // conceptId → methodology
    public Dictionary<string, List<string>> MethodAttemptHistory { get; set; } = new();  // conceptCluster → [methods tried]
    public Dictionary<string, double> HalfLifeMap { get; set; } = new();                // conceptId → half-life hours
    public int TotalXp { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public DateTimeOffset LastActivityDate { get; set; }
    public string? ExperimentCohort { get; set; }
    public double BaselineAccuracy { get; set; }     // trailing 20-question median
    public double BaselineResponseTimeMs { get; set; } // trailing 20-question median
    public int SessionCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // ── Apply methods (event → state mutation) ──

    public void Apply(ConceptAttempted_V1 e)
    {
        if (!ConceptMastery.ContainsKey(e.ConceptId))
            ConceptMastery[e.ConceptId] = new ConceptMasteryState();

        var state = ConceptMastery[e.ConceptId];
        state.PKnown = e.PosteriorMastery;
        state.TotalAttempts++;
        state.LastAttemptedAt = e.Timestamp; // FIXED: use event timestamp, not wall clock (deterministic replay)
        state.LastMethodology = e.MethodologyActive;
    }

    public void Apply(ConceptMastered_V1 e)
    {
        if (!ConceptMastery.ContainsKey(e.ConceptId))
            ConceptMastery[e.ConceptId] = new ConceptMasteryState();

        ConceptMastery[e.ConceptId].IsMastered = true;
        ConceptMastery[e.ConceptId].MasteredAt = e.Timestamp; // FIXED: use event timestamp, not wall clock
        HalfLifeMap[e.ConceptId] = e.InitialHalfLifeHours;
    }

    public void Apply(MasteryDecayed_V1 e)
    {
        if (ConceptMastery.ContainsKey(e.ConceptId))
        {
            ConceptMastery[e.ConceptId].IsMastered = false;
            ConceptMastery[e.ConceptId].PKnown = e.PredictedRecall;
        }
    }

    public void Apply(MethodologySwitched_V1 e)
    {
        ActiveMethodologyMap[e.ConceptId] = e.NewMethodology;

        var clusterKey = e.ConceptId; // TODO: map to concept cluster
        if (!MethodAttemptHistory.ContainsKey(clusterKey))
            MethodAttemptHistory[clusterKey] = new();
        MethodAttemptHistory[clusterKey].Add(e.NewMethodology);
    }

    public void Apply(XpAwarded_V1 e) => TotalXp = e.TotalXp;

    public void Apply(StreakUpdated_V1 e)
    {
        CurrentStreak = e.CurrentStreak;
        LongestStreak = e.LongestStreak;
        LastActivityDate = e.LastActivityDate;
    }

    public void Apply(SessionStarted_V1 e)
    {
        SessionCount++;
        ExperimentCohort ??= e.ExperimentCohort;
    }
}

public class ConceptMasteryState
{
    public double PKnown { get; set; }
    public bool IsMastered { get; set; }
    public int TotalAttempts { get; set; }
    public DateTimeOffset? LastAttemptedAt { get; set; }
    public DateTimeOffset? MasteredAt { get; set; }
    public string? LastMethodology { get; set; }
}

// ─────────────────────────────────────────────────────────────────────
// 4. CQRS READ MODEL PROJECTIONS
// ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Per-student mastery overview — used by the knowledge graph UI.
/// Inline projection: always up-to-date, zero latency.
/// </summary>
public class StudentMasteryView
{
    public string Id { get; set; } = "";              // StudentId
    public Dictionary<string, double> MasteryMap { get; set; } = new(); // conceptId → P(known)
    public int ConceptsMastered { get; set; }
    public int ConceptsInProgress { get; set; }
    public int TotalXp { get; set; }
    public int CurrentStreak { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
}

public class StudentMasteryProjection : SingleStreamProjection<StudentMasteryView>
{
    public void Apply(ConceptAttempted_V1 e, StudentMasteryView view)
    {
        view.MasteryMap[e.ConceptId] = e.PosteriorMastery;
        view.LastUpdated = e.Timestamp /* FIXED: deterministic — use event timestamp, never wall clock */;
        view.ConceptsInProgress = view.MasteryMap.Count(kv => kv.Value > 0.3 && kv.Value < 0.85);
    }

    public void Apply(ConceptMastered_V1 e, StudentMasteryView view)
    {
        view.MasteryMap[e.ConceptId] = e.MasteryLevel;
        view.ConceptsMastered = view.MasteryMap.Count(kv => kv.Value >= 0.85);
        view.LastUpdated = e.Timestamp /* FIXED: deterministic — use event timestamp, never wall clock */;
    }

    public void Apply(XpAwarded_V1 e, StudentMasteryView view) => view.TotalXp = e.TotalXp;
    public void Apply(StreakUpdated_V1 e, StudentMasteryView view) => view.CurrentStreak = e.CurrentStreak;
}

/// <summary>
/// Class-level overview for teacher dashboard.
/// Inline projection scoped by school/class (filtered at query time).
/// </summary>
public class ClassOverviewView
{
    public string Id { get; set; } = "";              // "{classId}"
    public Dictionary<string, StudentSummary> Students { get; set; } = new();
    public DateTimeOffset LastUpdated { get; set; }
}

public class StudentSummary
{
    public string StudentId { get; set; } = "";
    public int ConceptsMastered { get; set; }
    public int CurrentStreak { get; set; }
    public int TotalXp { get; set; }
    public DateTimeOffset LastActive { get; set; }
    public string RiskLevel { get; set; } = "green"; // "green" | "yellow" | "red"
}

// Placeholder projection classes — implementations follow the same Apply() pattern
public class ClassOverviewProjection : SingleStreamProjection<ClassOverviewView>
{
    // Apply events to update class-level aggregates
}

public class TeacherDashboardProjection : MultiStreamProjection<TeacherDashboardView, string>
{
    // Async: rebuilds from all student events in a class
}

public class ParentProgressProjection : SingleStreamProjection<ParentProgressView>
{
    // Async: parent sees child's weekly progress
}

public class MethodologyEffectivenessProjection : MultiStreamProjection<MethodologyEffectivenessView, string>
{
    // Async: which methodology works for which student profile
}

public class RetentionCohortProjection : MultiStreamProjection<RetentionCohortView, string>
{
    // Async: D1/D7/D30 retention by cohort
}

// Read model stubs (implementations follow same pattern)
public class TeacherDashboardView { public string Id { get; set; } = ""; }
public class ParentProgressView { public string Id { get; set; } = ""; }
public class MethodologyEffectivenessView { public string Id { get; set; } = ""; }
public class RetentionCohortView { public string Id { get; set; } = ""; }
