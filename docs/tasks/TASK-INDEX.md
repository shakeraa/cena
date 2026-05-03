# Cena Task Index

> **Generated**: 2026-04-13 | **Updated**: 2026-04-13 late  
> **Source**: `.agentdb/kimi-queue.db` — 57 pending / 30 done this session (of 89 tasks in this index)  
>
> ### Active Workers
> | Worker | Role | Done this session | Still pending |
> |--------|------|-------------------|---------------|
> | `claude-code` | Coordinator + coder | 13 (GD-001/002/003/004/005/010, TENANCY-P1, FIGURE-001, BKT-PLUS-001, CAS-001/002, PWA-BE-004, MASTERY-MAP-001) | 0 |
> | `kimi-coder` | Backend coder | 3 (P1a, P1c, PWA-BE-001) | 10 |
> | `claude-1` | Backend coder | 14 (P1b/d/e/f, DATA-READY-001, BAGRUT-ALIGN-001, STEP-003, SEC-ASSESS-001/002, FIGURE-002/005, CAS-BIND-001, + more) | 5 |

---

## Critical Priority

These ship-gate and foundational tasks block large downstream chains. Do these first.

| ID | Task | Assignee |
|----|------|----------|
| `t_1e403143f266` | ~~**GD-001**: ADR — CAS engine is sole source of truth for tutor correctness (3-tier + QuestionCasBinding)~~ | `claude-code` DONE |
| `t_1f649986bd81` | ~~**GD-002**: ADR — Misconception state is session-scoped, not profile-scoped (Edmodo precedent)~~ | `claude-code` DONE |
| `t_5e70a5443a83` | ~~**GD-004**: Elevate Point 7 to engineering ship-gate (CI scanner + banned-terms + PR template)~~ | `claude-code` DONE |
| `t_66da32a5e647` | ~~**CAS-001**: Build Cena Mini CAS Engine (MathNet in-process + SymPy sidecar)~~ | `claude-code` DONE |
| `t_299a195273b6` | ~~**CAS-002**: Step verifier API endpoint + NATS integration~~ | `claude-code` DONE |
| `t_80beac4a0617` | **PHOTO-003**: Content moderation pipeline for minors | `kimi-coder` |
| `t_ed9e82edb171` | ~~**BKT-PLUS-001**: BKT+ extensions (forgetting, prerequisites, assistance weighting)~~ | `claude-code` DONE |
| `t_426568611b77` | **CAS-LLM-001**: CAS-verify all mathematical expressions in LLM output | `kimi-coder` |

## CAS Engine & Step Solver (High)

| ID | Task | Assignee |
|----|------|----------|
| `t_042a298ac521` | **CAS-003**: 500-pair conformance suite (SymPy ↔ MathNet equivalence) | `claude-1` |
| `t_eeddcd506753` | ~~**CAS-BIND-001**: QuestionCasBinding — lock question to authoring CAS engine~~ | `claude-1` DONE |
| `t_5d86d606aeda` | **STEP-001**: StepSolverCard.vue + StepInput.vue components | — |
| `t_816b6c6dca5e` | **STEP-002**: MathInput.vue (MathLive wrapper) | — |
| `t_1860c4bc90ff` | ~~**STEP-003**: StepSolverQuestion schema + events + upcaster~~ | `claude-1` DONE |
| `t_ad69926430fc` | **STEP-004**: Step generation tooling in admin (CAS-proposed steps) | — |
| `t_48d611d17e41` | **STEP-005**: Seed 10 step-solver questions (algebra/calculus/trig) | — |
| `t_a61b633e9df5` | **SCAFFOLD-001**: Exploratory scaffolding level (productive failure) | — |

## IRT & Mastery

