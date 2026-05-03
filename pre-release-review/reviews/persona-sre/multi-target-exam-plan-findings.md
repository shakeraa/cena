---
persona: sre
subject: MULTI-TARGET-EXAM-PLAN-001
date: 2026-04-21
verdict: yellow
---

## Summary

Multi-target is the right data model, but the ops story is undercooked in three specific places that will bite on a Bagrut morning: (1) catalog hot-path has no defined cache semantics or CDN-failure runbook, (2) first-login migration has no declared retry/DLQ path so an upcast exception silently locks affected students out, and (3) SAT+PET v1 inclusion roughly doubles the capacity envelope and moves exam-day risk from a single June/Aug Israeli spike to a **quarterly compound spike**, which PRR-053's "5-10x baseline" model does not cover. Green on the data model; yellow on ops because the three gaps above are each a 03:00-on-Bagrut-morning failure I can describe but cannot yet point at a runbook for. Fixable with three new PRR tasks and an amendment to PRR-053, not a blocker to the ADR.

## Section 9.7 answers

**Catalog hot-path**: Yes — `GET /api/catalog/exams` fires on every session-start for anyone whose client-side cache is cold (new device, cleared storage, first app update). Expected read pattern at steady state: ~1 req/student/day (session-start) + onboarding spike (1 req per wizard load). At 50k DAU baseline that's 50k/day, trivial. At exam-week spike (PRR-053's 5-10x), catalog is one of 8–12 must-hit endpoints at session-start and will saturate whichever layer is weakest.

Cache strategy should be: (a) static JSON asset at `/catalog/exams/v{N}.json` served from CDN with `Cache-Control: public, max-age=86400, stale-while-revalidate=604800`, payload ~4–8 KB gzipped for 8 exams × 3 locales × metadata (rough: 8 entries × ~500 bytes per locale × 3 locales = 12 KB uncompressed, <5 KB gzipped); (b) version pin carried in session-start response so clients only refetch when `catalogVersion` changes; (c) origin fallback through the main API with a 60s in-memory LRU on the API side. **If CDN goes down 08:00 on June 15 Bagrut Math 5U morning**: last-good JSON is pinned in the app's IndexedDB/localStorage keyed by `catalogVersion` → onboarding is unaffected for returning users; new signups on that morning (rare, Bagrut morning is not a signup moment) either get the origin fallback or, worst case, a hardcoded minimal fallback catalog compiled into the client for `BAGRUT_MATH/PHYSICS/CHEMISTRY/BIOLOGY/ENGLISH/CS` so Bagrut morning is never blocked on a network path. This fallback-catalog-in-client is **currently undefined** — see new PRR task below.

**Refresh on new exam added**: bump `catalogVersion`, invalidate CDN path, next session-start carries new version, client refetches. No hot-reload needed since catalog additions are low-frequency (weeks/months).

**Migration failure mode (section 7)**: Not described in the brief. Required behavior: upcast runs inside a Marten session as part of the aggregate's `Load` path; if it throws (bad `grade` claim format, missing track for a non-null-guarded exam, serialization skew mid-deploy), the student currently cannot load their aggregate → full app lockout. Needed: (a) upcast wrapped in try/catch that, on failure, writes a `PlanMigrationFailed` event with the raw legacy payload preserved, and returns an empty `StudentPlan` so the student is routed to the onboarding wizard's "add a target" path (graceful degrade, no lockout); (b) a daily reconciliation job replays `PlanMigrationFailed` events against the current upcaster so fixes ship without per-student intervention; (c) idempotency key = student ID + legacy config hash so re-deploys don't double-upcast. **Blast radius** if unguarded: 100% of pre-multi-target users on first login after deploy — easily 1–5k students depending on launch timing. This needs to be in the task, not a post-mortem.

