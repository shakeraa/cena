# Tenancy — Multi-Institute Enrollment Task Bundle

**Source**: [docs/adr/0001-multi-institute-enrollment.md](../../docs/adr/0001-multi-institute-enrollment.md)
**Date**: 2026-04-11
**Architect**: Senior Architect review (claude-code coordinator session)
**Status**: Phase 1 ready for implementation · Phases 2–3 blocked

---

## Overview

Cena's current data model assumes one student belongs to exactly one school. The Israeli market requires a student to hold simultaneous enrollments across multiple institutes — bagrut prep at their school, SAT prep at a private tutor, psychometry at a cram school. This bundle delivers the full multi-institute enrollment architecture in three phases.

Prefix: `TEN-*` (Tenancy).

Total estimated effort: **~40–60 engineer-days** across all three phases. Phase 1 is independently shippable (~8–12 days). Phases 2–3 depend on a research gate (VERIFY-0001).

---

## Architecture — the entity hierarchy

```
Institute (Mentor-owned)
  │
  ├── CurriculumTrack            ← target-exam level (MATH-BAGRUT-5UNIT …)
  │     └── LearningObjective[]  ← FIND-pedagogy-008
  │
  └── Program                    ← authored course: "Grade 10 Algebra 2026-27"
        │  content, lesson plans, question bank, schedule template
        │  Origin: Platform | Forked | Custom
        │
        └── Classroom (cohort)   ← "Round 1", "Group 2", "Mon-Wed 9am"
              │  Mode: SelfPaced | InstructorLed | PersonalMentorship
              │  JoinApprovalMode: AutoApprove | ManualApprove | InviteOnly
              │
              └── Enrollment (student ↔ classroom)
```

The Cena Platform institute ships 5 canonical programs on day 1. Third-party institutes Reference, Fork, or Author from scratch.

---

## Locked decisions (do not re-open)

1. Student belongs to M institutes (ADR-0001 Decision 1 — locked 2026-04-11).
2. Mentor is the single unified role; capabilities come from ClassroomMode + MentorCapability flags.
3. Three classroom modes: SelfPaced, InstructorLed, PersonalMentorship.
4. Per-classroom join-approval setting (AutoApprove | ManualApprove | InviteOnly).
5. Program + Classroom are separate entities (content vs cohort).
6. Cena Platform institute with canonical programs; 3rd parties Reference/Fork/Author.
7. Day-1 seed: Bagrut Math 3/4/5 + SAT Math + Psychometry Quantitative (math-only).
8. AssignmentDocument is Phase 2, not Phase 1.

## Open decision (gated behind research)

**Decision 2** — mastery state sharing across tracks (A: shared / B: isolated / C: seeded-divergent). User prior: "usually yes, but verify." Blocks Phase 2. See [VERIFY-0001](TASK-TEN-VERIFY-0001.md).

---

## Quality standard (applies to ALL tasks in this bundle)

Every implementation in this bundle MUST be:

- **Production-grade from day one.** No stubs, no canned data, no `// TODO phase N` placeholders. If a feature belongs to a later phase, it is excluded entirely — not stubbed.
- **Event-sourced.** State changes go through events, not direct document writes. No `session.Store()` on projections (see FIND-data-007 lesson). No separate `LightweightSession` that races the caller's transaction.
- **Tested with real assertions.** Every new document type gets a round-trip test. Every new event gets a registration test. Every upcaster gets an idempotency test. "Test that asserts `result != null`" is not a test.
- **Backward compatible.** Existing tests must keep passing. New fields on existing documents must be nullable or defaulted. Event upcasters must handle streams created before the change.
- **Convention-compliant.** Event names use `snake_case_v1` (FIND-data-005 convention). Documents follow the `Id` alias pattern from `QuestionDocument`. Seed data is idempotent (two runs = one row, not two).
- **Architecturally sound.** Follow the patterns established in the 2026-04-11 review-fix session: CQRS purity, dual Elo updates on the same session, tenant scoping via JWT claims not request bodies, typed subject registries over magic strings.

---

## Prerequisites

| Prereq | Why | Status |
|---|---|---|
| [2026-04-11 review-fix session](../../docs/reviews/cena-review-2026-04-11.md) | 44 P0/P1 findings shipped; the codebase is now stable enough for schema expansion | Done |
| [FIND-pedagogy-008](../../docs/reviews/agent-4-pedagogy-findings.md) | LearningObjectiveDocument exists; CurriculumTrack references it | Done (merged) |
| [FIND-pedagogy-009 enriched](../../src/actors/Cena.Actors/Services/EloDifficultyService.cs) | Dual Elo + student-side EloRating on StudentProfileSnapshot | Done (merged) |
| BKT + scaffolding infrastructure | BktService, HintGenerator, ScaffoldingService all wired | Done (merged) |

---

## Task index

### Research gate

| ID | Task | Effort | Depends on | Blocks |
|---|---|---|---|---|
| [TEN-VERIFY-0001](TASK-TEN-VERIFY-0001.md) | Transfer-of-learning lit review → ADR-0002 | 3–5d | nothing | Phase 2 |

