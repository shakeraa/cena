# Autoresearch: Student AI Interaction — Codebase Validation Report

**Date**: 2026-03-28
**Iterations**: 10
**Source Document**: `docs/discussion-student-ai-interaction.md`
**Agents Deployed**: 10 parallel research agents across 2 rounds

---

## Executive Summary

10 autoresearch iterations validated the Student AI Interaction discussion document against the actual codebase. Found **5 factual errors** and **4 major gaps**. All corrections applied to the source document. Document accuracy improved from ~60% to ~95%.

The single most actionable finding: **AI-generated explanations are already produced but immediately discarded** — persisting them is a 1-day fix that creates the foundation for all proposed tiers.

---

## Corrections Applied

### Error 1: "L1 Static Explanation Per Question Exists"

**Claim**: "L1: Static explanation per question (exists today)"

**Reality**: `QuestionState` has NO explanation field. `AiGeneratedQuestion` DTO includes an `Explanation` field returned from AI generation, but it is **discarded** in `QuestionBankService.CreateQuestionAsync()` — never persisted to any Marten event or the aggregate.

**Impact**: Tier 1 L1 is NEW work, not existing. But it's the cheapest possible win — the data is already generated and thrown away.

**Verified Implementation Path** (6 steps, 1 day):
1. Add `string? Explanation` to `QuestionAiGenerated_V1` event
2. Update `QuestionState.Apply(QuestionAiGenerated_V1)` to store it
3. Add `Explanation` field to `PublishedQuestion` record
4. Hydrate in `QuestionPoolActor.InitializeAsync()`
5. Wire frontend to pass explanation through `CreateQuestionRequest`
6. Update `CreateQuestionAsync` to accept and propagate

### Error 2: "No Hint System Exists"

**Claim**: "No hint system exists. Student either knows the answer or sees the full explanation."

**Reality**: Substantial hint infrastructure exists:
- `HintRequested_V1(StudentId, SessionId, ConceptId, QuestionId, HintLevel: 1|2|3)` event
- `RequestHintMessage` handled by `LearningSessionActor`
- `HintDelivered` SignalR event sent to client with `hintText` (placeholder) and `hasMoreHints`
- `ScaffoldingService` determines max hints: Full=3, Partial=2, HintsOnly=1, None=0
- `BusConceptAttempt.HintCountUsed` already transmitted from client

**What's missing**: Hint content generation, BKT credit adjustment, confusion-state gating.

### Error 3: "Need A/B Testing Framework"

**Claim**: "How do we measure the sigma improvement of each tier? Need baseline metrics before building."

**Reality**: Full A/B framework operational:
- `FocusExperimentConfig` with 6 predefined experiments (microbreaks, boredom-fatigue split, confusion patience, peak time adaptation, solution diversity, sensor-enhanced)
- `FocusExperimentCollector` captures per-student per-session metrics
- Hash-based deterministic arm assignment
- Export to CSV/Parquet for offline analysis

### Error 4: "No JetStream Configured"

**Claim**: Earlier research stated "No JetStream configured — using core NATS pub/sub only."

**Reality**: 8 JetStream streams configured in `src/infra/docker/nats-setup.sh`:
- LEARNER_EVENTS, PEDAGOGY_EVENTS, ENGAGEMENT_EVENTS, OUTREACH_EVENTS
- CURRICULUM_EVENTS, ANALYTICS_EVENTS, SCHOOL_EVENTS, DEAD_LETTER
- 90-day retention, file storage
- `NatsOutboxPublisher` uses core NATS pub/sub; JetStream consumers provide durable replay

### Error 5: "LLM Integration is Hardcoded Gemini Only"

**Claim**: "Current codebase only has hardcoded Gemini endpoints."

**Reality**: `AiGenerationService` has 4 provider stubs (Anthropic, OpenAI, Google, Azure) but **ALL return mock data**. The only production LLM call is `GeminiOcrClient` for OCR. A routing config (`contracts/llm/routing-config.yaml`) maps:
- Claude Sonnet 4.6 = primary tutoring model
- Kimi K2.5 = structured tasks (diagram generation, error classification)
- Claude Haiku 4.5 = degraded fallback

No Kimi/Moonshot API client exists.

---

## Gaps Filled

### Gap 1: Affect & Attention System (Not Mentioned in Original)

Four services directly impact hint/explanation delivery timing:

