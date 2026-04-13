# ML Training Prohibition -- Cena Adaptive Learning Platform

**Status: DRAFT -- awaiting legal counsel review**
**Created**: 2026-04-13 (GD-005)
**Legal basis**: FTC v. Edmodo "Affected Work Product" decree (2023), COPPA 2025 Final Rule, GDPR Art 5(1)(b), Israel PPL Amendment 13
**Version**: 1.0

---

## 1. Purpose

This document establishes Cena's absolute prohibition on using student data to
train, fine-tune, or improve machine learning models. This is not a future
aspiration -- it is a current technical and contractual requirement derived from
regulatory enforcement precedent and implemented in code.

---

## 2. Scope

This prohibition applies to:

- All student personal data (as classified by `PiiClassification.cs`)
- All learning events, session data, and behavioral analytics
- All AI tutor conversation transcripts
- All misconception detection data
- All profiling data (Elo ratings, BKT mastery states, IRT parameters)
- All aggregated or pseudonymized data that could be re-identified

It applies to every party in the data processing chain: Cena engineering,
Cena's third-party processors (Anthropic, Google/Firebase), and any future
sub-processors.

---

## 3. Legal Foundation

### 3.1 The Edmodo precedent (FTC 2023)

The FTC's consent decree against Edmodo required deletion of **all models
trained on children's data** -- termed "Affected Work Product." This was not
limited to raw data; it extended to derived ML artifacts. The decree
established that:

1. Per-student behavioral profiles of minors are educational records subject to COPPA and FERPA
2. Aggregating per-student traces into a global model makes the model itself an "Affected Work Product"
3. Retaining per-student behavioral timelines beyond the instructional purpose triggers data minimization violations

### 3.2 Subsequent enforcement

| Enforcement action | Date | Relevance to Cena |
|--------------------|------|-------------------|
| FTC v. Edmodo | 2023 | "Affected Work Product" -- models trained on student data must be deleted |
| FTC 2025 COPPA Final Rule | 2025 | Explicit data minimization for minors; prohibition on retention beyond instructional purpose |
| ICO v. Reddit (14.47M GBP) | Feb 2026 | Per-user behavioral profiles of minors constitute profiling under GDPR Art 22 |
| Israel PPL Amendment 13 | In force | Enhanced protections for minors' personal data; applies to all Cena users in Israel |
| GDPR Art 5(1)(b) | In force | Purpose limitation -- data collected for education cannot be repurposed for ML training |

---

## 4. ADR-0003: Misconception Data is Session-Scoped

[ADR-0003](../adr/0003-misconception-session-scope.md) is the architectural
decision that anchors this prohibition in code. Key provisions:

### 4.1 Session boundary

Misconception telemetry lives inside the `LearningSessionState` aggregate. It
is **never** persisted to a student's long-lived profile, **never** used to
train global ML models, and **never** exported outside the session boundary.

```
// ALLOWED -- session-scoped
LearningSessionState.ActiveMisconceptions

// PROHIBITED -- never on student profile
StudentState.MisconceptionTimeline  // does not exist; ADR-0003 prevents it
StudentState.MisconceptionCounts    // does not exist; ADR-0003 prevents it
```

### 4.2 Ephemeral tally

The `SessionMisconceptionTally` record in `MisconceptionCatalog.cs`
(`src/actors/Cena.Actors/Services/MisconceptionCatalog.cs`) is explicitly
ephemeral:

```csharp
public record SessionMisconceptionTally(
    string SessionId,
    Dictionary<string, int> DetectionCounts,
    Dictionary<string, bool> Remediated
);
```

This tally is discarded after the session ends. It is never written to
persistent storage beyond the session event stream, which itself has a 30-day
retention cap (hard maximum: 90 days per ADR-0003).

### 4.3 ML training exclusion (ADR-0003 Decision 3)

Misconception data **MUST** be excluded from any corpus used for:

- LLM fine-tuning or RLHF
- Embedding model training
- Recommendation model training
- Any model that could constitute "Affected Work Product" under the Edmodo precedent

Implementation: a filter tag `[ml-excluded]` on misconception event types,
enforced by a test that scans any training data pipeline for events with this tag.

### 4.4 Retention schedule

| Data type | Retention | Justification |
|-----------|-----------|---------------|
| Session misconception events | 30 days | Active remediation window |
| Session misconception events (hard cap) | 90 days | COPPA data minimization |
| Anonymized catalog-level aggregates (k >= 10) | Indefinite | No personal data; e.g. "42% of students hit DIST-EXP-SUM on algebra unit 3" |

---

## 5. Anthropic API Usage -- No Training on Inputs

### 5.1 API vs. fine-tuning

Cena uses the **Anthropic API** (Claude) for AI tutoring. We do **not**:

- Fine-tune Anthropic models on student data
- Use Anthropic's model customization or training features
- Export student data to any ML training pipeline

We use the standard chat completions API endpoint at `api.anthropic.com`.
Student conversation messages are sent as API requests and responses are
streamed back in real-time.

### 5.2 Anthropic's data use policy

Per Anthropic's usage policy for API customers: **API inputs and outputs are
NOT used for model training.** Anthropic states that data submitted through
the API is not used to train or improve their models.

### 5.3 Consent gating

The `ThirdPartyAi` processing purpose in `ProcessingPurpose.cs` controls
access to the AI tutor:

```csharp
[PurposeDescription("AI tutoring assistance powered by third-party services")]
[DefaultValue(true)]
[LawfulBasis(LawfulBasis.Consent)]
[MinorDefault(false)]    // OFF by default for minors
ThirdPartyAi,
```