**Event volume**: `ExamTargetUpdated` on every slider tick would be catastrophic — a student dragging weekly-hours 1→40 emits 40 events in 3 seconds. Debounce **must** be server-side (400ms tail on the write endpoint) not client-side, because a scripted client bypasses client debounce (redteam concern). Expected steady-state rate with server debounce: ~0.5 `ExamTargetUpdated`/student/month (real editing is rare), ~1.2 `ExamTargetAdded`/student/lifetime (average 1.2 targets per student based on the persona table), ~0.3 `ExamTargetArchived`/student/year. For 100k students over 3 years = ~450k `ExamTarget*` events total — **nothing** for Marten on Postgres; the event store will notice this only in pg_stat. Compaction/archival: don't bother; these events are authoritative plan history and are cheap. Add a standing `pg_stat_user_tables` check on `mt_events` row-count growth as part of PRR-020 equivalent observability.

**Exam-day freeze (PRR-016) interaction**: Catalog edits during Bagrut week must be **blocked by default** by the freeze window. A catalog change pushes a new `catalogVersion` → forces every active client to refetch → in exam week this is both unnecessary risk (new bug surface) and unnecessary load (refetch storm). Exception path: `BAGRUT_MATH` sitting-date correction (Ministry moved a moed) — that is a content-only correction, not a code deploy, and needs a named break-glass owner. Recommend: catalog-change PR template requires "freeze-window impact: none | needs-break-glass" checkbox, and human-architect (or on-call designate) owns the decision. Do **not** leave this implicit.

**Time-zone correctness**: This is where we will ship a bug if we're not careful. Deadlines stored as `DateTimeOffset` (UTC) is fine; the 14-day exam-week rule (section 6) and the "next natural sitting" date-picker pre-fill are the danger zones. A student in IST (UTC+2/+3) on June 14 at 23:30 local has a June 15 exam — the 14-day rule must evaluate in the student's declared timezone, not UTC, otherwise a Bagrut candidate in Tel Aviv loses exam-week lock 2 hours early. Diaspora students (UTC-8 US west coast) prepping for a Bagrut makeup compound this. Testing: property-based tests with student timezones drawn from `{IST, UTC, UTC-8, UTC-5, UTC+10}`, deadlines at midnight boundaries ± 1 min, and the 14-day rule assertion that "exam is within 14 days in student's local date" — not UTC. Store the student's declared timezone on the plan aggregate; don't infer from IP.

## Additional findings

**Scheduler determinism (section 6)**: `seed = hash(userId, dayOfYear)` is fragile. `dayOfYear` resets at Dec 31→Jan 1 UTC, which causes a discontinuity at that boundary regardless of local time, and for IST students the UTC day boundary is 02:00/03:00 local — exactly the 03:00-on-Bagrut-morning regime. A student who studies 23:00–02:00 IST on Dec 31 will see their picker swap mid-session. Recommend: `seed = hash(userId, localDateISO, activeTargetSet)` where `localDateISO` is YYYY-MM-DD in the student's declared timezone. Add a session_id tiebreaker in log lines so we can replay a picker decision in incident review. Without the active-target-set component, adding a new target mid-day also shifts seeds; with it, the shift is intentional and correlated with the user action.

**Redis session store (PRR-020) interaction**: The 14-day exam-week lock reads `ActiveExamTargetId` per session-start. If Redis evicts this (memory pressure), the picker falls back to weighted round-robin, which during exam week silently violates the intended behavior. PRR-020's eviction alert is now load-bearing on plan correctness, not just misconception scope. Amend PRR-020's scope to include plan-aggregate projection keys.

**Mashov sync (PRR-039) interaction**: Classroom-assigned plans (section 9.5, deferred to v2) are a Mashov-adjacent feature. If/when added, staleness of the classroom→target mapping becomes a correctness issue. Park this; just note that v2 inherits PRR-039's circuit breaker.

**Observability gaps**: no metric yet defined for (a) catalog fetch p95 from CDN vs origin, (b) `PlanMigrationFailed` event rate, (c) scheduler picker decisions broken down by rule (deadline-proximity vs round-robin vs override), (d) upcast cache-hit rate on session-start.

## Section 10 positions

