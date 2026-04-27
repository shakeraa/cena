# REPORT-EPIC-G — Admin operations (real-browser journey)

**Status**: ✅ green at the smoke level — admin can log in, /apps/permissions and /apps/moderation/queue render without crashing
**Date**: 2026-04-27
**Worker**: claude-1
**Spec file**: `src/student/full-version/tests/e2e-flow/workflows/EPIC-G-admin-journey.spec.ts`

## What this spec exercises

1. Drive admin-SPA `/login` form with seeded `admin@cena.local` (`DevAdmin123!`)
2. Wait for SPA to leave `/login`
3. Visit `/apps/permissions` and `/apps/moderation/queue` — both render without console errors and no uncaught JS exceptions

This is a **smoke-level** journey: it proves a SUPER_ADMIN can reach the admin shell + the gated surfaces without 5xx or console-error noise. It does NOT exercise per-cell interactions (promote user, approve queue item, CAS-override).

## Buttons clicked

- Admin SPA `/login`: `input[type="email"]`, `input[type="password"]`, `button[type="submit"]`
- Then plain `page.goto()` for two further pages

## API endpoints fired

The Vuetify SPA's auth shell + page bootstraps emit standard dev calls (no targeted assertion in this smoke pass).

## Gaps surfaced (queue these per-cell tests separately)

The original epic body lists G-01 through G-09 (Bagrut ingestion, parametric template, reference recreation, CAS override, moderation queue, cultural DLQ, LLM cost dashboard, RTBF admin, live monitor). All of those need either:

1. Seeded data in the dev stack the per-cell tests can act on (e.g. a moderation queue item, a CAS-failed question, a Bagrut ingest record), OR
2. Specialized fixtures that mint the data via direct DB writes / probe endpoints

This smoke spec doesn't claim to cover them — it's the regression catcher for "admin SPA at all". The deeper journeys are queued.

## Diagnostics

Console errors / page errors / failed requests collected — all empty.

## What's next

Per-cell tests for moderation-approve, CAS-override, RTBF, etc. require the data-seeding work above. Queue them under their respective `TASK-E2E-G-*` task IDs once dev seed exists.
