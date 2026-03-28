# SAI: Student AI Interaction — Task Overview

**Source**: `docs/autoresearch-student-ai-interaction.md` (10-iteration codebase validation)
**Source Discussion**: `docs/discussion-student-ai-interaction.md`
**Created**: 2026-03-28

---

## Dependency Graph

```
SAI-001 (Persist L1 Explanations)          SAI-002 (Hint Content + BKT Credit)
    │  no deps                                  │  no deps
    ▼                                           ▼
SAI-003 (L2 ErrorType Cache)              SAI-005 (Confusion-State Gating)
    │  depends: SAI-001, LLM-001              │  depends: SAI-002
    ▼                                           │
SAI-004 (L3 Personalized Explanations)         │
    │  depends: SAI-003                         │
    ├───────────────────────────────────────────┘
    ▼
SAI-006 (A/B Experiments)
    │  depends: SAI-002, SAI-004
    ▼
SAI-007 (Content Extraction Pipeline)
    │  no deps on SAI chain
    ▼
SAI-008 (pgvector + Embeddings)
    │  depends: SAI-007
    ▼
SAI-009 (Conversational Tutoring)
    │  depends: SAI-004, SAI-008
    ▼
    DONE
```

## Parallel Tracks

| Track | Tasks | Days | Dependencies |
|-------|-------|------|-------------|
| A — Foundation | SAI-001, SAI-003, SAI-004 | ~8 days serial | LLM-001 (ACL scaffold) for SAI-003+ |
| B — Hints | SAI-002, SAI-005 | ~4 days serial | None |
| C — Measurement | SAI-006 | ~2 days | Tracks A + B |
| D — Tier 3 Infra | SAI-007, SAI-008 | ~9 days serial | None (can start day 1) |
| E — Tier 3 | SAI-009 | ~8 days | Tracks A + D |

**Critical path**: Track A → SAI-006 → Track E = ~20-25 days
**With parallelism**: Tracks A+B+D start simultaneously, converge at SAI-006 and SAI-009.

## Cross-Cutting Constraints

- LLM ACL (`src/llm-acl/`) is a separate Python FastAPI service (see `tasks/llm/LLM-001` through `LLM-010`)
- .NET actors call LLM ACL via gRPC (defined in LLM-001)
- All SAI tasks are .NET domain-side changes in `src/actors/Cena.Actors/` and `src/api/Cena.Admin.Api/`
- Event sourcing: all state changes via Marten events, Apply() projections, snapshot compatibility
- SignalR contract at `contracts/frontend/signalr-messages.ts` — modify contract, not just backend
- Redis keys follow `cena:` prefix convention (see `MessagingRedisKeys.cs` pattern)
