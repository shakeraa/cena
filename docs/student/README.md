# Cena Student Web Interface — Documentation

> **Scope**: Student-facing web application (`src/student/full-version/`) built on the same Vuexy Vue 3 template as the admin dashboard.
> **Status**: Specification — feeds task extraction.
> **Audience**: Engineers, product, QA, design.

---

## Purpose

This folder contains the complete specification for the **Cena Student Web App** — a Vue 3 / Vuetify / TypeScript single-page application that mirrors every learning feature we built for the Flutter mobile client, and adds capabilities unlocked by larger screens, keyboards, and multi-window workflows.

The documents are organized so that each feature file can produce a self-contained bundle of tasks (`STU-xxx`) with acceptance criteria, backend dependencies, and UI contracts.

---

## Index

| # | Document | Topic |
|---|----------|-------|
| 00 | [Overview & Architecture](00-overview.md) | Product vision, stack, template reuse, high-level architecture |
| 01 | [Navigation & IA](01-navigation-and-ia.md) | Sitemap, routes, nav structure, breadcrumbs |
| 02 | [Design System](02-design-system.md) | Vuexy theme, colors, typography, layouts, i18n/RTL, a11y |
| 03 | [Auth & Onboarding](03-auth-onboarding.md) | Sign-in, registration, role selection, first-run wizard |
| 04 | [Home Dashboard](04-home-dashboard.md) | Home page, widgets, daily plan, review-due, quick actions |
| 05 | [Learning Session](05-learning-session.md) | Core session: question flow, hints, feedback, flow monitor, deep study, boss battles |
| 06 | [Gamification](06-gamification.md) | XP, levels, streaks, badges, leaderboards, quests, celebrations |
| 07 | [AI Tutor](07-ai-tutor.md) | Conversational tutor, chat UI, voice, context sharing |
| 08 | [Progress & Mastery](08-progress-mastery.md) | Mastery dashboard, learning time, analytics, session history |
| 09 | [Knowledge Graph](09-knowledge-graph.md) | Concept graph, skill tree, prerequisites, recommended path |
| 10 | [Challenges](10-challenges.md) | Card chains, daily/weekly challenges, boss battles menu |
| 11 | [Social Learning](11-social-learning.md) | Class feed, peer solutions, co-op study, friends |
| 12 | [Diagrams & Interactives](12-diagrams.md) | Rive viewer, comparative diagrams, simulations, whiteboard |
| 13 | [Notifications & Profile](13-notifications-profile.md) | Notification center, profile, settings, preferences |
| 14 | [Web Enhancements](14-web-enhancements.md) | Web-only features: keyboard shortcuts, multi-pane, PWA, print, export |
| 15 | [Backend Integration](15-backend-integration.md) | REST + SignalR contracts, auth, rate limits, offline sync |

---

## Reading Order

- **First-time reader** → start at [00-overview](00-overview.md), then skim [01-navigation](01-navigation-and-ia.md).
- **Feature implementer** → read the overview, then jump straight to the feature doc(s) you own.
- **Backend engineer** → start at [15-backend-integration](15-backend-integration.md), cross-reference features.
- **Designer** → [02-design-system](02-design-system.md) + the feature docs that include wireframes.
- **PM / task extraction** → scan the "Acceptance Criteria" sections of each feature doc. Each bullet is a candidate task.

---

## Conventions Used in These Docs

- **Task IDs** use the prefix `STU-` (Student Web). Example: `STU-SES-001` = Learning Session task 001.
- **Mobile parity** sections reference the Flutter source (`src/mobile/lib/features/...`) so web implementers can see the behavioral spec already in production.
- **Web-only** sections mark features that do not exist on mobile and must be designed from scratch.
- **Acceptance Criteria** bullets are written to be mechanically convertible into tasks: each starts with a verb and describes an observable outcome.
- **API contracts** are linked to the canonical C# endpoint file in `src/api/Cena.Api.Host/Endpoints/`.

---

## Related Documents (Outside This Folder)

- [docs/mobile-tasks.md](../mobile-tasks.md) — Mobile task registry (parity source of truth)
- [docs/api-contracts.md](../api-contracts.md) — Backend API contracts
- [docs/adaptive-learning-architecture-research.md](../adaptive-learning-architecture-research.md) — Pedagogical research that drives feature design
- [docs/learning-methodology-strategy.md](../learning-methodology-strategy.md) — Learning science strategy
- [docs/design/](../design/) — Design assets and wireframes
- [src/admin/full-version/](../../src/admin/full-version/) — Reference template
