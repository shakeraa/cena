# Student AI Interaction — Execution Plan

**Owner**: Lead Architect
**Source**: `docs/student-ai-interaction-tasks.md` + `docs/discussion-student-ai-interaction.md`
**Date**: 2026-03-28
**Status**: Ready for execution

---

## Dependency Graph

```
Track A (days 1-3)           Track B (days 1-3)       Track E (days 10-20)
┌──────────────┐             ┌──────────────┐         ┌──────────────┐
│ 00-llm-sdk   │             │ 01b-hints    │         │ 05-content   │
│ (Anthropic)  │             │ (BKT+confuse)│         │ (extraction) │
└──────┬───────┘             └──────┬───────┘         └──────┬───────┘
       │                            │                        │
┌──────┴───────┐                    │                 ┌──────┴───────┐
│ 01a-persist  │                    │                 │ 06-pgvector  │
│ (L1 explain) │                    │                 │ (embeddings) │
└──────┬───────┘                    │                 └──────┬───────┘
       │                            │                        │
Track C (days 4-8)                  │                        │
┌──────┴───────┐                    │                        │
│ 02-L2-cache  │                    │                        │
│ (Redis)      │                    │                        │
└──────┬───────┘                    │                        │
       │                            │                        │
┌──────┴───────┐                    │                        │
│ 03-L3-person │                    │                        │
│ (LLM context)│                    │                        │
└──────┬───────┘                    │                        │
       │         Track D (day 9)    │                        │
       └────────►┌──────────────┐◄──┘                        │
                 │ 04-ab-tests  │                             │
                 │ (experiments)│                             │
                 └──────┬───────┘                             │
                        │         Track F (days 21-30)        │
                        └────────►┌──────────────┐◄───────────┘
                                  │ 07-tutor     │
                                  │ (TutorActor) │
                                  └──────────────┘
```

## Critical Path

00 → 02 → 03 → 04 → 07 (LLM SDK → L2 cache → L3 personalization → A/B → TutorActor)

Parallel compression: ~20-25 days total (from ~30 sequential).

## Architecture Invariants (Every Task MUST Obey)

1. **Event sourcing is law** — every state change is a Marten event. No direct state mutation.
2. **Proto.Actor virtual actors** — per-student state lives in actors, not singleton services.
3. **SignalR for students, REST for admin** — no GraphQL (project decision).
4. **Circuit breaker** — all LLM calls go through `LlmCircuitBreakerActor`. No direct HTTP.
5. **NATS JetStream** — 8 durable streams, 90-day retention. Use for inter-service events.
6. **Hebrew/Arabic RTL** — all text content must handle RTL. Language-aware explanations.
7. **Files < 500 lines** — split if approaching.
8. **TDD London School** — mock-first. Tests before implementation.
9. **`dotnet build` + `dotnet test`** — must pass at end of every task.

## Task Index

| # | File | Effort | Track | Depends On |
|---|------|--------|-------|------------|
| 00 | [00-llm-sdk-integration.md](00-llm-sdk-integration.md) | 2-3d | A | Nothing |
| 01a | [01a-persist-explanations.md](01a-persist-explanations.md) | 1d | A | Nothing |
| 01b | [01b-hint-generation.md](01b-hint-generation.md) | 2-3d | B | Nothing |
| 02 | [02-explanation-cache.md](02-explanation-cache.md) | 3-5d | C | 00 |
| 03 | [03-personalized-explanations.md](03-personalized-explanations.md) | 3-4d | C | 02 |
| 04 | [04-ab-experiments.md](04-ab-experiments.md) | 1-2d | D | 01b, 02, 03 |
| 05 | [05-content-extraction.md](05-content-extraction.md) | 5-7d | E | Nothing |
| 06 | [06-pgvector-embeddings.md](06-pgvector-embeddings.md) | 3-4d | E | 05 |
| 07 | [07-tutor-actor.md](07-tutor-actor.md) | 7-10d | F | All |

## Research Reference

See [RESEARCH.md](RESEARCH.md) for the autoresearch findings (58 citations, effect sizes, Israeli education context) that justify every design decision in these tasks.
