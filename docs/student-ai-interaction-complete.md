# Student AI Interaction — Implementation Summary

**Completed**: 2026-03-28
**Commit**: `d3066e2` on `main`
**Source Plan**: `.tasks/student-ai-interaction/`

---

## What Was Built

A full student-facing AI interaction layer for the Cena educational platform, spanning 8 tasks across 6 parallel tracks. The system takes what Cena already knows about each student (BKT mastery, Bloom level, error patterns, confusion state, fatigue, methodology) and uses it to personalize every response — not just question selection.

---

## Summary Table

| Task | Track | Description | Key Files | Status |
|------|-------|-------------|-----------|--------|
| **00** | A | Real Anthropic SDK calls | `AiGenerationService.cs`, `QualityGateService.cs` | Done — SDK already integrated; switched QualityGate to Haiku (3x cost reduction) |
| **01a** | A | Persist L1 explanations | `index.vue` (Vue frontend) | Done — backend chain was wired; fixed 3 breaks in Vue form/autofill/submit |
| **01b** | B | Hint content + BKT credit + confusion gating | `HintGenerationService.cs`, `LearningSessionActor.cs`, `BktTracer.cs` | Done — services existed; **fixed** `_confusionWindowQuestions` never being updated |
| **02** | C | L2 error-type explanation cache | `ExplanationCacheService.cs`, `ErrorClassificationService.cs`, `ExplanationOrchestrator.cs` | Done — Redis cache `explain:{qId}:{errorType}:{lang}`, 30-day TTL, 52 tests |
| **03** | C | L3 personalized explanations | `L3ExplanationGenerator.cs`, `PersonalizedExplanationService.cs`, `ExplanationOrchestrator.cs` | Done — fixed escalation gate, added methodology mappings, enabled prompt caching, 50 tests |
| **04** | D | A/B experiment configuration | `FocusExperimentConfig.cs`, `FocusExperimentCollector.cs` | Done — 3 experiments (explanation_tiers 4-arm, hint_bkt_credit 3-arm, confusion_gating 3-arm), opt-in only, 6 tests |
| **05** | E | Content extraction pipeline | `ContentExtractorService.cs`, `PipelineItemDocument.cs`, `IngestionOrchestrator.cs` | Done — added type counts, unlinked fallback, Hebrew/Arabic markers, token enforcement (50-500) |
| **06** | E | pgvector + embedding pipeline | `EmbeddingService.cs`, `EmbeddingIngestionHandler.cs`, `PgVectorMigrationService.cs` | Done — **fixed** NATS subject mismatch, added `EmbedBatchAsync`/`SearchSimilarAsync`, HNSW index, 27 tests |
| **07** | F | Conversational TutorActor | `TutorActor.cs` (836 lines), `TutorPromptBuilder.cs`, `TutoringEvents.cs`, `signalr-messages.ts` | Done — 4 entry points, RAG retrieval, methodology enforcement, 10-turn cap, 5-min timeout, budget guard, safety guardrails |

---

## Architecture

```
Student (SignalR WebSocket)
  |
  +-- SubmitAnswer --> LearningSessionActor
  |                     +-- Error Classification --> ErrorClassificationService (Haiku)
  |                     +-- L2 Cache Check ------> ExplanationCacheService (Redis)
  |                     +-- L3 Personalization ---> L3ExplanationGenerator (Sonnet, prompt cached)
  |                     +-- BKT Update ----------> HintAdjustedBktService (credit multiplier)
  |                     +-- Confusion Detection --> ConfusionDetector + DeliveryGate
  |
  +-- RequestHint --> LearningSessionActor
  |                     +-- Confusion Gating -----> DeliveryGate (suppress during ConfusionResolving)
  |                     +-- Disengagement Check --> DisengagementClassifier
  |                     +-- Hint Generation ------> HintGenerationService (template-based, 3 levels)
  |
  +-- AddAnnotation --> LearningSessionActor
  |   (confusion/       +-- Spawn TutorActor (child)
  |    question)             +-- RAG Retrieval ---------> EmbeddingService (pgvector)
  |                          +-- Methodology Enforcement > TutorPromptBuilder
  |                          +-- Safety Guardrails ------> TutorSafetyGuard
  |                          +-- Multi-turn Dialogue ----> ILlmClient (Sonnet, circuit breaker)
  |
  +-- TutorMessage --> LearningSessionActor --> TutorActor (conversation continues)

Ingestion Pipeline (Admin):
  Upload --> OCR --+--> QuestionSegmenter (existing)
                   +--> ContentExtractorService (NEW)
                          +--> EmbeddingIngestionHandler --> pgvector
```

---

## Explanation Layer (L1 / L2 / L3)

| Layer | What | Cache | Cost | Hit Rate |
|-------|------|-------|------|----------|
| **L1** | Static AI-generated explanation per question | Marten aggregate | $0 (pre-generated) | 100% (always available) |
| **L2** | Error-type-specific explanation per (question, errorType, language) | Redis, 30-day TTL | ~$0.003/miss | ~80-90% after warm-up |
| **L3** | Personalized explanation using full student context | Not cached (ephemeral) | ~$0.01/call | Triggered only on high uncertainty |

**Fallback chain**: L3 -> L2 -> L1 -> generic message. Never leaves the student without an explanation.

---

## Key Bug Fixes

| Bug | Impact | Fix |
|-----|--------|-----|
| `_confusionWindowQuestions` never incremented | ConfusionDetector could never reach ConfusionResolving/ConfusionStuck — gating was dead code | Increment counters when in confused state, reset on resolution |
| EmbeddingIngestionHandler NATS subject mismatch | Embeddings never triggered after content extraction — RAG corpus stayed empty | Changed to match IngestionOrchestrator publish subject |
| QualityGateService using Sonnet for assessment | 3x higher cost than necessary | Switched to Haiku |
| Vue frontend dropping explanation in 3 places | AI-generated explanations produced then discarded — L1 layer was empty | Added explanation to form state, autofill, and submit body |

---

## A/B Experiments (All Opt-In, Not Active)

| Experiment | Arms | Primary Metric | Purpose |
|-----------|------|----------------|---------|
| `sai-explanation-tiers` | 4: control (10%), L1, L2, L3 | mastery_gain | Measure learning impact of explanation personalization |
| `sai-hint-bkt-credit` | 3: aggressive, moderate, lenient | mastery_accuracy | Validate hint credit curve |
| `sai-confusion-gating` | 3: patience, immediate, student_choice | confusion_resolution_rate | Test whether respecting productive struggle improves outcomes |

Activate via `FocusExperimentConfig` start date (currently 2099-01-01).

---

## Test Coverage

| Area | Tests | File |
|------|-------|------|
| Explanation cache + orchestrator | 52 | ExplanationCacheServiceTests, ErrorClassificationServiceTests, ExplanationOrchestratorTests |
| L3 personalization | 50 | PersonalizedExplanationServiceTests + orchestrator tests |
| Embeddings | 27 | EmbeddingServiceTests |
| A/B experiments | 6 | FocusExperimentTests |
