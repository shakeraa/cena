# 00 — Overview & Architecture

## Product Vision

The Cena Student Web App is the **primary web surface** for learners on laptops, desktops, tablets (landscape), and Chromebooks. It delivers the same adaptive learning experience as the Flutter mobile client, tuned for larger screens, keyboard-first interaction, and longer study sessions.

Target users:

| Segment | Primary use-case | Session length |
|---------|------------------|----------------|
| School students (grade 5–12) | Assigned homework + class challenges | 20–45 min |
| Self-directed learners (university / adult) | Deep study on topics of choice | 45–90 min |
| Tutored students | Guided sessions with AI tutor + progress review | 30–60 min |
| Test-prep students | Exam simulations, timed challenges | 60–120 min |

Non-goals:

- This is **not** a replacement for the admin dashboard — admins use `src/admin/full-version/`.
- This is **not** a content authoring tool — authors use the ingestion + moderation pages in the admin UI.
- This is **not** a teacher grading tool — teachers use a separate future surface (out of scope for this doc).

---

## Stack & Template

The student web app reuses the **Vuexy Vue 3 admin template** already vendored under `src/admin/full-version/` to keep the look & feel consistent across internal tools and guarantee design-system parity.

| Layer | Technology |
|-------|------------|
| Framework | Vue 3 (Composition API, `<script setup>`) |
| UI library | Vuetify 3 (Vuexy theme) |
| Build tool | Vite |
| Language | TypeScript (strict) |
| Routing | `unplugin-vue-router` (file-based, typed) |
| State | Pinia |
| HTTP | `ofetch` + `$api` wrapper (same pattern as admin) |
| Realtime | `@microsoft/signalr` to `/hub/cena` |
| Auth | Firebase Auth (`cena-platform` project) + JWT forwarded to backend |
| i18n | `vue-i18n` (English primary, Arabic/Hebrew secondary, Hebrew hideable outside Israel) |
| Icons | `@iconify/vue` + Tabler icons (matches admin) |
| Charts | ApexCharts (matches admin dashboards) |
| Graph viz | Cytoscape.js (knowledge graph) |
| Animation | `@rive-app/canvas` (for interactive diagrams), Vue Motion for micro-interactions |
| Math rendering | KaTeX (same as mobile's `math_text.dart`) |
| Tests | Vitest (unit) + Playwright (E2E) |

---

## Template Reuse Strategy

**Copy, don't fork.** Create a new directory `src/student/full-version/` by copying the Vuexy starter shell (not the admin pages) and then selectively lifting the building blocks the student app needs.

Reused from admin:
- `src/admin/full-version/src/@core/` — the Vuexy design system primitives (do not duplicate; reference or extract to a shared package later)
- `src/admin/full-version/src/@layouts/` — layout shell
- `src/admin/full-version/src/plugins/vuetify/` — theme, colors, typography
- `src/admin/full-version/src/plugins/iconify/` — icon presets
- `src/admin/full-version/src/utils/` — API client, auth helpers, date formatters

Not reused (student has its own):
- `src/admin/full-version/src/pages/apps/` — admin pages; student builds its own
- `src/admin/full-version/src/navigation/` — admin nav; student nav is a fresh file
- `src/admin/full-version/src/stores/` — admin-specific stores

**Decision point (needs explicit agreement before implementation):** extract `@core`, `@layouts`, and `plugins/` into a shared package `packages/vuexy-core/` so admin and student pull from the same source of truth, OR keep the student repo self-contained by copying everything. Recommendation: extract to a shared package to avoid drift.

---

## High-Level Architecture

```
┌────────────────────────────────────────────────────────────────┐
│  Browser (Student Web SPA)                                     │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │  Vue 3 + Vuetify (Vuexy theme)                           │  │
│  │    Pages  →  Composables  →  Pinia Stores                │  │
│  │               │                  │                        │  │
│  │               ▼                  ▼                        │  │
│  │         REST ($api)        SignalR (signalrClient)       │  │
│  └──────────────────────────────────────────────────────────┘  │
│         │ HTTPS                          │ WSS                │
└─────────┼──────────────────────────────────┼──────────────────┘
          │                                  │
          ▼                                  ▼
   /api/* endpoints                   /hub/cena
   (SessionEndpoints, ContentEndpoints,    (CenaHub.cs)
    StudentAnalyticsEndpoints, ...)        JWT auth, rate-limit,
                                           BusEnvelope wrapping
          │                                  │
          └───────────┬──────────────────────┘
                      ▼
           ┌────────────────────┐
           │  Cena.Api.Host     │  Marten (event-sourced)
           └────────────────────┘
                      │ NATS publish/subscribe
                      ▼
           ┌────────────────────┐
           │  Cena.Actors       │  Proto.Actor cluster
           │   StudentActor     │  Virtual actors per student
           │   SessionActor     │  Live session state
           └────────────────────┘
```

Student web uses the **exact same backend contracts** as the Flutter mobile client — no student-specific backend surface is added. Any new feature in the student web that requires backend work must add endpoints that mobile will also consume.

---

## Authentication Flow

1. User opens the student app → sees login page ([Auth & Onboarding](03-auth-onboarding.md)).
2. Firebase Auth handles sign-in (email/password, Google, Apple, phone).
3. Student app receives a Firebase ID token.
4. Every REST call includes `Authorization: Bearer <firebaseIdToken>`.
5. SignalR connection includes the same token in the `access_token` query param.
6. Backend validates via `Firebase.Auth` middleware and issues per-request claims.
7. `ResourceOwnershipGuard` ensures a student can only access their own data.

Token refresh is handled transparently by the Firebase SDK; `$api` interceptor re-reads the current token on every request.

---

## Offline & Resilience

Web does **not** need full offline support like mobile (no sync queue, no local DB) because sessions are short and connectivity is assumed. However:

- **Resilient reconnection**: SignalR client reconnects with exponential backoff if the WS drops mid-session; in-flight answers are queued and replayed.
- **Session resume**: If the tab is closed or refreshed during an active session, the student can resume via `/api/sessions/active` → `POST /api/sessions/{id}/resume`.
- **Draft answers**: Long-form text answers (teach-back, explain-your-thinking) are saved to `localStorage` every 5 seconds until submitted.
- **Optimistic UI**: XP gain, streak updates, and mastery bumps render immediately and roll back if the server rejects them.

---

## Performance Budgets

| Metric | Target |
|--------|--------|
| Initial bundle (gzipped) | ≤ 350 KB |
| Time-to-interactive (mid-range laptop, 3G) | ≤ 3 s |
| Session question transition | ≤ 150 ms |
| SignalR event → UI update | ≤ 50 ms |
| Knowledge graph render (500 nodes) | ≤ 500 ms |
| Chart render (ApexCharts, 30 data points) | ≤ 200 ms |

Code-splitting strategy: every feature in `pages/apps/*` becomes its own route-level chunk. The learning session module is the only chunk allowed to exceed 200 KB.

---

## Out of Scope for v1

The following are explicitly deferred to keep v1 shippable:

- Live video tutoring (teacher ↔ student video calls)
- Peer-to-peer video study rooms
- Handwriting input (stylus math)
- Native desktop app wrapper (Electron / Tauri)
- Full offline PWA with background sync
- Embedded IDE for programming challenges

These are documented as stretch goals in [14-web-enhancements](14-web-enhancements.md) so they can be picked up in later releases.

---

## Related Docs

- [01-navigation-and-ia.md](01-navigation-and-ia.md) — page inventory
- [02-design-system.md](02-design-system.md) — theme and layout rules
- [15-backend-integration.md](15-backend-integration.md) — API contracts
