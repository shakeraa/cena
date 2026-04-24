# Pre-release review — resumption plan (post-batch-11)

**Created 2026-04-21 after batches 1–11 shipped.**
Main: `520061e1`. 5 main epics all 100% complete. 76 tasks done.

## Priority batches (in order)

### Batch 12 — Salvage partial-worktree P0/P1 (HIGHEST PRIORITY)
Agents hit usage reset on 2026-04-21 mid-implementation; 3 worktrees have significant uncommitted work. Salvage pattern = the prr-015 pattern: main-session coordinator commits what the agent built, fixes build/test failures, pushes, merges.

| Task | P | Worktree | Domain | Notes |
|---|---|---|---|---|
| **prr-001** | **P0** | `.claude/worktrees/prr-001combo` | Security | EXIF strip fix — response currently lies |
| prr-017 | P1 | `.claude/worktrees/prr-001combo` | Secrets | Mashov → Secret Manager + rotation runbook |
| prr-020 | P1 | `.claude/worktrees/prr-001combo` | Ops | Redis session store health + eviction alerts |
| prr-026 | P1 | `.claude/worktrees/prr-026combo` | Privacy | k=10 anonymity floor for teacher aggregates |
| prr-045 | P1 | `.claude/worktrees/prr-026combo` | Privacy | TutorPromptScrubber audit (extends prr-022) |
| prr-152 | P1 | `.claude/worktrees/prr-026combo` | Privacy | Erasure cascade to new per-student projections |
| prr-031 | P1 | `.claude/worktrees/prr-031combo` | A11y/i18n | Hebrew/Arabic MathAriaLabels |
| prr-032 | P1 | `.claude/worktrees/prr-031combo` | A11y/i18n | Arabic RTL math + numerals toggle |

**Salvage protocol per worktree:** (1) `cd` into worktree, `git add -A`, (2) `dotnet build ... -p:SkipOpenApiGeneration=true`, (3) fix any CS errors from untracked-but-unbuildable partial code (most common: missing `using`, missing DI wiring), (4) `dotnet test` Actors.Tests + Admin.Api.Tests, (5) fix arch-test violations (baseline bumps, allowlist updates), (6) commit + push + merge.

### Batch 13 — Ministry/Mashov integration hardening (4 tasks, P1)
| Task | Domain |
|---|---|
| prr-033 | Ministry Bagrut rubric DSL — version-pinning per track, sign-off |
| prr-035 | Sub-processor registry + DPAs (Mashov/Classroom/Twilio/Anthropic) |
| prr-037 | Grade passback policy ADR — teacher opt-in + veto allowlist |
| prr-039 | Mashov sync circuit breaker + synthetic probe + staleness badge |

All four are Ministry-integration P1 surface; bundle for single ADR + arch test.

### Batch 14 — Privacy/Moderation P1 (3 tasks)
| Task | Domain |
|---|---|
| prr-025 | CAS-gate or teacher-moderate peer math explanations before display |
| prr-036 | Reflective-text PII scrub before persistence (session-scope) |
| prr-158 | Offline cache encryption + wipe on logout |

### Batch 15 — Exam-day SRE P1 (2 tasks)
| Task | Domain |
|---|---|
| prr-016 | Publish exam-day SLO + change-freeze window in CD |
| prr-053 | Exam-day capacity plan — Bagrut traffic forecast |

Tight pair; same SRE runbook surface.

### Batch 16 — ADR-only + P2 pedagogy tail (6 tasks)
| Task | Domain |
|---|---|
| prr-023 | Saga process-manager pattern for cross-student collaboration (ADR) |
| prr-024 | External integration adapter pattern (ADR) |
| prr-034 | Cultural-context community review board + ops queue DLQ |
| prr-043 | ADR companion-bot therapy scope boundary |
| prr-154 | If-then implementation-intentions planner (F2, pedagogy) |
| prr-159 | F5 "I'm confused too" anonymous signal (pedagogy) |

All light implementations; ADRs first, tiny arch tests + placeholder services second.

### Parked follow-ups (do NOT include in batches — user deliberately deferred)
- prr-011c CSRF double-submit token
- prr-011d HS256 → RS256/JWKS
- prr-011e session idle-timeout
- prr-011f frontend Vue cookie migration
- prr-011g E2E Playwright cookie flow
- prr-011h Redis-backed revocation list
- prr-011i legacy cookie-name cleanup

These get re-enqueued only on user request.

## Execution rule (from `feedback_finish_all_epics_continuous`)

> Do not pause between batches. Spawn next batch immediately when current batch merges. Only stop for genuine decision gates.

Batches 12–16 together = 23 items. Expected 5 session-merges. After batch 16: only the 7 parked 011 follow-ups remain — those stay parked until user un-parks them.
