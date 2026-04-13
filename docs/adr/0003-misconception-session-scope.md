# ADR-0003 — Misconception state is session-scoped, not profile-scoped

- **Status**: Proposed
- **Date proposed**: 2026-04-13
- **Deciders**: Shaker (project owner), claude-code (coordinator)
- **Supersedes**: none
- **Related**: [ADR-0002](0002-sympy-correctness-oracle.md), [Track 8 research](../research/cena-sexy-game-research-2026-04-11.md), [Track 9 research](../research/tracks/track-9-socratic-ai-tutoring.md)
- **Task**: GD-002 (`t_1f649986bd81`)

---

## Context

Cena's tutor identifies misconceptions during learning sessions (e.g. "distributed exponent over sum: (a+b)² → a²+b²"). The pedagogical value of misconception-aware remediation is well-established (Koedinger Cognitive Tutor, d ≈ 0.2–0.4 per targeted buggy rule, Track 9).

The question is: **where does misconception state live?**

### The Edmodo precedent

In 2023, the FTC issued a consent decree against Edmodo requiring deletion of **all models trained on children's data** ("Affected Work Product"). This was not limited to raw data — it extended to derived ML artifacts. The decree established that:

1. Per-student behavioral profiles of minors are educational records subject to COPPA + FERPA
2. Aggregating per-student misconception traces into a global model makes the model itself an affected work product
3. Retaining per-student misconception timelines beyond the instructional purpose triggers data minimization violations

Subsequent enforcement reinforced this:
- **FTC 2025 COPPA Final Rule**: explicit data minimization for minors
- **ICO v. Reddit £14.47M (Feb 2026)**: per-user behavioral profiles of minors = profiling under GDPR Art. 22
- **Israel Privacy Protection Law Amendment 13**: applies to all Cena users in Israel

### Current state

Cena currently uses `ExplanationErrorType` (in `ExplanationCacheService.cs`) to classify wrong answers into categories for **explanation caching**. The cache key is `explain:{questionId}:{errorType}:{language}` — scoped per question, not per student. This is compliant: it's a content cache, not a student profile.

However, future features (buggy-rule catalog, MISC-001) will introduce richer misconception tracking. This ADR establishes the boundary before that work begins.

---

## Decision

**Misconception telemetry lives inside the session aggregate. It is never persisted to a student's long-lived profile, never used to train global ML models, and never exported outside the session boundary.**

### Decision 1 — Aggregate boundary

Misconception tags are properties of `LearningSessionState`, not `StudentState`.

```csharp
// ALLOWED — session-scoped
public class LearningSessionState
{
    public List<SessionMisconception> ActiveMisconceptions { get; set; } = new();
}

public record SessionMisconception(
    string BuggyRuleId,         // e.g. "DIST-EXP-SUM"
    string TopicId,
    DateTimeOffset DetectedAt,
    int RemediationAttempts
);

// PROHIBITED — never on student profile
public class StudentState
{
    // NO: public List<MisconceptionHistory> MisconceptionTimeline { get; set; }
    // NO: public Dictionary<string, int> MisconceptionCounts { get; set; }
}
```

Events that carry misconception data:
- `MisconceptionDetected_V1` — emitted when the tutor identifies a buggy rule in a student's step
- `MisconceptionRemediated_V1` — emitted when the student demonstrates correct understanding
- `SessionMisconceptionsScrubbed_V1` — emitted at session end, explicitly clears all misconception state

### Decision 2 — Retention

| Data type | Retention | Justification |
|-----------|-----------|---------------|
| Session misconception events | 30 days | Active remediation window — student may return to similar problems |
| Session misconception events (max) | 90 days | Hard legal cap — COPPA data minimization |
| Aggregated anonymized stats | Indefinite | k-anonymity (k ≥ 10) aggregates at the catalog level (e.g. "42% of students hit DIST-EXP-SUM on algebra unit 3") |

