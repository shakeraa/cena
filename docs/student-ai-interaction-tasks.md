# Student AI Interaction — Implementation Task Plan

**Source**: `docs/autoresearch-student-ai-interaction.md`
**Date**: 2026-03-28
**Status**: Planning — awaiting approval before execution

---

## Architecture Overview

This plan implements student-facing AI tutoring in 8 sequential steps (with parallel tracks).
The system is an event-sourced .NET platform using Proto.Actor, Marten (PostgreSQL), NATS JetStream,
and SignalR for real-time student communication.

**Key constraint**: The codebase already has extensive infrastructure (hint events, confusion detection,
scaffolding levels, A/B framework, circuit breakers, LLM routing config) — most tasks wire existing
pieces together rather than building from scratch.

**Parallel tracks** (compressible from ~30 days to ~20-25 days):
- Track A (days 1-3): Task 0 + Task 1a (independent)
- Track B (days 1-3): Task 1b (independent)
- Track C (days 4-8): Task 2 + Task 3 (depends on Track A)
- Track D (day 9): Task 4 (depends on B + C)
- Track E (days 10-20): Task 5 + Task 6 (independent of C + D)
- Track F (days 21-30): Task 7 (depends on everything)

---

## Task 0: Implement Real LLM API Calls (Anthropic SDK)

**Effort**: 2-3 days
**Depends on**: Nothing
**Track**: A (parallel with Task 1a)

### Objective

Replace the 4 mock provider stubs in `AiGenerationService` with real Anthropic SDK calls.
Claude Sonnet 4.6 is the primary tutoring model per `contracts/llm/routing-config.yaml`.
This also unblocks 3 stubbed quality gate dimensions in `QualityGateService`.

### Files to Read First (mandatory — understand before changing)

| File | Why |
|------|-----|
| `src/api/Cena.Admin.Api/AiGenerationService.cs` | 4 provider stubs (lines 327-362), all return `GenerateMockResponse()`. The `AiGeneratedQuestion` DTO already has `Explanation` field (line 91) |
| `src/api/Cena.Admin.Api/QualityGate/QualityGateService.cs` | 3 dimensions stubbed at default scores (FactualAccuracy=80, LanguageQuality=80, PedagogicalQuality=75) awaiting real LLM |
| `contracts/llm/routing-config.yaml` | Full model config: Sonnet 4.6 = primary tutoring, Kimi K2 = structured tasks, Haiku = fallback. Rate limits, cost caps, circuit breaker thresholds, prompt caching strategies |
| `src/actors/Cena.Actors/Gateway/LlmCircuitBreakerActor.cs` | Per-model circuit breaker — understand the existing resilience layer before adding HTTP calls |

### Architectural Requirements

1. **Anthropic SDK only for now** — do NOT implement Kimi/OpenAI/Azure. Start with the primary tutoring model (Claude Sonnet 4.6). Keep the other stubs as-is but make them throw `NotImplementedException` with a clear message instead of returning mock data silently.

2. **Use the official Anthropic .NET SDK** (`Anthropic` NuGet package). Do NOT hand-roll HTTP calls.

3. **Respect the routing config contract**:
   - Temperature, max_tokens per task type from `routing-config.yaml`
   - Implement prompt caching: system prompts use `cache_control: { type: "ephemeral" }` per section 6 of the config
   - API key from `IConfiguration` (never hardcoded)

4. **Wire into existing circuit breaker** — `LlmCircuitBreakerActor` already manages per-model state. The new SDK calls must flow through it.

5. **PII handling** — Anthropic is a trusted provider (`is_trusted_provider: true`), so no PII stripping needed. But the interface must support it for when Kimi is added later.

6. **Structured output** — The response must deserialize into `AiGeneratedQuestion` (with `Explanation`). Use Anthropic's tool_use/JSON mode for reliable structured output.

7. **Quality gate integration** — After implementing real LLM calls, update `QualityGateService` to use LLM for the 3 stubbed dimensions. The LLM evaluates factual accuracy, language quality, and pedagogical quality of generated questions.