- **SAT+PET v1 — capacity implication**: This is a real ops delta, not just a content-engineering delta. Bagrut exam-day spike is June/August IST-clustered. SAT is quarterly US-time-zone clustered (Mar/May/Aug/Oct/Dec). PET is Apr/Jul/Sep/Dec. In v1 we now have **~7 distinct exam-week spike windows per year**, not 2, with diaspora student load at different local times. PRR-053's "5-10x baseline, June focus" model is stale. Recommend amending PRR-053 before launch to forecast a compound traffic calendar: 12 exam-adjacent weeks spread across the year, peak spike still June, but the baseline "exam-adjacent" floor is now ~3x baseline for roughly half the calendar. This is a capacity-plan rewrite, not a tweak.
- **Max targets cap**: 4 is fine from an SRE angle; each additional target adds one catalog entry, O(1) storage, O(log n) picker cost. Raising to 6 costs nothing until cap × users × events/target stops being trivial, which it isn't at these scales. Decision is product, not SRE.
- **Free-text note field**: SRE-neutral. Storage cost negligible. Concern is privacy/redteam, not mine.

## Recommended new PRR tasks

1. **PRR-217** — Catalog API cache + CDN fallback runbook. Deliverables: `Cache-Control` policy, `catalogVersion` pin, IndexedDB-backed client cache, hardcoded minimal-catalog client fallback for Bagrut core subjects, runbook for "CDN down on Bagrut morning." Owner: human-architect + kimi-coder. Effort: S.
2. **PRR-218** — `StudentPlanConfig` → `StudentPlan` migration safety net. Deliverables: try/catch upcaster, `PlanMigrationFailed` event, reconciliation job, idempotency key, dashboard. Blocks launch. Owner: kimi-coder. Effort: S.
3. **PRR-219** — Amend PRR-053 with SAT+PET exam calendar. Deliverables: compound-spike traffic forecast covering 7 exam windows/year, updated k6 scenarios, auto-scale policy verified under quarterly SAT+PET overlap. Owner: human-architect. Effort: M. **Amends existing task, does not replace.**
4. **PRR-220** — Scheduler determinism + timezone correctness. Deliverables: seed = hash(userId, localDateISO, activeTargetSet), student-timezone on plan aggregate, property-based tests covering midnight boundaries across 5 timezones, structured logging of picker decisions. Owner: claude-subagent. Effort: S.
5. **PRR-221** — Observability for plan aggregate. Deliverables: metrics for catalog fetch path, `PlanMigrationFailed` rate, picker decision breakdown, upcast cache hit rate, Grafana panel. Owner: kimi-coder. Effort: S.
6. **PRR-222** — Extend PRR-020 scope to plan-aggregate Redis keys (eviction = correctness incident for exam-week lock). Owner: kimi-coder. Effort: XS.

## Blockers / non-negotiables

- **Hard blocker**: PRR-218 (migration safety net) must ship with or before the multi-target deploy. Unguarded upcast on first login = mass lockout.
- **Hard blocker**: Server-side debounce on `ExamTargetUpdated` before any slider UI ships. Client-only debounce is redteam-bypassable and will blow up the event store under adversarial load.
- **Soft blocker**: PRR-053 amendment (PRR-219) before launch. We do not currently know whether the SAT+PET calendar fits the existing auto-scale envelope. Need a load test before we find out on October 5 SAT morning.
- **Freeze-window policy**: catalog edits during exam weeks need a named break-glass owner. Not a blocker but leaving it implicit is how we ship a regression on a Bagrut Saturday.

## Questions back to decision-holder

1. For PRR-218's degrade path ("empty plan → routed to wizard"): is that acceptable product UX, or do you want a retry-with-banner flow instead? SRE prefers degrade-to-wizard; product may disagree.
2. Fallback-catalog compiled into the client (PRR-217): which subjects? Recommend all 6 Bagrut subjects + PET + SAT so the full v1 catalog is offline-survivable, but this adds ~8 KB to the bundle. Acceptable?
3. Catalog-edit during exam-week freeze — who is the named break-glass owner? If not human-architect, name the role.
4. Declared student timezone: required at onboarding, or inferred from browser and editable later? Required is safer; infer-and-edit is lower friction. Your call.
5. PRR-053 amendment: is October 2026 SAT the first real v1 exam-day at scale, or does the launch timeline push that out? Determines how urgent PRR-219 really is.