### Phase 1 — schema scaffold, zero behavior change

| ID | Task | Effort | Depends on | Blocks |
|---|---|---|---|---|
| [TEN-P1a](TASK-TEN-P1a.md) | 4 new document types | 1–2d | — | P1b, P1c |
| [TEN-P1b](TASK-TEN-P1b.md) | Extend ClassroomDocument | 0.5d | P1a | P1d |
| [TEN-P1c](TASK-TEN-P1c.md) | 8 new events + MartenConfiguration | 1d | P1a | P1d, P1e |
| [TEN-P1d](TASK-TEN-P1d.md) | Platform seed data (5 programs) | 1–2d | P1a, P1b, P1c | P1e |
| [TEN-P1e](TASK-TEN-P1e.md) | Student stream upcaster + snapshot defaults | 2–3d | P1c, P1d | P1f |
| [TEN-P1f](TASK-TEN-P1f.md) | TenantScope.GetInstituteFilter | 0.5d | P1e | Phase 2 |

### Phase 2 — cross-enrollment + PersonalMentorship + assignments (blocked)

| ID | Task | Effort | Depends on | Blocks |
|---|---|---|---|---|
| [TEN-P2a](TASK-TEN-P2a.md) | Mastery re-key per ADR-0002 | 3–5d | VERIFY-0001 | P2f |
| [TEN-P2b](TASK-TEN-P2b.md) | PersonalMentorship mode | 2–3d | Phase 1 | P2c, P2d |
| [TEN-P2c](TASK-TEN-P2c.md) | AssignmentDocument + endpoints | 3–4d | P2b | — |
| [TEN-P2d](TASK-TEN-P2d.md) | MentorNoteDocument | 1–2d | P2b | — |
| [TEN-P2e](TASK-TEN-P2e.md) | Student onboarding V2 | 3–4d | Phase 1 | P2f |
| [TEN-P2f](TASK-TEN-P2f.md) | Enrollment switcher UI | 2–3d | P2a, P2e | Phase 3 |

### Phase 3 — mentor admin surface + chat + invites (blocked)

| ID | Task | Effort | Depends on | Blocks |
|---|---|---|---|---|
| [TEN-P3a](TASK-TEN-P3a.md) | Firebase custom claims | 2–3d | Phase 2 | P3d, P3f |
| [TEN-P3b](TASK-TEN-P3b.md) | Mentor dashboard Vue pages | 5–8d | Phase 2 | P3c, P3e |
| [TEN-P3c](TASK-TEN-P3c.md) | Instructor-scoped view | 2–3d | P3b | — |
| [TEN-P3d](TASK-TEN-P3d.md) | Chat capability | 3–5d | P2, P3a | — |
| [TEN-P3e](TASK-TEN-P3e.md) | Fork/reference workflows | 2–3d | P3b | — |
| [TEN-P3f](TASK-TEN-P3f.md) | Invite link + short code + QR | 2–3d | P3a | — |

---

## Critical path

```
P1a ──→ P1b ──→ P1d ──→ P1e ──→ P1f ──→ Phase 2
   └──→ P1c ──┘    │
                    ↓
          VERIFY-0001 ──→ P2a ──→ P2f ──→ Phase 3
                          P2b ──→ P2c
                            └──→ P2d
                          P2e ──→ P2f
```

Phase 1 sub-tasks can partially parallelize: P1a is the gate; P1b and P1c can run in parallel after it ships; P1d fans in from all three; P1e and P1f are serial.

VERIFY-0001 (the research gate) can run in parallel with ALL of Phase 1. It only blocks Phase 2.

---

## Queue IDs (for CLI reference)

| Task | Queue ID |
|---|---|
| VERIFY-0001 | `t_785163249bae` |
| TEN-P1 (umbrella) | `t_c7282695281e` |
| TEN-P1a | `t_2efbdd5b49a4` |
| TEN-P1b | `t_b67c64eb08fa` |
| TEN-P1c | `t_c4865abd14d0` |
| TEN-P1d | `t_d497a446f333` |
| TEN-P1e | `t_89d9c909b4cd` |
| TEN-P1f | `t_f6b1364b1892` |
| TEN-P2 (umbrella) | `t_08733488d83e` |
| TEN-P2a | `t_08f268d584e8` |
| TEN-P2b | `t_6f5b0e4467b4` |
| TEN-P2c | `t_b8530ac8af0d` |
| TEN-P2d | `t_7f29b647f581` |
| TEN-P2e | `t_fb7fe86b1d13` |
| TEN-P2f | `t_30fdeb58211e` |
| TEN-P3 (umbrella) | `t_bc1e95472c38` |
| TEN-P3a | `t_c8ef4f5d3652` |
| TEN-P3b | `t_5882bcd92306` |
| TEN-P3c | `t_f7afcb20c570` |
| TEN-P3d | `t_41f24e92beb5` |
| TEN-P3e | `t_e4e50f990dc0` |
| TEN-P3f | `t_43a5353a2a96` |
