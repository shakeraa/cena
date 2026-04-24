// =============================================================================
// Cena Platform — Onboarding Self-Assessment Document (RDY-057)
//
// Captures a student's affective / self-concept state at onboarding:
// subject-level confidence (1-5 Likert), self-identified strengths +
// friction points, topic-level feelings, optional free-text.
//
// Privacy / retention:
//   - [MlExcluded] — never flows into ML training corpora (ADR-0003 Decision 4,
//     FTC v. Edmodo alignment). Self-reported psychological fields share
//     the same concern class as misconception data.
//   - Default retention: 90 days (`ExpiresAt = CapturedAt + 90d`) unless the
//     student opts in via `OptInPersistent = true`.
//   - Never re-purposed as a profile attribute visible to other students or
//     classmates. Teacher roll-ups are aggregate-only (classroom-level).
//
// Consumers:
//   - `LearningSessionActor` hint: if `TopicFeelings[currentTopic] = 'anxious'`,
//     opener prefers a faded worked example over a cold direct problem.
//   - Admin teacher dashboard: aggregate counts per classroom, no per-student
//     rows.
//   - BKT prior: subject confidence nudges `P_Initial` as a tiebreaker only
//     (students misjudge themselves; self-signal is weak vs observed data).
// =============================================================================

using Cena.Infrastructure.Compliance;

namespace Cena.Infrastructure.Documents;

public enum TopicFeeling
{
    Solid = 0,      // 😊 — student feels comfortable
    Unsure = 1,     // 🤔 — uncertain but not panicked
    Anxious = 2,    // 😰 — active apprehension
    New = 3,        // ❌ — haven't seen it
}

[MlExcluded("RDY-057 / ADR-0003: self-reported affective data, no ML use")]
public sealed class OnboardingSelfAssessmentDocument
{
    public string Id { get; set; } = string.Empty;    // = StudentId (1:1 with student)
    public string StudentId { get; set; } = string.Empty;

    /// <summary>When the self-assessment was last captured / updated.</summary>
    public DateTimeOffset CapturedAt { get; set; }

    /// <summary>
    /// Retention ceiling. Background reaper deletes when UtcNow > ExpiresAt.
    /// 90 days on first write; null if OptInPersistent is true.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// 1-5 Likert per subject (e.g. "algebra" → 3). Missing subject = no
    /// response. Use lowercase kebab keys matching curriculum taxonomy.
    /// </summary>
    public Dictionary<string, int> SubjectConfidence { get; set; } = new();

    /// <summary>
    /// Multi-select chip selections. Stable tag ids (not display strings)
    /// — i18n happens on the SPA side. Example: "visualizer",
    /// "step-by-step", "formula-memorizer".
    /// </summary>
    public List<string> Strengths { get; set; } = new();

    /// <summary>
    /// Mirror of Strengths: self-identified friction tags. Example:
    /// "word-problems", "freeze-on-tests", "no-starting-point".
    /// </summary>
    public List<string> FrictionPoints { get; set; } = new();

    /// <summary>
    /// Per-concept affective label. Key = concept id (e.g. "derivatives").
    /// Consumed by LearningSessionActor opener heuristic.
    /// </summary>
    public Dictionary<string, TopicFeeling> TopicFeelings { get; set; } = new();

    /// <summary>
    /// Optional free text, capped at 200 chars server-side. Held in memory
    /// only for the session opener + admin teacher's classroom roll-up
    /// (aggregate counts, never shown as individual quote without consent).
    /// </summary>
    public string? FreeText { get; set; }

    /// <summary>
    /// Student opted into longer retention (no 90-day expiry). Parent
    /// consent check enforced server-side before setting to true.
    /// </summary>
    public bool OptInPersistent { get; set; }

    /// <summary>
    /// Student chose to skip the assessment entirely. True means all other
    /// fields are empty; session-opener falls back to the cold-start path.
    /// </summary>
    public bool Skipped { get; set; }

    /// <summary>
    /// Default retention window in days. Public constant so the POST
    /// endpoint and tests stay in sync.
    /// </summary>
    public const int DefaultRetentionDays = 90;
}