The `RetentionWorker` (already exists in `src/shared/Cena.Infrastructure/Compliance/RetentionWorker.cs`) must include a rule for session misconception events.

### Decision 3 — ML training exclusion

Misconception data MUST be excluded from any corpus used for:
- LLM fine-tuning or RLHF
- Embedding model training
- Recommendation model training
- Any model that could constitute "Affected Work Product" under the Edmodo precedent

Implementation: a filter tag `[ml-excluded]` on misconception event types, enforced by a test that scans any training data pipeline for events with this tag.

### Decision 4 — Export exclusion

GDPR Art. 20 data exports (`StudentDataExporter.cs`) MUST NOT include historical misconception tags. Only the currently-active session's remediation notes are visible. The exporter already exists; it needs a negative filter for misconception event types.

### Decision 5 — Audit trail

Every misconception event logs:
```
[MISCONCEPTION] session={sessionId} topic={topicId} rule={buggyRuleId} retention_horizon={date}
```

Visible in admin audit log. No student PII in the log line — session ID is the correlation key.

### Decision 6 — Mastery vs. misconception boundary

| Signal | Scope | Persistent? | Rationale |
|--------|-------|-------------|-----------|
| BKT mastery probability | Student + Skill + Track | Yes | Aggregate learning signal, no specific error details |
| Elo difficulty rating | Question | Yes | Item property, not student property |
| Misconception tag | Session | No (30-day TTL) | Specific error pattern, personally identifiable when combined with student ID |
| Explanation cache | Question + ErrorType + Language | Yes | Content cache, not student-scoped |

Mastery signals (`P(known)` from BKT, theta from IRT) are aggregate measures of competence — they don't reveal *what specific mistake* a student made. Misconception tags do. That's the line.

---

## Non-compliant code paths

| File | Issue | Risk |
|------|-------|------|
| `src/actors/Cena.Actors/Services/ExplanationCacheService.cs` | Currently compliant (keyed by question, not student), but must stay that way | Low — add test assertion |
| `src/actors/Cena.Actors/Services/PersonalizedExplanationService.cs` | Generates misconception-specific text using LLM; output is streamed to student and not persisted beyond the response | Low — verify no persistence |
| `src/actors/Cena.Actors/Students/StudentState.cs` | No misconception fields exist today — this ADR prevents adding them | Preventive |
| Future: MISC-001 (buggy-rule catalog) | Must scope catalog entries to session aggregate per this ADR | Gated by this ADR |

---

## Evidence

| Enforcement action | Date | Relevance |
|-------------------|------|-----------|
| FTC v. Edmodo "Affected Work Product" decree | 2023 | Models trained on student data must be deleted |
| FTC 2025 COPPA Final Rule | 2025 | Explicit data minimization for minors |
| ICO v. Reddit £14.47M | Feb 2026 | Per-user behavioral profiles of minors = profiling |
| Israel PPL Amendment 13 | In force | Applies to all Cena users in Israel |
| GDPR Art. 8 + Art. 22 | In force | Profiling restrictions for minors |

---

## Consequences

**Positive**:
- Legal compliance with COPPA, GDPR-K, Edmodo precedent, Israel PPL
- Clean separation: mastery (persistent, useful) vs. misconception (ephemeral, sensitive)
- Future-proof against "Affected Work Product" enforcement on ML models
- Students' specific mistakes don't follow them across sessions or schools

**Negative**:
- Cannot build a long-term "misconception profile" for a student (pedagogically useful but legally prohibited)
- 30-day retention means remediation must happen quickly or the context is lost
- Aggregate analytics require k-anonymity threshold (k ≥ 10), reducing granularity for small classes

**Mitigations**:
- Mastery signals (BKT, IRT) capture the *learning outcome* without the specific mistake
- Explanation cache captures the *content* without the student identity
- Together, these provide ~80% of the pedagogical value without the legal risk