| ID | Task | Assignee |
|----|------|----------|
| `t_5e2e6538ac44` | **IRT-001**: Rasch/2PL item calibration pipeline | `kimi-coder` |
| `t_10fc74478934` | **IRT-002**: Item bank health dashboard + quality gate | `kimi-coder` |
| `t_7488f2fc6a42` | **IRT-003**: Constrained CAT algorithm + exposure control | `kimi-coder` |
| `t_ed9e82edb171` | ~~**BKT-PLUS-001**: BKT+ extensions (forgetting, prerequisites, assistance weighting)~~ | `claude-code` DONE |
| `t_753c673f6f10` | **MASTERY-001**: Per-skill-per-track mastery with cross-track seepage | `kimi-coder` |
| `t_fe3adac90186` | **MASTERY-002**: Anonymous class-level mastery stats (k≥10 anonymity) | — |
| `t_f3c0f987e5a6` | ~~**MASTERY-MAP-001**: Mastery map progress visualization~~ | `claude-code` DONE |
| `t_5985d7e0bbc6` | **STEP-IRT-001**: Include InstituteId + TrackId in step verification events | — |

## Figures & Diagrams

| ID | Task | Assignee |
|----|------|----------|
| `t_df2c171d1345` | ~~**FIGURE-001**: ADR — Figure rendering stack (function-plot, JSXGraph, programmatic SVG for physics)~~ | `claude-code` DONE |
| `t_36778eabc6dd` | ~~**FIGURE-002**: Schema — figure_spec on QuestionDocument + event + upcaster~~ | `claude-1` DONE |
| `t_2fad758f7fd3` | **FIGURE-003**: Web — `<QuestionFigure>` Vue component (function-plot + JSXGraph + SVG) | — |
| `t_db2a55cbb61b` | **FIGURE-004**: Wire `<QuestionFigure>` into QuestionCard.vue + seed 6 demo questions | — |
| `t_d703918cd9ea` | ~~**FIGURE-005**: Backend PhysicsDiagramService — programmatic SVG for inclined plane, free-body, pulley, vector~~ | `claude-1` DONE |
| `t_32fe0df86015` | **FIGURE-006**: Admin figure editor — JSON spec + live preview + validation + templates (Phase 1) | — |
| `t_faa8109c6380` | **FIGURE-007**: Quality gate rules for figures (aria-label, marker correctness, equilibrium check) | `claude-1` |
| `t_0ff186d0dbd6` | **FIGURE-008**: AI generation proposes figure specs during variant generation (with retry loop) | — |
| `t_b7d877a20577` | **FIG-RTL-001**: Script property on diagram text elements for bidi rendering | — |
| `t_04371cd19b52` | **FIG-MOBILE-001**: Mini figure thumbnail on mobile during step input | — |
| `t_0e58c3b1a33f` | **FIG-VIS-001**: visibleAtLevel on PhysicsDiagramSpec elements | — |
| `t_4e151211d7dc` | **FIG-QUAL-001**: Figure quality gate — info level vs difficulty consistency | — |
| `t_db2b16b3f28a` | **FBD-001**: Free-body diagram Construct mode for physics | — |

## Photo Ingestion & Camera

| ID | Task | Assignee |
|----|------|----------|
| `t_f545af369b82` | **PHOTO-001**: Student photo capture + Gemini Vision integration | — |
| `t_282c0e046792` | **PHOTO-002**: Bagrut PDF ingestion pipeline (admin) | — |
| `t_80beac4a0617` | **PHOTO-003**: Content moderation pipeline for minors | — |
| `t_e5691cd3b2b8` | **PWA-BE-003**: Photo upload endpoint hardening (EXIF verify, circuit breaker) | `kimi-coder` |

## Tenancy — Phase 1 (Multi-Institute Schema)

| ID | Task | Assignee |
|----|------|----------|
| `t_c7282695281e` | ~~**TENANCY-P1**: Multi-institute schema scaffold (Program + Classroom modes + platform seed)~~ | `claude-code` DONE |
| `t_2efbdd5b49a4` | ~~**TENANCY-P1a**: New document types (Institute + CurriculumTrack + Enrollment)~~ | `kimi-coder` DONE |
| `t_b67c64eb08fa` | ~~**TENANCY-P1b**: Extend ClassroomDocument with Mode + JoinApproval + Institute/Program binding~~ | `claude-1` DONE |
| `t_c4865abd14d0` | ~~**TENANCY-P1c**: New EnrollmentEvents.cs — 8 event types + MartenConfiguration registration~~ | `kimi-coder` DONE |
| `t_d497a446f333` | ~~**TENANCY-P1d**: Platform seed — 3 Bagrut tracks + BAGRUT-GENERAL placeholder~~ | `claude-1` DONE |
| `t_89d9c909b4cd` | ~~**TENANCY-P1e**: Student stream upcaster — BAGRUT-GENERAL placeholder~~ | `claude-1` DONE |
| `t_f6b1364b1892` | ~~**TENANCY-P1f**: TenantScope.GetInstituteFilter — Phase 1 single-element wrapper~~ | `claude-1` DONE |

