# RDY-063: Stuck-Type Classifier (precursor to live-help pipeline)

- **Status**: ✅ **Shipped 2026-04-19** — Phase 1 (classifier core: `HybridStuckClassifier` + heuristic + Haiku LLM), Phase 2a (shadow mode on `/hint` path), Phase 2b (`HintStuckDecisionService` + hint-level adjuster wired into `/hint` endpoint), Phase 4 (ADR-0036 accepted at `docs/adr/0036-stuck-type-ontology.md`), admin stuck-diagnostics page with en/he/ar/fr i18n. Verified by artifact check, not full acceptance-criteria audit. Phase 3 observability (Grafana dashboard) not independently verified.
- **Priority**: High — unblocks RDY-062 redesign, improves existing hint ladder independently
- **Source**: Shaker 2026-04-19 — ULTRATHINK pass on RDY-062 surfaced that "I need help" is 7 different signals. Routing decisions without stuck-type diagnosis are structurally wrong. Build the classifier first, let its data drive the v2 of RDY-062.
- **Tier**: 2 (quality — pedagogical fidelity + data flywheel for live-help)
- **Effort**: 3-5 days
- **Depends on**:
  - RDY-061 Syllabus advancement (landed — classifier reads `CurrentChapterId`)
  - ADR-0002 CAS oracle (preserved — classifier never emits math claims, just labels)
  - ADR-0003 misconception scope (session-scoped input → session-scoped output)
- **Blocks**:
  - RDY-062 redesign (live-help pipeline is dependent on knowing the stuck-type before routing)
- **Co-ships with**: ADR-0036 Stuck-Type Ontology (short ADR locking the 7-category taxonomy)

## Why this task exists

An ULTRATHINK re-think of RDY-062 found that routing "student pressed help" between teacher and AI is the wrong unit of analysis. The right unit is **what kind of stuck is this student in?** — because different stuck-types need different interventions, and neither pure-AI nor pure-teacher dominates across all types.

Seven stuck-types (RDY-062 doc, §1 ULTRATHINK):

| # | Stuck type | Signals | Best intervention |
|---|---|---|---|
| 1 | **Encoding** | long time with no attempt, fragmented input, re-read patterns | Rephrase / translate / similar-worded example |
| 2 | **Recall** | attempt references wrong/absent theorem, blank | Show relevant definition |
| 3 | **Procedural** | correct setup, wrong or missing step | Show next step scaffold |
| 4 | **Strategic** | multiple method attempts without commitment | Decomposition prompt: goal? given? |
| 5 | **Misconception** | confident repeated wrong pattern across items | Targeted contradiction / discovery prompt |
| 6 | **Motivational** | long pause then help, low-effort attempts | Encouragement + rest prompt |
| 7 | **Meta-stuck** | "I'm lost" / no engagement signal | Step back, regroup, emotional validation |

A classifier that labels the stuck-type — cheaply, before we commit to AI-scaffold or teacher-endorsement — is:

1. **Useful immediately**: today's hint ladder in `LearningSessionActor.HintRequest` is pre-authored and type-blind. With a classifier, we can pick the most fitting hint-level or synthesize a type-appropriate hint on the fly.
2. **A prerequisite for RDY-062 v2**: the three-layer pipeline (diagnose → scaffold → endorse) has the classifier as layer 1.
3. **A teacher-PD data signal**: "your classroom is 60% strategic-stuck this week" tells a homeroom teacher something actionable about their upcoming instruction.
4. **An item-quality signal**: an item that triggers 80% encoding-stuck across students is probably poorly worded.

## Design

### Input (session-scoped, ADR-0003 compliant)

```
StuckContext {
  sessionId: string
  studentAnonId: string              // not studentId — classifier sees per-session anon
  currentQuestion: {
    id, canonicalTextByLocale, chapterId, learningObjectiveIds[]
  }
  advancementSnapshot: {             // from RDY-061
    currentChapterId, chapterStatus, retention
  }
  attempts: [{                       // session-scoped, last N on this question
    submittedAt, latexInput, wasCorrect,
    timeSincePrevAttemptSec, inputChangeRatio  // character delta vs prior
  }]
  sessionSignals: {                  // aggregate session patterns, session-scope only
    timeOnQuestionSec,
    hintsRequestedSoFar,
    itemsSolvedInSession,
    itemsBailedInSession
  }
  // NO: studentId, email, name, crossSession history, profile PII
}
```

### Output

