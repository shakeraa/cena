# TASK-STB-02: Plan, Review Queue, Recommendations

**Priority**: HIGH — powers the home dashboard
**Effort**: 3-4 days
**Depends on**: [STB-00](TASK-STB-00-me-profile-onboarding.md)
**UI consumers**: [STU-W-05](../student-web/TASK-STU-W-05-home-dashboard.md)
**Status**: Not Started

---

## Goal

Provide the home dashboard with three data sources it currently has no endpoints for: today's planned study time, SRS review queue, and a ranked list of recommended next sessions.

## Endpoints

| Method | Path | Purpose | Rate limit | Auth |
|---|---|---|---|---|
| `GET` | `/api/me/plan/today` | Today's planned blocks based on student goal + calendar | `api` | JWT |
| `GET` | `/api/review/due` | SRS review cards due now | `api` | JWT |
| `GET` | `/api/recommendations/sessions` | Next-best session recommendations | `api` | JWT |

Responses:

```json
// /api/me/plan/today
{
  "date": "2026-04-10",
  "targetMinutes": 30,
  "completedMinutes": 12,
  "blocks": [
    { "index": 0, "subject": "math", "targetMinutes": 15, "status": "completed" },
    { "index": 1, "subject": "physics", "targetMinutes": 15, "status": "pending" }
  ]
}

// /api/review/due
{
  "totalDue": 7,
  "oldestDueAt": "2026-04-06T09:00:00Z",
  "bySubject": [
    { "subject": "math", "count": 4 },
    { "subject": "physics", "count": 3 }
  ],
  "next10": [ /* question IDs + due timestamps */ ]
}

// /api/recommendations/sessions
{
  "items": [
    {
      "subject": "math",
      "concept": "quadratic-formula",
      "reason": "low-mastery|due-review|on-learning-path|streak-match",
      "estimatedMinutes": 15,
      "expectedXp": 120,
      "difficulty": 0.62
    }
  ]
}
```

## Data Access

- **Reads**: `StudentProfileSnapshot`, per-concept mastery projection, SRS due queue projection (new), session history
- **Writes**: none
- **Async projections**: this is where the real work happens. Instead of computing recommendations on each request (which would blow the 5 s timeout), Marten async projections run in the background and maintain:
  - `StudentPlanSnapshotProjection` — per-student today's plan, updated on `SessionEnded`
  - `ReviewQueueProjection` — SRS queue maintained from `ConceptMastered` / `MasteryDecayed` events
  - `RecommendationProjection` — per-student recommendation list, updated on mastery events

  Enable the commented-out projections in [MartenConfiguration.cs:170-176](../../src/actors/Cena.Actors/Configuration/MartenConfiguration.cs#L170) and extend with the three above. Each GET becomes a cheap document lookup.

- **Statement timeout**: each endpoint is a primary-key lookup on a projection document — well under 100 ms

## Hub Events

- `PlanUpdated` — pushed when a session ends and the plan recomputes
- `ReviewDueChanged` — pushed when a new card becomes due (cron-driven)

Both are additive and land in STB-10.

## Contracts

Add to `Cena.Api.Contracts/Dtos/Plan/`, `Cena.Api.Contracts/Dtos/Review/`, `Cena.Api.Contracts/Dtos/Recommendations/`:

- `PlanTodayDto`, `PlanBlockDto`
- `ReviewDueDto`, `SubjectReviewCountDto`, `ReviewCardPreviewDto`
- `SessionRecommendationDto` with `reason` enum

## Auth & Authorization

- Firebase JWT
- `ResourceOwnershipGuard.VerifyStudentAccess` on every endpoint
- No cross-student reads

## Cross-Cutting

- All three endpoints are cacheable with `ETag` + `Cache-Control: private, max-age=60`
- Respond with `304 Not Modified` on matching `If-None-Match`
- Handler logs with `correlationId`
- The projections must be idempotent and resilient to out-of-order events

## Definition of Done

- [ ] Three endpoints implemented and registered in `Cena.Student.Api.Host`
- [ ] DTOs in `Cena.Api.Contracts`
- [ ] Three Marten async projections enabled and producing correct data against a seeded test stream
- [ ] Projection rebuild tested against a full-stream replay
- [ ] Integration tests cover: no-plan student, active-plan student, all-caught-up on review, recommendation-reason variants
- [ ] ETag caching verified with repeated requests
- [ ] OpenAPI spec updated
- [ ] TypeScript types regenerated
- [ ] Mobile lead review — confirm mobile home screen can switch from its current sources to these endpoints without regressions

## Out of Scope

- The actual SRS algorithm — existing in the mastery engine
- Recommendation ranking research — use a simple rule-based ranker for v1 (low-mastery first, then due-review, then on-path)
- Schedule editor UI — students don't set block schedules in v1; the plan is derived from the daily goal
- Calendar integration with external services — out of scope
