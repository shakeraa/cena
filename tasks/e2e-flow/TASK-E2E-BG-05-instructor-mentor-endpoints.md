# TASK-E2E-BG-05: Backend gap — `/api/instructor/*` and `/api/mentor/institutes` return 404

**Status**: Proposed
**Priority**: P1
**Epic**: Backend gap fixes (EPIC-E2E-F teacher + EPIC-E2E-G admin)
**Tag**: `@backend-gap @admin-api @teacher @mentor @p1`
**Owner**: admin-api maintainers
**Surfaced by**: EPIC-G admin smoke

## Evidence

```
GET /api/instructor/*         → 404 Not Found
GET /api/mentor/institutes    → 404 Not Found
```

The admin SPA has full-fidelity pages at `/instructor` and `/mentor/institutes/[id]` and `/mentor` — the routes are wired in `vue-router`, the components mount, but their `onMounted` API calls all 404.

Per `EPIC-F-teacher-journey.spec.ts`, the TEACHER role today has **NO UI** — they can't sign in to admin SPA (role-gated) and there's no separate teacher SPA. The `/instructor` page is the closest thing to a teacher surface and it's dead.

Same for `/mentor` — institute mentors who'd manage classroom assignments have a designated landing page that 404s on first paint.

## What to investigate

1. Search for endpoint mappings:
   ```bash
   grep -rn "MapGroup.*instructor\|MapGet.*instructor\|MapGroup.*mentor" src/api/Cena.Admin.Api.Host/Endpoints/
   ```
2. Check whether the endpoint files exist but are not registered in `Program.cs`.
3. Compare against the existing `ClassroomEndpoints.cs` (which IS mapped at `/api/classrooms`) — same shape applies here.
4. Likely causes:
   - Endpoint file written, never wired up via `app.MapInstructorEndpoints()` in `Program.cs`
   - Endpoint group exists but at a different path (`/api/admin/instructor` vs `/api/instructor`); SPA points at the wrong path
   - Endpoints intended to live on a yet-to-build `cena-instructor-api` service (mirror of how student-api / admin-api split)

## Definition of done

- [ ] Decision documented: "do we ship instructor/mentor surfaces in admin-api, or stand up a separate service?" — short ADR if the answer is "separate service"
- [ ] Endpoints mapped at the path the SPA expects, returning real data
- [ ] Cross-tenant guard verified — instructor X cannot see institute Y's data
- [ ] Unit tests for at least: anonymous → 401, wrong role → 403, correct role → 200
- [ ] EPIC-G admin smoke allowlist entries for `/instructor` and `/mentor` REMOVED

## Why this is more than cosmetic

The teacher/instructor flow is a load-bearing future product surface (per the Bagrut-tutor-classroom narrative). Shipping the SPA pages without their backing endpoints is a **half-implementation** that the no-stubs memory explicitly bans. Either ship the back half or feature-flag the front half off.