## Tenancy — Phase 2 (Cross-Enrollment)

| ID | Task | Assignee |
|----|------|----------|
| `t_785163249bae` | **VERIFY-0001**: Transfer-of-learning literature + design review for cross-track mastery sharing | — |
| `t_08733488d83e` | **TENANCY-P2**: Cross-enrollment re-key + PersonalMentorship mode + AssignmentDocument | — |
| `t_08f268d584e8` | **TENANCY-P2a**: Mastery state re-key per ADR-0002 model (A/B/C) | — |
| `t_6f5b0e4467b4` | **TENANCY-P2b**: PersonalMentorship classroom mode | — |
| `t_b8530ac8af0d` | **TENANCY-P2c**: AssignmentDocument aggregate + mentor assignment endpoints | — |
| `t_7f29b647f581` | **TENANCY-P2d**: MentorNoteDocument — markdown notes anchored to sessions/questions | — |
| `t_fb7fe86b1d13` | **TENANCY-P2e**: Student onboarding V2 — platform catalog picker + join-code/invite flow | — |
| `t_30fdeb58211e` | **TENANCY-P2f**: Enrollment switcher UI — top-bar dropdown scoping all downstream pages | — |

## Tenancy — Phase 3 (Mentor + Chat + Roles)

| ID | Task | Assignee |
|----|------|----------|
| `t_bc1e95472c38` | **TENANCY-P3**: Mentor admin surface + Firebase roles + Chat capability | — |
| `t_c8ef4f5d3652` | **TENANCY-P3a**: Firebase custom claims — per-institute role mapping | — |
| `t_5882bcd92306` | **TENANCY-P3b**: Mentor dashboard Vue pages (institute CRUD + classroom management) | — |
| `t_f7afcb20c570` | **TENANCY-P3c**: Instructor-scoped view — classroom-only subset of mentor dashboard | — |
| `t_41f24e92beb5` | **TENANCY-P3d**: Chat capability wire-up (mentor-student text channel via SignalR) | — |
| `t_e4e50f990dc0` | **TENANCY-P3e**: Platform program fork/reference workflow + version update push | — |
| `t_43a5353a2a96` | **TENANCY-P3f**: Invite link machinery — signed JWT + short code + QR + rate-limited redeem | — |

## Assessment Security

| ID | Task | Assignee |
|----|------|----------|
| `t_64d5bd85ea89` | ~~**SEC-ASSESS-001**: Per-student variant seeding with daily rotation~~ | `claude-1` DONE |
| `t_62a078e4700f` | **SEC-ASSESS-002**: Exam simulation mode (reserved pool, timed, no hints) | `claude-1` |
| `t_0a848d030bb2` | **SEC-ASSESS-003**: Behavioral anomaly detection (informational flags) | `claude-1` |
| `t_fe1782b3fb46` | **SEC-ASSESS-004**: Exam-time upload detection + homework copy-paste mitigation | `claude-1` |
| `t_d492d9ab5a3e` | **LATEX-001**: LaTeX sanitization (200-command allowlist, CVE-2024-28243) | `kimi-coder` |
| `t_3e5d35e0d15d` | **RATE-001**: 4-tier rate limiting (token bucket + cost circuit breaker) | `kimi-coder` |

## PWA & Backend Infrastructure

| ID | Task | Assignee |
|----|------|----------|
| `t_288dd630543d` | **PWA-BE-002**: Web Push notification backend (VAPID, subscriptions, dispatch) | `kimi-coder` |
| `t_e1b639238c66` | ~~**PWA-BE-004**: Offline submission replay (idempotent batch, session expiry)~~ | `claude-code` DONE |
| `t_84920aea86df` | **EVENT-SCALE-001**: Event store scaling (snapshots, partitioning, async projections) | `claude-1` |
| `t_9f558b23be97` | **OBS-001**: Three-layer observability (OTel + structured logs + 6 critical alerts) | `claude-1` |

