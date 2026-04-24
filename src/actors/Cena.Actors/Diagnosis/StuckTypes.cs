// =============================================================================
// Cena Platform — Stuck-Type Ontology (RDY-063, ADR-0036)
//
// Session-scoped classification labels describing *what kind of stuck* a
// student is in on a given question. Different stuck-types need different
// interventions; neither pure-AI nor pure-teacher dominates across all
// types (ref: ULTRATHINK notes on RDY-062).
//
// The label is NEVER persisted on the student profile, never fed into
// cross-session ML training, never joined with demographic fields. It is
// a session-scoped cognitive-state tag that flows into the hint-ladder
// decision and (eventually, RDY-062 v2) the live-help pipeline.
//
// Evidence base (documented in ADR-0036):
//   - Aleven & Koedinger 2001 "help abuse" taxonomy (help-seeking types)
//   - Koedinger et al. 2012 "Knowledge-Learning-Instruction" framework
//   - Pardos & Bhandari 2024 RCT on ChatGPT-vs-human hints (confirms
//     that type-matched hints outperform generic hints)
// =============================================================================

namespace Cena.Actors.Diagnosis;

/// <summary>
/// Seven-category stuck ontology locked by ADR-0036. Adding a value is a
/// breaking change — all downstream scaffolds, metrics, and analytics
/// partitions must be updated simultaneously. Removing or re-ordering is
/// forbidden (values are used by wire DTOs).
/// </summary>
public enum StuckType
{
    /// <summary>Default when no signal is available yet (cold start).</summary>
    Unknown = 0,

    /// <summary>
    /// Student doesn't parse the question itself — unfamiliar wording,
    /// notation, language-switch friction. Signal: long time with no
    /// attempt, or rapid blank submissions.
    /// </summary>
    Encoding = 1,

    /// <summary>
    /// Student can't retrieve the theorem/definition needed. Signal:
    /// attempt references wrong or absent procedure; blank answer after
    /// thinking time.
    /// </summary>
    Recall = 2,

    /// <summary>
    /// Student knows the procedure but can't execute a specific step.
    /// Signal: correct setup, wrong or missing intermediate step.
    /// </summary>
    Procedural = 3,

    /// <summary>
    /// Student knows available tools but can't pick the right one.
    /// Signal: multiple method attempts without commitment, mid-solution
    /// pivots.
    /// </summary>
    Strategic = 4,

    /// <summary>
    /// Student is confidently wrong on a repeated pattern. Signal:
    /// ≥3 attempts with the same error signature across items in this
    /// session (not cross-session — ADR-0003).
    /// </summary>
    Misconception = 5,

    /// <summary>
    /// Student could continue but doesn't want to. Signal: long pause,
    /// then low-effort attempts; help-request without engagement.
    /// </summary>
    Motivational = 6,

    /// <summary>
    /// Student reports "I'm lost" or shows no engagement signal on a
    /// brand-new item. Requires emotional validation + stepping back,
    /// not a scaffold. Highest candidate for teacher involvement.
    /// </summary>
    MetaStuck = 7,
}

/// <summary>
/// Downstream scaffolding strategy. Consumers (hint ladder now, live-help
/// pipeline in RDY-062 v2) pick the template/tone that matches. Strategy
/// is derived from the stuck-type plus context; multiple stuck-types can
/// share a strategy (e.g., Motivational and MetaStuck both lean on
/// Encouragement + TeacherInvolved).
/// </summary>
public enum StuckScaffoldStrategy
{
    Unspecified = 0,

    /// <summary>Rephrase the question, offer a similar-worded example. Encoding.</summary>
    Rephrase = 1,

    /// <summary>Show the relevant theorem or definition verbatim. Recall.</summary>
    ShowDefinition = 2,

    /// <summary>Show the next step scaffold, not the whole solution. Procedural.</summary>
    ShowNextStep = 3,

    /// <summary>Ask "what's the goal? what's given?" to force decomposition. Strategic.</summary>
    DecompositionPrompt = 4,

    /// <summary>Offer a targeted contradiction that exposes the misconception. Misconception.</summary>
    ContradictionPrompt = 5,

    /// <summary>Encouragement copy without content disclosure. Motivational.</summary>
    Encouragement = 6,

    /// <summary>Step way back; re-ground in the current chapter's core idea. MetaStuck.</summary>
    Regroup = 7,
}

/// <summary>
/// Source of a diagnosis — used for metric partitioning + audit.
/// </summary>
public enum StuckDiagnosisSource
{
    /// <summary>Classifier disabled or returned Unknown; caller used fallback path.</summary>
    None = 0,

    /// <summary>Fast heuristic rules fired without LLM call.</summary>
    Heuristic = 1,

    /// <summary>LLM classifier (Haiku) produced the label.</summary>
    Llm = 2,

    /// <summary>Heuristic + LLM agreed; confidence escalated.</summary>
    HybridAgreement = 3,

    /// <summary>Heuristic + LLM disagreed; caller received low-confidence output.</summary>
    HybridDisagreement = 4,

    /// <summary>Circuit breaker open — LLM skipped, heuristic-only fallback.</summary>
    CircuitBreaker = 5,

    /// <summary>LLM error; returned heuristic result or Unknown.</summary>
    LlmError = 6,
}
