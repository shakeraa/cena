# Student Web — Task Bundle

**Source**: [docs/student/](../../docs/student/README.md) — comprehensive feature specification
**Date**: 2026-04-10
**Architect**: Lead Senior Architect review
**Status**: Ready for implementation (gated on infra prerequisites)

---

## Overview

This bundle turns the full specification in [docs/student/](../../docs/student/README.md) into a sequenced set of delivery tasks. Each task maps to exactly one feature doc and inherits its acceptance criteria (the `STU-*` bullet list at the bottom of each feature file is the task's checklist of work).

- **UI work** → tasks below with prefix `STU-W-*`
- **Backend work** → lives in [docs/tasks/student-backend/README.md](../student-backend/README.md) with prefix `STB-*`
- **Infrastructure prereqs** → [docs/tasks/infra-db-migration/README.md](../../docs/tasks/infra-db-migration/README.md) (`DB-05`, `DB-06`, `DB-08`)

Total estimated UI effort: **~55-75 engineer-days**, heavily parallelizable across feature leads after the foundation phase lands.

---

## Prerequisites

Before any student UI task begins, the following **must** be complete:

| Prereq | Why |
|---|---|
| [DB-05](../../docs/tasks/infra-db-migration/TASK-DB-05-contracts-library.md) — Extract `Cena.Api.Contracts` | TypeScript type generation for DTOs and hub events depends on a clean contracts library |
| [DB-06](../../docs/tasks/infra-db-migration/TASK-DB-06-split-hosts.md) — Split hosts | Student web connects to a dedicated `Cena.Student.Api.Host`, not the mixed host |
| [DB-08](../../docs/tasks/infra-db-migration/TASK-DB-08-role-timeouts-pool-isolation.md) — Role timeouts & pool isolation | Workload isolation guarantees student SLOs under admin load |

DB-00 through DB-04 are unblocking but independent — they fix the existing backend without affecting student UI work. DB-07 can ship in parallel with the first student UI tasks.

---

## Task Index

### Phase 1 — Foundation (sequential, ~8-12 days)

Builds the chassis every feature task needs. Must land first, in order.

| ID | Task | Feature Spec | Effort | Depends On |
|---|---|---|---|---|
| [STU-W-00](TASK-STU-W-00-project-scaffold.md) | Project scaffold — copy Vuexy starter, Vite, TypeScript, Pinia, routing | [00-overview.md](../../docs/student/00-overview.md) | 2-3d | DB-06 |
| [STU-W-01](TASK-STU-W-01-design-system-bootstrap.md) | Design system bootstrap — theme tokens, layouts, i18n/RTL, a11y primitives | [02-design-system.md](../../docs/student/02-design-system.md) | 2-3d | STU-W-00 |
| [STU-W-02](TASK-STU-W-02-navigation-shell-auth-guard.md) | Navigation shell — file-based router, sidebar, bottom nav, auth guards, breadcrumbs | [01-navigation-and-ia.md](../../docs/student/01-navigation-and-ia.md) | 2-3d | STU-W-01 |
| [STU-W-03](TASK-STU-W-03-api-signalr-client.md) | `$api` wrapper, SignalR client, typed hub events, draft autosave composable | [15-backend-integration.md](../../docs/student/15-backend-integration.md) | 2-3d | STU-W-01, DB-05 |

### Phase 2 — Core Learning Experience (parallelizable, ~20-28 days)

The heart of the product. These can run in parallel after Phase 1 lands, but STU-W-06 (session) is the largest single task and should get the most experienced lead.

| ID | Task | Feature Spec | Effort | Depends On | Backend |
|---|---|---|---|---|---|
| [STU-W-04](TASK-STU-W-04-auth-onboarding.md) | Auth screens + 7-step onboarding wizard + classroom code + guest trial | [03-auth-onboarding.md](../../docs/student/03-auth-onboarding.md) | 3-4d | STU-W-02, STU-W-03 | [STB-00](../student-backend/TASK-STB-00-me-profile-onboarding.md) |
| [STU-W-05](TASK-STU-W-05-home-dashboard.md) | Home widgets, realtime updates, customizable layout | [04-home-dashboard.md](../../docs/student/04-home-dashboard.md) | 3-4d | STU-W-04 | [STB-00](../student-backend/TASK-STB-00-me-profile-onboarding.md), [STB-02](../student-backend/TASK-STB-02-plan-review-recommendations.md) |
| [STU-W-06](TASK-STU-W-06-learning-session-core.md) | Live session, all answer types, flow monitor, feedback, hints, coach marks, all session types | [05-learning-session.md](../../docs/student/05-learning-session.md) | 8-12d | STU-W-03 | [STB-01](../student-backend/TASK-STB-01-session-start-and-hub.md), [STB-10](../student-backend/TASK-STB-10-hub-contracts-expansion.md) |
| [STU-W-07](TASK-STU-W-07-gamification.md) | XP popup, streaks, badges, quests, leaderboards, celebrations | [06-gamification.md](../../docs/student/06-gamification.md) | 3-4d | STU-W-05 | [STB-03](../student-backend/TASK-STB-03-gamification.md) |

### Phase 3 — Support & Enrichment (parallelizable, ~15-20 days)

Everything around the core loop. Mostly independent of each other; all depend on Phase 2.

| ID | Task | Feature Spec | Effort | Depends On | Backend |
|---|---|---|---|---|---|
| [STU-W-08](TASK-STU-W-08-ai-tutor.md) | Tutor chat, streaming, tool calls, context panel, voice, image, whiteboard | [07-ai-tutor.md](../../docs/student/07-ai-tutor.md) | 5-7d | STU-W-05 | [STB-04](../student-backend/TASK-STB-04-tutor.md) |
| [STU-W-09](TASK-STU-W-09-progress-mastery.md) | Progress dashboards, session history, mastery breakdown, learning time | [08-progress-mastery.md](../../docs/student/08-progress-mastery.md) | 3-4d | STU-W-04 | [STB-09](../student-backend/TASK-STB-09-analytics-export.md) |
| [STU-W-10](TASK-STU-W-10-knowledge-graph.md) | Cytoscape graph, concept detail, skill tree, pathfinding | [09-knowledge-graph.md](../../docs/student/09-knowledge-graph.md) | 4-6d | STU-W-04 | [STB-08](../student-backend/TASK-STB-08-knowledge-graph.md) |
| [STU-W-11](TASK-STU-W-11-challenges.md) | Daily challenge, boss battles menu, card chains, calendar, tournaments | [10-challenges.md](../../docs/student/10-challenges.md) | 3-4d | STU-W-06 | [STB-05](../student-backend/TASK-STB-05-challenges.md) |
| [STU-W-12](TASK-STU-W-12-social-learning.md) | Class feed, peer solutions, friends, leaderboards, study rooms | [11-social-learning.md](../../docs/student/11-social-learning.md) | 4-5d | STU-W-07 | [STB-06](../student-backend/TASK-STB-06-social.md) |
| [STU-W-13](TASK-STU-W-13-diagrams.md) | All 9 diagram renderers, PiP, annotations, export, measurement tools | [12-diagrams.md](../../docs/student/12-diagrams.md) | 4-6d | STU-W-06 | reuse existing ContentEndpoints |
| [STU-W-14](TASK-STU-W-14-notifications-profile-settings.md) | Notification center, profile, 6-tab settings | [13-notifications-profile.md](../../docs/student/13-notifications-profile.md) | 3-4d | STU-W-04 | [STB-07](../student-backend/TASK-STB-07-notifications.md) |

### Phase 4 — Web-Only Polish (parallelizable, ~6-10 days)

Non-blocking. Ship after or alongside Phase 3 for the full desktop/tablet experience.

| ID | Task | Feature Spec | Effort | Depends On | Backend |
|---|---|---|---|---|---|
| [STU-W-15](TASK-STU-W-15-web-enhancements.md) | Keyboard shortcuts, command palette, multi-pane, PiP windows, scratchpad, math keyboard, PWA, global search | [14-web-enhancements.md](../../docs/student/14-web-enhancements.md) | 6-10d | Phase 2 + parts of Phase 3 | none |

---

## Dependency Graph

```text
                       [Infra Prereqs]
                    DB-05 ── DB-06 ── DB-08
                               │
                               ▼
  ── Phase 1 ──────────────────┼──────────────────────────────────
                               │
                    STU-W-00 (scaffold)
                               │
                               ▼
                    STU-W-01 (design system)
                               │
                  ┌────────────┴────────────┐
                  ▼                         ▼
          STU-W-02 (shell)         STU-W-03 ($api + SignalR)
                  │                         │
                  └────────────┬────────────┘
                               │
  ── Phase 2 ──────────────────┼──────────────────────────────────
                               │
              ┌────────────────┼────────────────┐
              ▼                ▼                ▼
       STU-W-04 (auth)   STU-W-06 (session)
              │                │
              ▼                ▼
       STU-W-05 (home)   STU-W-07 (gamification)
              │                │
  ── Phase 3 ─┴────────────────┴──────────────────────────────────
              │
   ┌──────────┼─────────┬──────────┬──────────┬──────────┬────────┐
   ▼          ▼         ▼          ▼          ▼          ▼        ▼
STU-W-08  STU-W-09  STU-W-10  STU-W-11  STU-W-12  STU-W-13  STU-W-14
(tutor)   (progress)(graph)   (challs)  (social)  (diagrams)(notif)
   │          │         │          │          │          │        │
  ── Phase 4 ─┴─────────┴──────────┴──────────┴──────────┴────────┤
                                                                  │
                                                          STU-W-15
                                                          (web polish)
```

---

## Parallelism Plan

With 2-3 frontend engineers and the backend work happening in parallel:

| Calendar | Lead A | Lead B | Lead C |
|---|---|---|---|
| Week 1-2 | STU-W-00, 01 | (blocked) | (blocked) |
| Week 3 | STU-W-02 | STU-W-03 | (blocked) |
| Week 4-5 | STU-W-04, 05 | STU-W-06 (session, part 1) | STU-W-13 (diagrams) |
| Week 6-7 | STU-W-07 | STU-W-06 (part 2) | STU-W-10 (graph) |
| Week 8-9 | STU-W-08 (tutor) | STU-W-11 (challenges) | STU-W-09 (progress) |
| Week 10-11 | STU-W-12 (social) | STU-W-14 (notif+profile) | STU-W-15 (polish) |
| Week 12 | Polish, QA, Lighthouse, a11y | | |

Rough total: **~12 calendar weeks with 3 engineers**, assuming the backend team ships the `STB-*` endpoints on a parallel track without blocking.

---

## Cross-Cutting Concerns Every Task Must Handle

These apply to every `STU-W-*` task and should be in each PR's review checklist:

1. **Accessibility** — WCAG 2.1 AA on every interactive element. Axe-core CI check passes. Keyboard path tested.
2. **i18n** — No hardcoded strings; every user-visible text goes through `vue-i18n` with en / ar / he entries.
3. **RTL** — Layout flips correctly when locale is `ar` or `he`.
4. **Dark mode** — Every component has both theme variants; no hardcoded colors.
5. **Reduced motion** — Non-essential animation disabled when `prefers-reduced-motion: reduce`.
6. **Loading states** — Skeleton, not spinner, for initial render of lists and cards.
7. **Empty states** — Designed empty state with illustration + CTA, not blank.
8. **Error states** — Inline error + retry for API failures; toast + error boundary for unrecoverable.
9. **Realtime** — Any data that can update via SignalR should update via SignalR without a poll.
10. **Tests** — Vitest unit tests for composables and Pinia stores; Playwright E2E for critical paths.

---

## Task File Template

Each `TASK-STU-W-*.md` file follows a compact structure (unlike the deep infra task files, because the feature specs already carry the detail):

```markdown
# TASK-STU-W-NN: <title>

**Priority**: HIGH | MEDIUM | LOW
**Effort**: <range> days
**Phase**: 1 | 2 | 3 | 4
**Depends on**: <list of task IDs>
**Backend tasks**: <list of STB-* IDs or "none">
**Status**: Not Started

## Spec
Full feature specification lives in [docs/student/<doc>.md](../../docs/student/<doc>.md).
The acceptance criteria listed at the bottom of that file form this task's checklist.

## Scope
- In scope: <compact bullet list>
- Out of scope: <compact bullet list>

## Definition of Done
- [ ] All `STU-*` acceptance criteria in the linked feature doc pass
- [ ] Cross-cutting concerns from the task bundle README apply
- [ ] Integration tests added to Playwright suite for critical paths
- [ ] Lighthouse performance ≥ 90 on mid-range laptop profile
- [ ] Axe-core accessibility audit passes
- [ ] PR links the specific backend task(s) this depends on

## Risks
<compact bullet list>
```

This keeps the task files focused on **delivery context** (effort, dependencies, risks) while the [docs/student/](../../docs/student/) docs remain the source of truth for **what** to build.

---

## Related Task Bundles

- **[Student Backend](../student-backend/README.md)** — the `STB-*` endpoint tasks this bundle consumes
- **[Infra DB Migration](../../docs/tasks/infra-db-migration/README.md)** — prereqs `DB-05`, `DB-06`, `DB-08`
- **[Student AI Interaction](../../docs/tasks/student-ai-interaction/README.md)** — existing completed backend work that enables the tutor task (STU-W-08)