For minors, the AI tutor is **disabled by default** (`MinorDefault(false)`).
Parental consent must be explicitly granted before any student data is
transmitted to Anthropic.

### 5.4 Tutor message retention

AI tutor conversations are retained for 90 days, then permanently purged:

```csharp
// DataRetentionPolicy.cs
public static readonly TimeSpan TutorMessageRetention = TimeSpan.FromDays(90);
```

This applies to `TutorMessageDocument` and `TutorThreadDocument`. The
`RetentionWorker` (`src/shared/Cena.Infrastructure/Compliance/RetentionWorker.cs`)
enforces this purge on a daily schedule. There is no long-term storage of
tutor conversations for any purpose, including model training.

> **LEGAL REVIEW REQUIRED**: Determine whether Anthropic's current data use
> policy (stating API inputs are not used for training) is sufficient as a
> contractual commitment, or whether a specific clause in the Data Processing
> Agreement (DPA) explicitly prohibiting training on Cena student data is
> required. The DPA with Anthropic is currently PENDING execution (tracked in
> DPIA Section 8). Recommend that the DPA include: (a) an explicit prohibition
> on using Cena data for model training, fine-tuning, RLHF, or any form of
> model improvement; (b) a commitment to delete all cached data within a
> defined period; (c) audit rights to verify compliance.

---

## 6. No Custom ML Models

Cena does **not** train any custom machine learning models on student data.
The platform's adaptive algorithms are:

| Algorithm | Type | Student data dependency | ML training involved? |
|-----------|------|------------------------|----------------------|
| Elo rating | Mathematical formula | Per-student running score | No -- pure arithmetic update rule |
| Bayesian Knowledge Tracing (BKT) | Probabilistic model | Per-student per-skill attempt counts | No -- Bayesian update with fixed priors |
| IRT (Item Response Theory) | Statistical model | Item parameters (not student-specific) | No -- parameters estimated from population, not individual training |
| Misconception detection | Rule-based catalog | Pattern matching against 15 known buggy rules | No -- static catalog, no learning |
| Explanation caching | Content cache | Keyed by (questionId, errorType, language) -- not by student | No -- cache lookup, no model |

None of these algorithms constitute ML model training on student data. They
are mathematical update rules or static lookup tables.

---

## 7. No Data Export to ML Pipelines

There are no data export mechanisms, ETL pipelines, data warehouses, or
analytics platforms that receive student data for ML purposes. Specifically:

- No BigQuery, Snowflake, or data lake exports
- No feature stores or ML experiment tracking systems
- No A/B testing frameworks that feed ML models
- No recommendation engine training pipelines
- No data sharing agreements with research institutions for ML purposes

The only data exports are:

1. **GDPR Art 20 portability export** (`StudentDataExporter.cs`) -- initiated by the data subject, delivered to the data subject
2. **Admin analytics dashboards** -- read-only queries within the tenant boundary, no data leaves the platform
3. **Anonymized k-anonymity aggregates** (k >= 10) -- catalog-level statistics with no individual data

---

## 8. Ship-gate Enforcement

The ship-gate CI scanner (`docs/engineering/shipgate.md`,
`scripts/shipgate/scan.mjs`) enforces related prohibitions on every pull
request:

### 8.1 Banned engagement patterns

| Banned pattern | Rationale |
|----------------|-----------|
| Streak counters that can go to zero | Loss aversion exploitation |
| Variable-ratio rewards on answer correctness | Slot-machine pattern |
| Loss-aversion copy ("don't break", "you'll lose", "keep the chain") | Manipulative design |
| Guilt/shame push notifications | Exploitative engagement |
| Default-on social matchmaking or public leaderboards ranking minors | Privacy violation |
| Streak-freeze currency | Monetized loss aversion |

### 8.2 CI enforcement

The scanner runs on every PR and checks:

1. Locale files (en.json, ar.json, he.json) for banned terms
2. Vue templates and TypeScript source for banned patterns
3. C# backend code for banned patterns
4. Allowlist at `scripts/shipgate/allowlist.json` for legitimate exceptions

This is relevant to the ML training prohibition because engagement mechanics
that depend on ML-trained recommendation models (e.g. personalized notification
timing, variable-ratio reward calibration) would violate both the ship-gate
and this prohibition.

---

## 9. Implementation Status

| Requirement | Code reference | Status |
|-------------|---------------|--------|
| ADR-0003 misconception session-scoping | `MisconceptionCatalog.cs`, ADR-0003 | Implemented |
| SessionMisconceptionTally ephemeral design | `MisconceptionCatalog.cs` | Implemented |
| ThirdPartyAi consent gating (minor default OFF) | `ProcessingPurpose.cs` | Implemented |
| Tutor message 90-day retention | `DataRetentionPolicy.cs`, `RetentionWorker.cs` | Implemented |
| Ship-gate CI scanner | `scripts/shipgate/scan.mjs` | Implemented |
| No ML training pipelines in codebase | Architecture-level | Verified (no ML training code exists) |
| Anthropic DPA with training prohibition clause | -- | PENDING (DPIA Section 8) |
| `[ml-excluded]` filter tag on misconception events | -- | PLANNED (ADR-0003 Decision 3) |
| Test scanning training pipelines for excluded events | -- | PLANNED (ADR-0003 Decision 3) |

---

## Review History

| Date | Reviewer | Notes |
|------|----------|-------|
| 2026-04-13 | claude-code | Skeleton created (GD-005) |
| 2026-04-13 | claude-code | Substantive rewrite with codebase-derived content |