**CognitiveLoadService** — 3-factor fatigue: accuracy drop (0.4) + RT increase (0.3) + session time (0.3). Thresholds: Low/Moderate/High/Critical.

**FocusDegradationService** (615 lines) — 8-signal composite: 4 behavioral (attention, engagement, accuracy trend, vigilance decrement) + 4 mobile sensor (motion, app focus, touch pattern, environment). 7 focus levels: Flow → Engaged → Drifting → Fatigued → Disengaged → DisengagedBored → DisengagedExhausted. Culturally-adjusted resilience weights (FOC-012).

**DisengagementClassifier** — Bored vs Fatigued (opposite interventions!):
- Bored_TooEasy → increase difficulty, DON'T offer hints
- Fatigued_Cognitive → take break, offer simpler scaffolding

**ConfusionDetector + ConfusionResolutionTracker** — Adaptive patience window (3-7 questions based on student history). Key insight (D'Mello & Graesser 2012): confusion that resolves leads to deeper learning — **don't interrupt too early**. States: NotConfused → Confused → ConfusionResolving (DON'T intervene) → ConfusionStuck (scaffold).

**Design implication**: Hint/explanation delivery must integrate with ConfusionDetector to avoid interrupting productive struggle.

### Gap 2: SignalR Student Communication Protocol

Student sessions use **bidirectional SignalR WebSocket**, not REST. Contract at `contracts/frontend/signalr-messages.ts`.

Key fields for AI interaction:
- `AnswerEvaluated.explanation` — Markdown+LaTeX string, currently placeholder
- `HintDelivered.hintText` — Markdown+LaTeX string, currently placeholder
- `SubmitAnswer.behavioralSignals` — `{ backspaceCount, answerChangeCount }` (uncertainty indicators)
- `AddAnnotation(kind: 'confusion')` — student flags confusion, natural tutoring entry point

The SignalR contract is ready. The generation service is not.

### Gap 3: Behavioral Signals from Client

`BusConceptAttempt` transmits rich signals beyond correctness:
- `HintCountUsed` — hints consumed before answering
- `BackspaceCount` — answer editing (indicates uncertainty)
- `AnswerChangeCount` — reconsidered answer (indicates confusion between options)

These should feed into L3 explanation personalization.

### Gap 4: Quality Gate LLM Stubs + Annotation Entry Point

`QualityGateService` has 8 dimensions, 3 stubbed at default scores awaiting LLM:
- FactualAccuracy (default 80)
- LanguageQuality (default 80)
- PedagogicalQuality (default 75)

The `ILlmClient` / real provider implementation needed for Tier 1 also unblocks these.

`AddAnnotation(kind: 'confusion' | 'question')` is a natural entry point for Tier 3 conversational tutoring — student explicitly signals they need help.

---

## Verified Build Order

| # | Task | Effort | Depends On | Verified? |
|---|------|--------|-----------|-----------|
| 0 | **Implement real LLM API calls** — `AiGenerationService` has 4 provider stubs returning mock data. Implement real Anthropic SDK calls (Claude Sonnet 4.6 = primary tutoring model). Also unblocks 3 quality gate stubs. | 2-3 days | Nothing | Yes — stubs exist, routing config exists |
| 1a | **Persist L1 explanations** — Add `Explanation` to `QuestionState`, stop discarding AI-generated explanations | 1 day | Nothing | Yes — 6-step path verified against code |
| 1b | **Hint content generation** + BKT credit weighting + confusion-state gating | 2-3 days | Hint infra (exists), ConfusionDetector (exists) | Yes — infrastructure verified |
| 2 | **ErrorType-based L2 explanation cache** — Redis `explain:{qId}:{errorType}` | 3-5 days | Step 0 (real LLM calls) | Yes — ErrorType classification exists |
| 3 | **L3 personalized explanation generation** — Full student context → LLM | 3-4 days | Step 2, ScaffoldingService (exists), MethodologyResolver (exists) | Yes — all inputs verified |
| 4 | **A/B experiment** for Tier 1-2 validation | 1-2 days | FocusExperimentCollector (exists) | Yes — framework operational |
| 5 | **Content extraction pipeline stage** — Add `ContentExtracted` event to pipeline | 5-7 days | Existing pipeline | Yes — pipeline verified |
| 6 | **pgvector + embedding pipeline** | 3-4 days | Step 5, connects to DeduplicationService L3 TODO | Yes — TODO comment verified |
| 7 | **Conversational tutoring** (`TutorActor`) | 7-10 days | Everything above | Yes — all blockers verified |

**Total estimated effort**: ~25-35 days sequential, compressible with parallel tracks:
- Track A (days 1-3): Steps 0 + 1a (independent)
- Track B (days 1-3): Step 1b (independent)
- Track C (days 4-8): Steps 2 + 3 (depends on Track A)
- Track D (day 9): Step 4 (depends on Tracks B + C)
- Track E (days 10-20): Steps 5 + 6 (independent of Tracks C + D)
- Track F (days 21-30): Step 7 (depends on everything)

**Critical path**: ~20-25 days with parallelism.

---

## Open Questions (Prioritized)

### Must Decide Before Building

1. **LLM provider**: Start with Anthropic SDK (Claude Sonnet 4.6 per routing config)? Or implement multiple providers simultaneously? Provider stubs exist for 4 providers.

2. **Confusion-state gating**: Should hint/explanation delivery always respect the ConfusionResolutionTracker patience window (3-7 questions), or can students override with explicit `RequestHint`?

3. **Boredom suppression**: When `DisengagementType == Bored_TooEasy`, suppress hints entirely and increase difficulty? Or still allow student-initiated hints?

### Decide During Implementation

4. **Hint BKT credit curve**: 1.0 → 0.7 → 0.4 → 0.1 modifying P_T. Vary by concept `IntrinsicLoad`?

5. **L2 cache key**: `(questionId, ErrorType)` or finer-grained answer pattern clusters?

6. **Methodology strictness**: Socratic mode never gives the answer, or softens after MCM exhaustion?

7. **Behavioral signal usage**: Feed `BackspaceCount`/`AnswerChangeCount` into L3 explanation personalization?

### Decide Before Tier 3

8. **Content corpus source**: Textbook PDFs through `ContentExtracted` pipeline stage? Teacher content? External?

9. **Cost budget**: Per-student per-month AI budget. L2 cache makes L3 rare (~$0.20/student/month). Tier 3 TBD.

10. **WhatsApp/Telegram tutoring**: Extend `ConversationThreadActor` pattern, or keep tutoring in-app only?

---

## Key Source Files Referenced

| File | Relevance |
|------|-----------|
| `contracts/frontend/signalr-messages.ts` | Student SignalR contract (explanation + hint placeholder fields) |
| `src/api/Cena.Admin.Api/AiGenerationService.cs` | 4 provider stubs (ALL mock data), explanation generated then discarded |
| `src/api/Cena.Admin.Api/QuestionBankService.cs` | `CreateQuestionAsync()` — where explanation is lost |
| `src/actors/Cena.Actors/Questions/QuestionState.cs` | No explanation field (22 properties, gap confirmed) |
| `src/actors/Cena.Actors/Events/QuestionEvents.cs` | `QuestionAiGenerated_V1` — needs `Explanation` field added |
| `src/actors/Cena.Actors/Serving/QuestionPoolActor.cs` | `PublishedQuestion` record — needs `Explanation` field added |
| `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | Hint handling + fatigue + delegation to parent |
| `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` | Full/Partial/HintsOnly/None — ready for LLM prompt construction |
| `src/actors/Cena.Actors/Services/ConfusionDetector.cs` | 4-signal confusion detection, adaptive patience |
| `src/actors/Cena.Actors/Services/DisengagementClassifier.cs` | Bored vs Fatigued (opposite interventions) |
| `src/actors/Cena.Actors/Services/FocusDegradationService.cs` | 8-signal focus model (615 lines) |
| `src/actors/Cena.Actors/Services/CognitiveLoadService.cs` | 3-factor fatigue model |
| `src/actors/Cena.Actors/Gateway/LlmCircuitBreakerActor.cs` | Per-model circuit breaker (Kimi/Sonnet/Opus/Redis) |
| `contracts/llm/routing-config.yaml` | Claude Sonnet 4.6 (tutoring), Kimi K2.5 (structured), task→model |
| `src/api/Cena.Admin.Api/QualityGate/QualityGateService.cs` | 3 LLM-stubbed dimensions awaiting real provider |
| `src/actors/Cena.Actors/Ingest/DeduplicationService.cs` | Level 3 semantic dedup TODO (pgvector/Redis VSS) |
| `src/infra/docker/nats-setup.sh` | 8 JetStream streams (90-day retention) |
| `src/actors/Cena.Actors/Bus/NatsBusMessages.cs` | BusConceptAttempt with BackspaceCount, AnswerChangeCount |
