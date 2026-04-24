# Student AI Interaction — Task Breakdown

**Owner**: Lead Architect
**Source**: `docs/autoresearch-student-ai-interaction.md`, `docs/student-ai-interaction-tasks.md`
**Created**: 2026-03-28

## Dependency Graph

```
  Task-00 (LLM SDK)          Task-01a (Persist L1)       Task-01b (Hints + BKT)
       |                          |                            |
       +----------+---------------+                            |
                  |                                             |
             Task-02 (L2 Cache)                                |
                  |                                             |
             Task-03 (L3 Personalized)                         |
                  |                                             |
                  +---------------------+-----------------------+
                                        |
                                   Task-04 (A/B)

  Task-05 (Content Extraction)    [independent track]
       |
  Task-06 (pgvector + Embeddings)
       |
       +--- all above ---+
                          |
                     Task-07 (TutorActor)
```

## Parallel Tracks

| Track | Tasks | Days | Dependencies |
|-------|-------|------|-------------|
| A | 00 + 01a | 1-3 | None |
| B | 01b | 1-3 | None |
| C | 02 + 03 | 4-8 | Track A |
| D | 04 | 9 | Tracks B + C |
| E | 05 + 06 | 10-20 | Independent |
| F | 07 | 21-30 | All |

## Non-Negotiable Rules (All Tasks)

1. **Event sourcing is law** — every state change is a domain event via Marten
2. **TDD London School** — write tests first, mock dependencies, verify behavior
3. **Proto.Actor grain model** — per-student virtual actors, no singleton services for per-student state
4. **Circuit breaker** — all LLM calls via `LlmCircuitBreakerActor`, never direct HTTP
5. **SignalR for students, REST for admin** — no GraphQL (project decision)
6. **RTL language support** — Hebrew/Arabic, all text content must handle RTL
7. **Files under 500 lines** — split if approaching
8. **Build gate** — `dotnet build` + `dotnet test` must pass at task end
9. **No mock data in production paths** — stubs throw `NotImplementedException`, not silent fakes