8. **Observability** — Every call must emit the metrics defined in `routing-config.yaml` section 9: `llm_request_duration_ms`, `llm_tokens_total`, `llm_cost_usd`.

### What NOT to Do

- Do NOT add a new abstraction layer or "LLM provider factory" — the existing `AiGenerationService` with its switch on `AiProvider` is the correct level of abstraction for now
- Do NOT implement streaming — question generation is a batch operation, not interactive
- Do NOT modify `routing-config.yaml` — it's the contract; code conforms to it
- Do NOT add retry logic inside the service — the circuit breaker actor handles that

### Verification

- Generate 3 questions via the Admin API with a real API key — confirm `Explanation` field is populated
- Confirm quality gate runs with real LLM scores (not defaults) for a generated question
- Confirm circuit breaker trips after 5 simulated failures (per default config)
- `dotnet build` succeeds, `dotnet test` passes

---

## Task 1a: Persist L1 Explanations

**Effort**: 1 day
**Depends on**: Nothing
**Track**: A (parallel with Task 0)

### Objective

Stop discarding AI-generated explanations. Currently `AiGeneratedQuestion.Explanation` is returned
from generation but never stored in the event or aggregate. This is the single cheapest win in the
entire plan.

### Files to Read First (mandatory)

| File | Why |
|------|-----|
| `src/actors/Cena.Actors/Events/QuestionEvents.cs` | `QuestionAiGenerated_V1` (line 57) — needs `Explanation` field added |
| `src/actors/Cena.Actors/Questions/QuestionState.cs` | `AiGenerationState` record (line 28) — needs `Explanation` field |
| `src/actors/Cena.Actors/Serving/QuestionPoolActor.cs` | `PublishedQuestion` record (line 157) — needs `Explanation` field |
| `src/api/Cena.Admin.Api/QuestionBankService.cs` | `CreateQuestionAsync()` — where explanation is currently lost |
| `src/api/Cena.Admin.Api/AiGenerationService.cs` | `AiGeneratedQuestion` DTO (line 85) — already HAS `Explanation` (line 91) |
| `contracts/frontend/signalr-messages.ts` | `AnswerEvaluated.explanation` — already a placeholder field, will receive real data |

### Exact 6-Step Implementation

1. **Add `string? Explanation` to `QuestionAiGenerated_V1` event** (in `QuestionEvents.cs`)
   - Add as the last parameter before `DateTimeOffset Timestamp` to maintain positional record compatibility
   - Nullable because existing events in the store won't have it (Marten handles missing fields as null)

2. **Update `AiGenerationState` in `QuestionState.cs`** — add `string? Explanation` field

3. **Update `QuestionState.Apply(QuestionAiGenerated_V1)`** — store `Explanation` in the `AiGenerationState`

4. **Add `string? Explanation` to `PublishedQuestion` record** in `QuestionPoolActor.cs`

5. **Hydrate in `QuestionPoolActor.InitializeAsync()`** — the Marten read-model query that populates `PublishedQuestion` must include the explanation. Check how the current query maps fields.

6. **Update `QuestionBankService.CreateQuestionAsync()`** — pass `Explanation` from `AiGeneratedQuestion` DTO through to the `QuestionAiGenerated_V1` event constructor

### Architectural Requirements

- **Event versioning** — this is an additive change (new nullable field). Do NOT create `QuestionAiGenerated_V2`. Marten handles missing fields gracefully by defaulting to null.
- **Backward compatibility** — all existing questions in the DB will have `Explanation = null`. UI must handle this gracefully (show "No explanation available" or hide the section).
- **No UI changes in this task** — this task is backend only. The SignalR contract already has the placeholder field.

### What NOT to Do

- Do NOT create a V2 event — additive nullable fields on records are safe in Marten
- Do NOT modify the `QuestionAuthored_V1` or `QuestionIngested_V1` events — those creation paths don't have AI explanations
- Do NOT add explanation to the Admin API response DTOs yet — that's a separate concern

### Verification

