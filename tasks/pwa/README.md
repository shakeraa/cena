# PWA Tasks — Cena Student App

> **Decision**: PWA replaces Flutter for mobile (2026-04-13)
> **Comparison docs**: `docs/research/cena-mobile-pwa-approach.md` / `docs/research/cena-mobile-flutter-approach.md`
> **Targets**: Desktop (1200px+), Tablet (768-1199px), Mobile (320-767px) — all from one Vue 3 codebase

## Task Index

### Foundation (Do First)

| Task | Title | Depends On | Complexity |
|------|-------|-----------|------------|
| [PWA-001](PWA-001-vite-pwa-plugin-service-worker.md) | Service Worker + Workbox | — | Medium |
| [PWA-002](PWA-002-web-app-manifest.md) | Web App Manifest + Install | PWA-001 | Medium |
| [PWA-003](PWA-003-viewport-touch-mobile-ux.md) | Viewport, Touch, Mobile UX | — | Medium |
| [PWA-004](PWA-004-session-persistence-signalr-reconnect.md) | Session Persistence + Reconnect | — | High |

### Features (After Foundation)

| Task | Title | Depends On | Complexity |
|------|-------|-----------|------------|
| [PWA-005](PWA-005-offline-question-cache.md) | Offline Question Cache | PWA-001, PWA-004 | Medium |
| [PWA-006](PWA-006-camera-photo-ingestion.md) | Camera + Photo Ingestion | PWA-001 | High |
| [PWA-007](PWA-007-figures-mobile-rendering.md) | Figures — Mobile Touch | PWA-003 | High |
| [PWA-008](PWA-008-responsive-layout-desktop-tablet-mobile.md) | Responsive Layout (All Devices) | PWA-003 | High |

### Distribution & Quality (After Features)

| Task | Title | Depends On | Complexity |
|------|-------|-----------|------------|
| [PWA-009](PWA-009-push-notifications.md) | Web Push Notifications | PWA-001, PWA-002 | Medium |
| [PWA-010](PWA-010-twa-android-app-store.md) | TWA — Android Play Store | PWA-002 | Low |
| [PWA-011](PWA-011-cross-device-testing-matrix.md) | Testing Matrix + CI | All | High |
| [PWA-012](PWA-012-capacitor-escape-hatch.md) | Capacitor Escape Hatch (GATED) | — | Low (prep) |

## Execution Order

```
Phase 1 — Foundation (parallel):
  PWA-001 (Service Worker)  ─┐
  PWA-003 (Viewport/Touch)  ─┼── can run in parallel
  PWA-004 (Session Persist)  ─┘

Phase 2 — Install & Manifest:
  PWA-002 (Manifest + Install)  ← needs PWA-001

Phase 3 — Features (parallel):
  PWA-005 (Offline Cache)    ─┐
  PWA-006 (Camera)           ─┼── can run in parallel
  PWA-007 (Figures Mobile)   ─┤
  PWA-008 (Responsive)       ─┘

Phase 4 — Distribution:
  PWA-009 (Push)             ─┐
  PWA-010 (TWA Android)      ─┘── can run in parallel

Phase 5 — Quality:
  PWA-011 (Testing Matrix)   ← needs all above

Phase 6 — Escape Hatch (if needed):
  PWA-012 (Capacitor)        ← DECISION-GATED, prep only
```

## Quality Bar

Every task requires:
- **No stubs, no fakes, no "Phase 2" deferred patterns** — production-grade from day one
- **Architect-level thinking** — trace data flows, question design, challenge assumptions, verify at system boundaries
- **Real device testing** — emulators are necessary but not sufficient
- **RTL verification** — Arabic and Hebrew at every breakpoint
- **Accessibility** — axe-core clean, WCAG 2.1 AA minimum
- **i18n** — all user-facing strings in Arabic and Hebrew

## Subdirectories

```
tasks/pwa/
├── service-worker/     — PWA-001 working files
├── manifest-install/   — PWA-002 working files
├── offline/            — PWA-005 working files
├── camera-photo/       — PWA-006 working files
├── figures-mobile/     — PWA-007 working files
├── rtl-mobile/         — RTL-specific working files
├── testing/            — PWA-011 working files
├── deployment/         — PWA-010, PWA-012 working files
└── done/               — Completed tasks
```