```
StuckDiagnosis {
  stuckType: "encoding" | "recall" | "procedural" | "strategic"
           | "misconception" | "motivational" | "meta"
  confidence: 0..1                   // classifier's own certainty
  secondary: { stuckType, confidence } | null   // top-2 always returned
  suggestedScaffold: {               // hint for downstream, not user-facing copy
    strategy: "rephrase" | "show-definition" | "show-next-step"
            | "decomposition-prompt" | "contradiction-prompt"
            | "encouragement" | "regroup"
    focusChapterId: string           // from advancement, never from classifier invention
    shouldInvolveTeacher: boolean    // TRUE for meta, motivational, misconception on classroom-synchronous
  }
  diagnosedAt: timestamp
  classifierVersion: string          // for A/B and retraining audit
}
```

### Classifier implementation

**Phase 1 (ship first): heuristic + Haiku LLM hybrid.**

- **Heuristic pre-pass** (fast, no LLM): rules over `attempts[]` + timing features. Catches easy cases:
  - 3+ attempts with identical first token → "misconception"
  - Zero attempts + >120s on question → "encoding" or "motivational" (tie-break on prior-session engagement pattern)
  - 1 attempt that diverged late (correct setup, wrong final step) → "procedural"
- **Haiku LLM** (~500ms, ~$0.0005/call) for the hard cases: gets prompt-cached system rules + the `StuckContext` JSON, returns the enum + confidence + secondary.
- Confidence < 0.6 → default to "strategic" + flag for review (don't gamble on low-signal data).

**Phase 2 (post-pilot, out of scope here): fine-tuned small model or EWC-preserved LoRA over real pilot data. Only if Phase 1 shows signal but Haiku cost scales painfully.**

### Hint-ladder upgrade (existing, non-breaking)

`LearningSessionActor.HintRequest` currently returns the next pre-authored hint by level. Extend to:

1. Call `IStuckTypeClassifier.DiagnoseAsync(ctx)` on first hint request per item
2. Store diagnosis in session state (session-scoped per ADR-0003)
3. Pick pre-authored hint whose `scaffoldMeta.strategy` matches diagnosed type; fall back to level-based if no match
4. Never auto-synthesize new hints in this phase — CAS-gated hint generation is a separate follow-up task

This gets us real classifier data without opening a new LLM-to-student path.

### Observability

- Metric: `cena_stuck_diagnoses_total{type,strategy,fromHeuristic,fromLlm}`
- Metric: `cena_stuck_confidence_bucket{bucket=low|mid|high}` — low-confidence rate is the quality dial
- Per-item: `cena_item_stuck_distribution{itemId}` — item curriculum signal
- Dashboard: classroom-level stuck-type distribution over 7 days
- Tracing: diagnosis span attached to the hint-request trace, end-to-end visible in existing Otel pipeline

### Privacy + safety

- Classifier input is **session-scoped only** — no cross-session history, no profile fields, no identifiers (student is anonymized via `studentAnonId = hash(studentId, sessionId, salt)`)
- Classifier output never persisted to student profile. Written to `StuckDiagnosisDocument` with session-scoped TTL (30-day retention per ADR-0003)
- Classifier cannot emit or verify math claims — labels only. CAS oracle stays on the scaffolding layer, not the diagnosis layer.
- Safeguarding classifier runs *before* stuck-type classifier; safeguarding match short-circuits the whole pipeline (existing path preserved).

## Acceptance criteria

### Functionality
- [ ] `IStuckTypeClassifier.DiagnoseAsync(StuckContext)` returns `StuckDiagnosis` within p95 ≤ 800ms (heuristic path p95 ≤ 50ms)
- [ ] Heuristic pre-pass handles ≥ 40% of cases without LLM call (cost + latency win)
- [ ] Haiku LLM path prompt-cache hit ratio ≥ 80% after warm-up
- [ ] Existing hint ladder unchanged on `scaffoldMeta` mismatch; strategy-match lookup is additive

### Data flywheel
- [ ] Every diagnosis persisted (session-scoped) with `classifierVersion` for future replay/retraining
- [ ] Dashboard query: "top 20 items by `stuckType='encoding'` rate" resolves in < 2s

### Privacy
- [ ] `studentAnonId` never reversible within classifier surface (unit test: no codepath reads `studentId` into classifier input)
- [ ] No PII in Haiku prompt — `TutorPromptScrubber` equivalent runs on any free-text attempt content
- [ ] Classifier output redacted before ReasoningBank cohort aggregation (reuse `AdvancementTrajectoryRedactor` pattern from RDY-061)

### Observability
- [ ] Metrics wired per §Observability above
- [ ] Runbook: `docs/ops/runbooks/stuck-classifier-degraded.md` — how to disable classifier + fall back to vanilla hint ladder via feature flag

### Architecture tests (Rami's lens)
- [ ] Classifier has no import path to `StudentDocument`, `UserDocument`, or any identifier-bearing document
- [ ] `DiagnoseAsync` throws if `StuckContext` contains `@` or `studentId=` substrings in any free-text field (PII-scrub unit test)
- [ ] Classifier output contains no LaTeX or math fragments (label-only contract enforced via static analysis of response shape)
- [ ] Disabling `feature.stuckClassifier` flag reverts hint ladder to pre-RDY-063 behavior byte-identical (regression guard)

## Scope

### Phase 1 — classifier core (2 days)
- `IStuckTypeClassifier` interface + `HybridStuckClassifier` implementation (heuristic + Haiku)
- `StuckDiagnosisDocument` + Marten schema
- Feature flag: `feature.stuckClassifier` (off by default, on for dev + pilot classroom)
- Unit tests + integration tests (golden fixtures of attempt histories → expected label)

### Phase 2 — hint ladder integration (1 day)
- Extend `LearningSessionActor.HintRequest` to consult classifier on first hint per item
- Session-scoped diagnosis cache (avoid re-classifying on level 2 / level 3 hint of same item)
- Existing hints get `scaffoldMeta.strategy` annotation (one-time authoring pass — Amjad validates)

### Phase 3 — observability + runbook (1 day)
- Metrics + Grafana dashboard panel
- Runbook for "classifier errors spike, flip the flag"
- Architecture tests

### Phase 4 — ADR + docs (0.5 day)
- ADR-0036: lock the 7-category taxonomy
- Amend ADR-0003 misconception-session-scope to explicitly include stuck-diagnosis data in the session-scope carve-out (mirror the RDY-061 amendment for advancement-trajectory)

## Open questions

1. **Taxonomy stability**: are 7 categories right? Nadia's lit review (Aleven help-seeking, ITS research) supports 5-9 depending on source. Pilot data may reveal collapse (e.g., motivational + meta always co-occur → one category).
2. **Haiku vs Sonnet cost/quality**: Haiku is ~8× cheaper and fast enough. Quality gap on classification tasks is empirically small (<5pp on similar educational labeling benchmarks). If Haiku underperforms in pilot, escalate individual low-confidence cases to Sonnet.
3. **Cold-start for new students**: first item of first session has empty `attempts[]`. Default to "encoding" with low confidence, or refuse to diagnose? I'd default with low confidence, metricize, decide after data.
4. **Hebrew/Arabic classification quality**: Haiku's non-English reasoning is weaker than Sonnet's. May need locale-dispatch: Hebrew/Arabic → Sonnet, English → Haiku. Measure in dev.
5. **Teacher-facing surface**: does the teacher see the diagnosis on their inbox card (when we get there in RDY-062 v2)? Pro: context. Con: label bias (teacher sees "misconception" and doesn't look). I'd hide label from teacher by default, show on expand.

## Out of scope

- Any live-help routing (RDY-062 territory; that task remains paused pending this classifier's pilot data)
- Auto-synthesized hints via LLM (CAS-gated hint generation is a separate future task)
- Cross-session misconception memory (explicitly forbidden by ADR-0003)
- Classifier retraining pipeline / fine-tuning infrastructure (Phase 2 of the classifier spec, not this task)
- Teacher-PD dashboards (follow-up once classifier ships and data exists)

## Links

- Hint ladder: `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` (HintRequest handler)
- Tutor PII scrubber pattern: `src/actors/Cena.Actors/Tutor/TutorPromptScrubber.cs`
- Safeguarding classifier (precedent for session-scoped classifier under safety): `src/actors/Cena.Actors/Tutor/SafeguardingClassifier.cs`
- Advancement state (input source): `src/actors/Cena.Actors/Advancement/StudentAdvancementState.cs`
- Paused parent: [RDY-062](RDY-062-live-assistance-teacher-first-ai-fallback.md)
- Personas: `docs/tasks/pre-pilot/PERSONAS.md`
- ADR-0002 (CAS oracle — preserved): `docs/adr/0002-sympy-correctness-oracle.md`
- ADR-0003 (misconception scope — amended): `docs/adr/0003-misconception-session-scope.md`