- Create a question via AI generation — verify the `QuestionAiGenerated_V1` event in PostgreSQL contains the explanation text
- Query the aggregate — verify `QuestionState.AiGeneration.Explanation` is populated
- Verify `PublishedQuestion` in `QuestionPoolActor` carries the explanation
- Verify existing questions (pre-change) still load with `Explanation = null`
- `dotnet build` succeeds, `dotnet test` passes

---

## Task 1b: Hint Content Generation + BKT Credit + Confusion Gating

**Effort**: 2-3 days
**Depends on**: Nothing (infrastructure exists)
**Track**: B (parallel with Track A)

### Objective

Wire hint content generation into the existing hint infrastructure. Currently:
- `RequestHintMessage` is handled by `LearningSessionActor`
- `HintRequested_V1` event is emitted with `HintLevel: 1|2|3`
- `HintDelivered` SignalR event sends `hintText` (placeholder) and `hasMoreHints`
- `ScaffoldingService` determines max hints: Full=3, Partial=2, HintsOnly=1, None=0

What's missing: actual hint text generation, BKT credit adjustment, confusion-state gating.

### Files to Read First (mandatory)

| File | Why |
|------|-----|
| `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | Handles `RequestHintMessage`, emits `HintRequested_V1`, sends `HintDelivered` via SignalR |
| `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` | Full/Partial/HintsOnly/None levels — determines max hints per concept |
| `src/actors/Cena.Actors/Services/ConfusionDetector.cs` | 4-state machine: NotConfused → Confused → ConfusionResolving → ConfusionStuck |
| `src/actors/Cena.Actors/Services/DisengagementClassifier.cs` | Bored_TooEasy vs Fatigued_Cognitive — opposite interventions |
| `src/actors/Cena.Actors/Services/CognitiveLoadService.cs` | 3-factor fatigue model — Low/Moderate/High/Critical |
| `src/actors/Cena.Actors/Bus/NatsBusMessages.cs` | `BusConceptAttempt.HintCountUsed` — already transmitted from client |
| `contracts/frontend/signalr-messages.ts` | `HintDelivered.hintText` — Markdown+LaTeX string, currently placeholder |

### Architectural Requirements

1. **Hint content generation** — Create a `HintGenerationService` in `src/actors/Cena.Actors/Services/`. Hints are NOT free-form LLM calls. They follow a 3-level progressive disclosure:
   - Level 1: Conceptual nudge ("Think about what happens when...")
   - Level 2: Procedural hint ("The formula you need is...")
   - Level 3: Worked example with one step hidden

   For now (before Task 0 completes): use template-based hints derived from the question's `DistractorRationale` and concept metadata. When real LLM is available, upgrade to generated hints.

2. **BKT credit adjustment** — When a student uses hints before answering correctly, reduce the P(T) credit:
   - 0 hints → P(T) multiplier = 1.0 (full credit)
   - 1 hint → 0.7
   - 2 hints → 0.4
   - 3 hints → 0.1
   - The multiplier modifies the `P_T` (probability of transition to learned) in the existing BKT update. Find where BKT updates happen and apply the multiplier.

3. **Confusion-state gating** — Before delivering a hint, check `ConfusionDetector` state:
   - `ConfusionResolving` → DO NOT deliver hint automatically. The student is in productive struggle (D'Mello & Graesser 2012). Only deliver if student explicitly requests via `RequestHint`.
   - `ConfusionStuck` → Proactively offer hint (push via SignalR)
   - `NotConfused` → Normal hint flow on request

4. **Boredom suppression** — Check `DisengagementClassifier`:
   - `Bored_TooEasy` → Suppress automatic hints, increase difficulty instead
   - `Fatigued_Cognitive` → Offer simpler scaffolding, reduce hint complexity

5. **Integration point** — `LearningSessionActor` orchestrates: it already handles `RequestHintMessage`. Add the confusion check and content generation there. Do NOT create a new actor for this.

### What NOT to Do

- Do NOT make LLM calls for hints in this task — use templates until Task 0 is done
- Do NOT modify `ConfusionDetector` or `DisengagementClassifier` — they work correctly, just integrate with them
- Do NOT change the SignalR contract — `HintDelivered` already has the right shape
- Do NOT change `ScaffoldingService` — it already determines max hints correctly

### Verification

- Request a hint for a concept where scaffolding=Full — verify 3 levels of progressive hints
- Request a hint while `ConfusionDetector` state is `ConfusionResolving` — verify hint is suppressed (unless explicit student request)
- Answer correctly after 2 hints — verify BKT credit is reduced by 0.4 multiplier
- Verify `BusConceptAttempt.HintCountUsed` matches the actual hints delivered
- `dotnet build` succeeds, `dotnet test` passes

---

## Task 2: ErrorType-Based L2 Explanation Cache

**Effort**: 3-5 days
**Depends on**: Task 0 (real LLM calls)
**Track**: C

### Objective

Build a Redis-cached explanation layer: when a student gets a question wrong, classify the error
type, then serve a cached explanation for that `(questionId, errorType)` pair. If no cached
explanation exists, generate one via LLM and cache it.

### Files to Read First (mandatory)

| File | Why |
|------|-----|
| `src/api/Cena.Admin.Api/AiGenerationService.cs` | Now has real LLM calls (from Task 0) |
| `contracts/frontend/signalr-messages.ts` | `AnswerEvaluated.explanation` — where the explanation is delivered |
| `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | Where answer evaluation happens — integration point |
| `contracts/llm/routing-config.yaml` | `error_classification` task type → Kimi K2 primary, fallback to Haiku |

