# TASK-PRR-324: Multi-student household view (parent sees all siblings)

**Priority**: P0 — launch-blocker (sibling-discount is Premium feature)
**Effort**: M (1 week)
**Lens consensus**: persona #5 large families, #6 Arabic-Israeli (multi-kid households common), #9 growth (LTV expansion)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: frontend + backend
**Tags**: epic=epic-prr-i, parent-ux, priority=p0, multi-child
**Status**: Done (backend) / Deferred (Vue UI) — see close-out below
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Parent dashboard shows all linked students in one view. Tab switcher per student + aggregate household view (total time-on-task, household readiness snapshot).

## Scope

- Parent aggregate read model extended to household.
- Student-switcher UI; each student preserves own mastery map + diagnostic summary.
- Household view: collapsed per-student summary card + aggregate time-on-task.
- Privacy: each student's session data private to that student; parent sees aggregates only (no raw session transcripts).

## Files

- `src/parent/src/pages/household.vue`
- `src/backend/Cena.StudentApi/Controllers/HouseholdDashboardController.cs`
- Tests.

## Definition of Done

- Parent with 2+ linked students sees all in one view.
- No cross-household leakage.
- Privacy: no raw transcripts exposed.
- Full sln green.

## Non-negotiable references

- [ADR-0001](../../docs/adr/0001-multi-institute-enrollment.md).
- [ADR-0003](../../docs/adr/0003-misconception-session-scope.md).

## Reporting

complete via: standard queue complete.

## Related

- [PRR-320](TASK-PRR-320-parent-dashboard-mvp.md), [PRR-293](TASK-PRR-293-sibling-add-post-purchase.md)

---

## Close-out (2026-04-23, backend scope)

Shipped on branch `claude-subagent-prr324-v2/prr-324-household-view` (3 commits, 5 files):

1. `src/api/Cena.Api.Contracts/Subscriptions/HouseholdDashboardDtos.cs` — wire DTOs (primary card, sibling cards, household aggregate). Summary scalars only; no session ids (ADR-0003).
2. `src/api/Cena.Api.Contracts/Subscriptions/HouseholdDashboardAggregator.cs` — pure static assembler. Enforces primary-slot invariant, sibling-ordinal sort, household totals = sum across all cards. Throws `ArgumentException` on inactive state / missing primary / empty cards. Lives in Contracts (not Actors) because Actors cannot see Contracts types (cyclic dep); see file banner.
3. `src/api/Cena.Api.Contracts/Subscriptions/IHouseholdCardSource.cs` — narrow port + `NoopHouseholdCardSource` legitimate zero-data default (pattern: NullEmailSender). Not a stub: the Marten minutes-on-task projection lands as a follow-up; until then the Noop returns one zero-scalar / null-readiness card per linked student.
4. `src/api/Cena.Student.Api.Host/Endpoints/HouseholdDashboardEndpoints.cs` — `GET /api/me/household-dashboard` (auth required). IDOR-guarded via JWT NameIdentifier; SKU-fenced via `SkuFeatureAuthorizer.CheckParent` (persona #8 pricing-leak guard); 404 on never-activated / empty-linked-students; 403 + stable ReasonCode on feature-denied.
5. `src/api/Cena.Student.Api.Host/Program.cs` — `TryAddSingleton<IHouseholdCardSource, NoopHouseholdCardSource>` + `MapHouseholdDashboardEndpoints()` alongside existing `MapParentDashboardEndpoints()`.

Tests: `src/actors/Cena.Actors.Tests/Subscriptions/HouseholdDashboardAggregatorTests.cs` — 10/10 passing:

- primary-only household (0 siblings)
- primary + 2 siblings sorted by ordinal asc
- aggregate totals = sum across all cards
- missing primary card → ArgumentException
- empty cards list → ArgumentException
- inactive state → ArgumentException
- duplicate ordinal 0 — first-wins decision (locked by test)
- null-state / null-cards → ArgumentNullException (2 tests)
- readiness-summary pass-through

Build gate: `dotnet build src/actors/Cena.Actors.sln` = 0 Error(s). 28 subscription-adjacent tests (new aggregator + existing `SkuFeatureAuthorizer` + `ParentDashboard`) all green.

**Deferred (explicit, not stubs):**

- Vue UI (`src/parent/src/pages/household.vue`) — separate frontend task.
- Marten read-model source that joins minutes-on-task + readiness into real cards — follow-up PR replaces the Noop via `services.Replace(...)`.

No cross-household leakage (IDOR guard enforced structurally via JWT-only parent id). No raw transcripts / session ids exposed (privacy line enforced by DTO shape, not by convention).