## Session UX & Pedagogy

| ID | Task | Assignee |
|----|------|----------|
| `t_264664cb404e` | **SESSION-UX-001**: Session start with topic choice + personalized suggestion | `kimi-coder` |
| `t_13969ffb1ccd` | **SESSION-UX-002**: Progressive disclosure + natural session boundaries | — |
| `t_df8bde06d82b` | **MISC-001**: Misconception catalog (15 empirical entries + session tally) | `kimi-coder` |
| `t_91774f2bd00f` | **REMEDIATION-001**: Remediation micro-task templates | — |
| `t_c2399e893bc2` | **READINESS-001**: Bagrut readiness report with confidence intervals | — |
| `t_b104aa9c2d51` | ~~**DATA-READY-001**: ContentReadiness on CurriculumTrack~~ | `claude-1` DONE |

## Accessibility & Localization

| ID | Task | Assignee |
|----|------|----------|
| `t_99e76db4326c` | **A11Y-SRE-001**: SRE aria-labels for math in Arabic/Hebrew | — |
| `t_528fe358d27b` | **ARABIC-001**: Arabic math input normalizer (س→x, جذر→√, Eastern digits) | — |
| `t_e8023a67c947` | **ARABIC-002**: Arabic parent install guide PDF for pilot | — |
| `t_ff4ca23cc6cd` | ~~**BAGRUT-ALIGN-001**: Bagrut structural alignment tags on QuestionDocument~~ | `claude-1` DONE |

## Game Design & Strategy

| ID | Task | Assignee |
|----|------|----------|
| `t_23cdde7471da` | ~~**GD-003**: Rewrite proposal Point 6 — daily Wordle → community puzzle (no streak)~~ | `claude-code` DONE |
| `t_e781f5e6524f` | ~~**GD-005**: Compliance artifacts umbrella — 10 docs under docs/compliance/~~ | `claude-code` DONE |
| `t_74e1081b02c3` | **GD-006**: Spike — MathLive RTL parity for Arabic and Hebrew (1–2 day time-box) | — |
| `t_a11f50b4614d` | **GD-007**: PhET-style student-interview protocol for sandbox physics iteration | — |
| `t_cbaa9ec2ae1b` | **GD-008**: Market decision — Arabic-first 5-unit physics wedge (Nazareth/Umm al-Fahm/Rahat pilot) | — |
| `t_61a65b864c8b` | **GD-009**: Hands-on competitor study week — 12 products | — |
| `t_14416e393624` | ~~**GD-010**: Memory update — ship-gate ban + SymPy oracle + misconception scope rule~~ | `claude-code` DONE |

## Bug Fixes

| ID | Task | Assignee |
|----|------|----------|
| `t_a54113d077fd` | **BUG-test-001**: QuestionSelectorTests flake — static wall-clock-seeded Random in QuestionSelector.cs:41 | `kimi-coder` |

---

## How to Claim a Task

```bash
# 1. Read the full task body
node .agentdb/kimi-queue.js show <id>

# 2. Claim it
node .agentdb/kimi-queue.js claim <id> --worker <your-name>

# 3. Work on a feature branch: <your-name>/<id>-<slug>

# 4. When done
node .agentdb/kimi-queue.js complete <id> --worker <your-name> --result "<summary + branch name>"

# 5. If blocked
node .agentdb/kimi-queue.js fail <id> --worker <your-name> --reason "<what's blocking>"
```

## Architecture Docs

| Document | Location |
|----------|----------|
| Expert Panel Consolidated Architecture | `docs/architecture/consolidated-architecture-v2.md` |
| Game Design Discussion (GD-*) | `docs/architecture/game-design-panel-discussion.md` |
| Question Bank Architecture | `docs/architecture/question-bank-architecture.md` |
| CAS & Step Solver Design | within consolidated architecture §7–§12 |
| IRT & Mastery Design | within consolidated architecture §13–§18 |
| Photo Ingestion Pipeline | within consolidated architecture §19–§22 |
| Assessment Security | within consolidated architecture §23–§26 |
| PWA Architecture | within consolidated architecture §27–§32 |
| Tenancy Multi-Institute | within consolidated architecture §33–§38 |
| Improvement Registry (all 67 items) | within consolidated architecture §44 |