### Architectural Requirements

1. **Error classification** — Before generating an explanation, classify the error type. The routing config maps `error_classification` to Kimi K2 (6.7x cheaper than Sonnet). Error types should be domain-specific:
   - `conceptual_misunderstanding` — wrong mental model
   - `procedural_error` — right concept, wrong execution
   - `careless_mistake` — understood but sloppy
   - `guessing` — no evidence of understanding
   - `partial_understanding` — close but missing a key step

2. **Cache key**: `explain:{questionId}:{errorType}:{language}` in Redis. TTL: 30 days (explanations don't expire quickly).

3. **Cache miss flow**: On cache miss, generate explanation via LLM using Sonnet 4.6 (`answer_evaluation` task type), store in Redis, return to student.

4. **Cache hit flow**: On cache hit, return immediately — no LLM call. This is the cost optimization that makes L3 affordable (~$0.20/student/month).

5. **Place the service in `src/actors/Cena.Actors/Services/ExplanationCacheService.cs`** — it's a domain service, not an API concern.

6. **Wire into `LearningSessionActor`** — after answer evaluation, if incorrect, classify error → check cache → generate or return cached → deliver via `AnswerEvaluated.explanation`.

### What NOT to Do

- Do NOT implement Kimi API client yet — use Haiku fallback for error classification until Kimi is implemented
- Do NOT use in-memory cache — Redis is the correct choice for shared state across actor instances
- Do NOT cache personalized explanations — only cache generic per-errortype explanations (L3 personalization is Task 3)

### Verification

- Submit a wrong answer → verify error classification runs → verify explanation generated and cached in Redis
- Submit the same wrong answer type for the same question → verify cache hit (no LLM call)
- Verify different error types for the same question produce different cached explanations
- `dotnet build` succeeds, `dotnet test` passes

---

## Task 3: L3 Personalized Explanation Generation

**Effort**: 3-4 days
**Depends on**: Task 2, ScaffoldingService (exists), MethodologyResolver (exists)
**Track**: C (after Task 2)

### Objective

Generate personalized explanations that incorporate the student's full learning context:
mastery level, scaffolding mode, active methodology, confusion state, and behavioral signals.

### Files to Read First (mandatory)

| File | Why |
|------|-----|
| `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` | Scaffolding level determines explanation depth |
| `src/actors/Cena.Actors/Services/ConfusionDetector.cs` | Confusion state affects explanation approach |
| `src/actors/Cena.Actors/Services/DisengagementClassifier.cs` | Bored vs Fatigued affects tone and complexity |
| `src/actors/Cena.Actors/Bus/NatsBusMessages.cs` | `BackspaceCount`, `AnswerChangeCount` — uncertainty indicators |
| `contracts/llm/routing-config.yaml` | `answer_evaluation` and `socratic_question` task types |
| `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | Integration point |

### Architectural Requirements

1. **Prompt context assembly** — Build a student context block for the LLM prompt:
   - Current mastery level (P_L from BKT)
   - Scaffolding mode (Full/Partial/HintsOnly/None)
   - Active methodology (from MethodologyResolver)
   - Confusion state (from ConfusionDetector)
   - Behavioral signals: `BackspaceCount` (uncertainty), `AnswerChangeCount` (option confusion)
   - Number of hints used
   - Error type (from Task 2's classification)

2. **Methodology-aware explanations**:
   - Socratic mode → explanation is a guided question, never reveals the answer directly
   - Direct instruction → full step-by-step solution
   - Worked examples → show similar problem with solution, let student apply to original

3. **Use Anthropic prompt caching** — student context changes per-session but not per-question. Use the `student_context` caching strategy (5-min TTL, `cache_control: { type: "ephemeral" }`) from `routing-config.yaml` section 6.

4. **Fallback to L2** — If LLM call fails (circuit breaker open, timeout, budget exhausted), fall back to the cached L2 explanation from Task 2. Never leave the student with no explanation.

5. **Cost guard** — Check per-student daily budget (`daily_output_token_limit: 25000`) before making L3 calls. If budget exhausted, serve L2 cached explanation instead.

### What NOT to Do

- Do NOT bypass the circuit breaker — all LLM calls go through `LlmCircuitBreakerActor`
- Do NOT build a separate prompt template system — use inline prompt construction with the assembled context
- Do NOT store personalized explanations in Redis — they're ephemeral, one-time-use

### Verification

- Submit a wrong answer with high uncertainty signals (high backspace count) → verify explanation acknowledges uncertainty
- Submit a wrong answer in Socratic mode → verify explanation asks guiding questions, doesn't reveal answer
- Exhaust the daily token budget → verify fallback to L2 cached explanation
- `dotnet build` succeeds, `dotnet test` passes

---

## Task 4: A/B Experiment for Tier 1-2 Validation

**Effort**: 1-2 days
**Depends on**: Tasks 1b, 2, 3
**Track**: D

### Objective

Configure A/B experiments to measure the learning impact of L1-L3 explanations and hint generation.

### Files to Read First (mandatory)

| File | Why |
|------|-----|
| `src/actors/Cena.Actors/Services/FocusDegradationService.cs` | Contains `FocusExperimentConfig` with 6 predefined experiments |
| Look for `FocusExperimentCollector` | Per-student per-session metric capture |

### Architectural Requirements

1. **Define new experiment configs** for:
   - `explanation_tiers` — control (no explanation) vs L1 (static) vs L2 (error-typed) vs L3 (personalized)
   - `hint_bkt_credit` — test different credit curves (1.0/0.7/0.4/0.1 vs 1.0/0.8/0.5/0.2)
   - `confusion_gating` — respect confusion patience window vs always deliver hints immediately

2. **Use existing infrastructure** — `FocusExperimentCollector` already captures per-student metrics with hash-based deterministic arm assignment. Add new experiment configs following the same pattern.

3. **Metrics to capture**: mastery gain (delta P_L), time-to-mastery, hint usage rate, explanation view rate, session length, engagement score.

4. **Export format**: CSV/Parquet for offline analysis (already supported).

### What NOT to Do

- Do NOT build a custom A/B framework — use the existing `FocusExperimentConfig` pattern
- Do NOT activate experiments in production without explicit approval

### Verification

- Verify new experiment configs are registered
- Verify hash-based arm assignment is deterministic (same student always gets same arm)
- Verify metrics are captured per-student per-session
- `dotnet build` succeeds, `dotnet test` passes

---

## Task 5: Content Extraction Pipeline Stage

**Effort**: 5-7 days
**Depends on**: Existing ingest pipeline
**Track**: E (independent of Tasks 2-4)

### Objective

Add a `ContentExtracted` event to the ingestion pipeline that extracts semantic content blocks
from source documents (textbook PDFs, teacher materials). This feeds into the embedding pipeline
(Task 6) for semantic search and RAG-based tutoring (Task 7).

### Files to Read First (mandatory)

| File | Why |
|------|-----|
| Explore `src/actors/Cena.Actors/Ingest/` | Existing ingestion pipeline — understand the event flow |
| `src/actors/Cena.Actors/Ingest/DeduplicationService.cs` | Has Level 3 semantic dedup TODO comment referencing pgvector/Redis VSS |

### Architectural Requirements

1. **`ContentExtracted` event** — new domain event in the ingestion pipeline:
   - `ContentBlockId`, `SourceDocId`, `ContentType` (definition, theorem, example, exercise, explanation)
   - `RawText`, `ProcessedText` (cleaned, structured)
   - `ConceptIds` (linked concepts), `Language`, `PageRange`

2. **Chunking strategy** — split documents into semantically meaningful blocks (not arbitrary token windows). Use heading structure, paragraph breaks, and mathematical notation boundaries.

3. **Metadata enrichment** — each block tagged with concept IDs from the existing concept graph.

4. **Event-sourced** — `ContentExtracted` is stored as a Marten event for audit trail and reprocessing.

### What NOT to Do

- Do NOT implement embeddings in this task — that's Task 6
- Do NOT modify the existing OCR pipeline (`GeminiOcrClient`) — add a new stage after OCR

### Verification

- Ingest a sample PDF → verify `ContentExtracted` events are emitted with correct content blocks
- Verify concept linking produces correct concept IDs
- `dotnet build` succeeds, `dotnet test` passes

---

## Task 6: pgvector + Embedding Pipeline

**Effort**: 3-4 days
**Depends on**: Task 5
**Track**: E (after Task 5)

### Objective

Add vector embeddings to extracted content blocks for semantic search. Uses pgvector (PostgreSQL
extension) — no new infrastructure needed since the platform already runs PostgreSQL.

### Files to Read First (mandatory)

| File | Why |
|------|-----|
| `src/actors/Cena.Actors/Ingest/DeduplicationService.cs` | Level 3 semantic dedup TODO — this task fulfills that TODO |
| PostgreSQL connection config | Verify pgvector extension can be enabled |

### Architectural Requirements

1. **pgvector extension** — enable in PostgreSQL. Store embeddings in a dedicated table:
   - `content_embeddings(id, content_block_id, embedding vector(1536), concept_ids, language, created_at)`

2. **Embedding model** — use Anthropic's embedding API or a dedicated embedding model. Cost-optimize: embed once, search many times.

3. **Similarity search** — cosine similarity for finding relevant content blocks given a student's question or confusion context.

4. **Integration with DeduplicationService** — fulfill the Level 3 semantic dedup TODO. Near-duplicate detection using embedding similarity threshold.

5. **Index** — create an IVFFlat or HNSW index on the embedding column for sub-100ms search at scale.

### What NOT to Do

- Do NOT add Redis VSS — pgvector is sufficient and avoids adding another data store
- Do NOT embed at query time — pre-compute embeddings during ingestion

### Verification

- Ingest content → verify embeddings stored in pgvector
- Search for similar content → verify relevant results returned with cosine similarity > 0.8
- Verify deduplication catches near-duplicate content blocks
- `dotnet build` succeeds, `dotnet test` passes

---

## Task 7: Conversational Tutoring (TutorActor)

**Effort**: 7-10 days
**Depends on**: All previous tasks
**Track**: F

### Objective

Build a `TutorActor` that manages multi-turn conversational tutoring sessions. This is the
culmination of all previous work: real LLM calls, explanations, hints, confusion detection,
behavioral signals, content retrieval, and A/B testing.

### Files to Read First (mandatory)

| File | Why |
|------|-----|
| All files from previous tasks | Full context required |
| `contracts/frontend/signalr-messages.ts` | `AddAnnotation(kind: 'confusion' | 'question')` — natural entry point for tutoring |
| `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | Parent actor that delegates to TutorActor |
| `contracts/llm/routing-config.yaml` | `socratic_question` task type → Sonnet 4.6, temp 0.4 |

### Architectural Requirements

1. **`TutorActor`** — a Proto.Actor virtual actor (per-student, per-session). Managed by `LearningSessionActor`.

2. **Entry points**:
   - `AddAnnotation(kind: 'confusion')` — student flags confusion
   - `AddAnnotation(kind: 'question')` — student asks a question
   - Automatic trigger when `ConfusionDetector` reaches `ConfusionStuck` state
   - Post-wrong-answer follow-up (after L2/L3 explanation delivered)

3. **Conversation state** — maintain a conversation thread (list of turns) within the actor. Do NOT persist to event store — conversation is ephemeral within the session.

4. **RAG integration** — use pgvector (Task 6) to retrieve relevant content blocks for the current concept, inject into LLM context.

5. **Methodology enforcement** — `MethodologyResolver` determines the tutoring style:
   - Socratic → ask questions, never give answers
   - Direct instruction → explain step by step
   - Worked examples → show analogous solved problem

6. **Session budget** — enforce per-student daily token budget. Show degraded tutoring (L2 cached explanations) when budget exhausted.

7. **Guardrails**:
   - Max 10 turns per tutoring episode
   - Off-topic detection — if student goes off-topic, redirect to the current concept
   - Safety — no personal advice, no opinions, only educational content

### What NOT to Do

- Do NOT build WhatsApp/Telegram integration — keep tutoring in-app only for now
- Do NOT persist conversation history to the event store — it's ephemeral
- Do NOT create a new SignalR hub — use the existing student session hub

### Verification

- Trigger tutoring via confusion annotation → verify multi-turn conversation
- Verify Socratic mode asks questions without revealing answers
- Verify budget enforcement — exhaust budget, verify degraded mode
- Verify max turn limit — after 10 turns, verify session ends gracefully
- `dotnet build` succeeds, `dotnet test` passes

---

## Open Decisions (Must Resolve Before Starting)

These are the "Must Decide Before Building" questions from the autoresearch report.
**No code should be written until these are answered.**

| # | Question | Impact | My Recommendation |
|---|----------|--------|-------------------|
| 1 | Start with Anthropic SDK only or multiple providers? | Task 0 scope | Anthropic only. Kimi later. Provider stubs stay as `NotImplementedException`. |
| 2 | Confusion gating: always respect patience window, or allow student override via explicit `RequestHint`? | Task 1b behavior | Allow student override — explicit `RequestHint` bypasses confusion patience. Automatic hints respect it. |
| 3 | Boredom suppression: suppress hints entirely when `Bored_TooEasy`? | Task 1b behavior | Suppress automatic hints but allow student-initiated. Increase difficulty. |

---

## Notes for All Agents

- **Event sourcing is law** — every state change is an event. No direct state mutation.
- **Marten is the event store** — use `AggregateStreamAsync<T>()` for reads, `IDocumentSession.Events.Append()` for writes.
- **Proto.Actor virtual actors** — per-student actors. Do NOT create singleton services for per-student state.
- **SignalR for student communication** — REST for admin, SignalR for students. No GraphQL (rejected per project decision).
- **NATS JetStream** — 8 streams configured, 90-day retention. Use for inter-service communication.
- **Circuit breaker** — all LLM calls go through `LlmCircuitBreakerActor`. No direct HTTP calls to LLM providers.
- **Hebrew/Arabic support** — all text content must handle RTL. Explanations and hints must be language-aware.
- **File size limit** — keep files under 500 lines. Split if approaching limit.
- **Tests** — TDD London School (mock-first). Write tests before implementation.
- **Build verification** — every task ends with `dotnet build` + `dotnet test` passing.
