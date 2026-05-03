# Student AI Interaction — Task Index

**Source**: `docs/autoresearch-student-ai-interaction.md` (Codebase Validation Report)
**Date**: 2026-03-28
**Architect**: Lead Senior Architect review
**Status**: Ready for implementation

---

## Overview

9 tasks across 6 parallel tracks to bring AI-powered student interaction into Cena. Builds on validated codebase analysis — every task references real files, real types, and verified integration points.

**Total estimated effort**: 25-35 days sequential, ~20-25 days with parallelism.

---

## Tasks

| ID | Task | Effort | Priority | Track |
|----|------|--------|----------|-------|
| [SAI-00](TASK-SAI-00-llm-provider.md) | Implement real LLM provider calls (Anthropic SDK) | 2-3d | CRITICAL | A |
| [SAI-01](TASK-SAI-01-persist-explanations.md) | Persist L1 explanations (stop discarding AI content) | 1d | HIGH | A |
| [SAI-02](TASK-SAI-02-hint-content-generation.md) | Hint content generation + BKT credit + confusion gating | 2-3d | HIGH | B |
| [SAI-03](TASK-SAI-03-l2-explanation-cache.md) | L2 ErrorType-based explanation cache (Redis) | 3-5d | HIGH | C |
| [SAI-04](TASK-SAI-04-l3-personalized-explanations.md) | L3 personalized explanation generation (full context) | 3-4d | HIGH | C |
| [SAI-05](TASK-SAI-05-ab-experiment.md) | A/B experiment wiring for Tier 1-2 validation | 1-2d | MEDIUM | D |
| [SAI-06](TASK-SAI-06-content-extraction.md) | Content extraction pipeline stage | 5-7d | MEDIUM | E |
| [SAI-07](TASK-SAI-07-pgvector-embeddings.md) | pgvector + embedding pipeline | 3-4d | MEDIUM | E |
| [SAI-08](TASK-SAI-08-tutor-actor.md) | Conversational tutoring (TutorActor) | 7-10d | LOW | F |

---

## Dependency Graph

```text
Track A (days 1-3):     SAI-00 (LLM SDK) ──┐      SAI-01 (persist explanations)
                                             │
Track B (days 1-3):     SAI-02 (hints) ─────┤
                                             │
Track C (days 4-8):     SAI-03 (L2 cache) ──┴── SAI-04 (L3 personalized)
                                                       │
Track D (day 9):        SAI-05 (A/B experiments) ◄─────┘
                                                       │
Track E (days 10-20):   SAI-06 (content extraction) ── SAI-07 (pgvector)
                                                              │
Track F (days 21-30):   SAI-08 (TutorActor) ◄────────────────┘
```

### Parallel Tracks

- **Track A + B**: Independent — start immediately, run in parallel
- **Track C**: Depends on SAI-00 completing (needs real LLM calls)
- **Track D**: Depends on Tracks B + C (needs hints + explanations to experiment on)
- **Track E**: Independent of C + D — start as soon as resources available
- **Track F**: Depends on everything — build last

### Critical Path

SAI-00 → SAI-03 → SAI-04 → SAI-05 (validation) then SAI-06 → SAI-07 → SAI-08

---

## Architecture Principles

Every task enforces:

1. **Event-sourced state changes** — all new data flows through Marten events with `_V1` versioned records
2. **Actor model** — stateful components are Proto.Actor grains; stateless logic is `sealed class` services
3. **Circuit breaker for all LLM calls** — through existing `LlmCircuitBreakerActor`
4. **No PII in prompts** — student context is anonymized (mastery level, not student ID)
5. **Methodology enforcement** — every student-facing AI output respects the active methodology (Socratic, Feynman, etc.)
6. **Confusion-state awareness** — don't interrupt productive struggle (D'Mello & Graesser 2012)
7. **Multi-language** — all generated content in student's preferred language (he/ar/en)
8. **Markdown + LaTeX** — consistent with SignalR contract for rendering
9. **Existing infrastructure first** — hint events, scaffolding service, experiment framework, JetStream streams all exist. Build on them, don't rebuild.

---

## Key Files Referenced Across Tasks

| File | Used By |
|------|---------|
| `contracts/llm/routing-config.yaml` | SAI-00 |
| `contracts/frontend/signalr-messages.ts` | SAI-01, SAI-02, SAI-08 |
| `src/api/Cena.Admin.Api/AiGenerationService.cs` | SAI-00, SAI-01 |
| `src/api/Cena.Admin.Api/QuestionBankService.cs` | SAI-01 |
| `src/api/Cena.Admin.Api/QualityGate/QualityGateService.cs` | SAI-00 |
| `src/actors/Cena.Actors/Questions/QuestionState.cs` | SAI-01 |
| `src/actors/Cena.Actors/Events/QuestionEvents.cs` | SAI-01 |
| `src/actors/Cena.Actors/Serving/QuestionPoolActor.cs` | SAI-01 |
| `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | SAI-02, SAI-03 |
| `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` | SAI-02, SAI-04 |
| `src/actors/Cena.Actors/Services/ConfusionDetector.cs` | SAI-02 |
| `src/actors/Cena.Actors/Services/DisengagementClassifier.cs` | SAI-02 |
| `src/actors/Cena.Actors/Services/CognitiveLoadService.cs` | SAI-04 |
| `src/actors/Cena.Actors/Gateway/LlmCircuitBreakerActor.cs` | SAI-00, SAI-04, SAI-08 |
| `src/actors/Cena.Actors/Bus/NatsBusMessages.cs` | SAI-04 |
| `src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs` | SAI-06 |
| `src/actors/Cena.Actors/Ingest/DeduplicationService.cs` | SAI-07 |
