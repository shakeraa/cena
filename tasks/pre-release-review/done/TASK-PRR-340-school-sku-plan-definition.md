# TASK-PRR-340: B2B school SKU — plan definition + admin dashboard

**Priority**: P1 (launch+1 acceptable; but critical to avoid pricing leaks if any school pilot near)
**Effort**: L (3-4 weeks)
**Lens consensus**: persona #8 school coordinator
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev + frontend + sales
**Tags**: epic=epic-prr-i, b2b, priority=p1
**Status**: Partial — SchoolSku tier + pricing brackets + feature fencing (3 endpoints) + 20+ tests shipped; school-admin Vue dashboard + roster onboarding + net-30 invoice flow deferred on frontend + PRR-305 downstream
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch-or-launch+1
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

School SKU: ~₪35/student/mo, admin dashboard, teacher-assigned practice, SSO, classroom metrics. Deliberately NON-overlapping with retail Premium features (no parent dashboard, no tutor-handoff PDF) to prevent pricing-leak embarrassment.

## Scope

- Plan type `b2b_school` with volume-bracket pricing (100-499 / 500-1499 / 1500+ students).
- Admin dashboard: class management, teacher onboarding, student roster CSV, per-class metrics.
- Feature fencing: parent-dashboard endpoints REJECT school-SKU accounts.
- Teacher role distinct from student role.
- Invoice flow: annual with net-30 payment terms (vs. parent prepay).

## Files

- `src/admin/full-version/src/pages/schools/`
- `src/backend/Cena.Domain/Subscriptions/SchoolSubscription.cs`
- Tests: feature-fencing enforces boundaries.

## Definition of Done

- School admin can onboard 100 students and assign teachers.
- Parent-dashboard endpoints return 403 for school accounts.
- Net-30 invoice flow works.
- Full sln green.

## Non-negotiable references

- [ADR-0001](../../docs/adr/0001-multi-institute-enrollment.md).

## Reporting

complete via: standard queue complete.

## Related

- [PRR-341](TASK-PRR-341-b2b-contract-volume-pricing.md), [PRR-342](TASK-PRR-342-sso-integration.md), [PRR-343](TASK-PRR-343-school-feature-fencing.md)

## Audit (2026-04-23)

### Already shipped

**Plan type + pricing** (via
[PRR-341 code slice](done/TASK-PRR-341-b2b-contract-volume-pricing.md)):

- `SubscriptionTier.SchoolSku = 4` enum value lives in
  [SubscriptionTier.cs](../../src/actors/Cena.Actors/Subscriptions/SubscriptionTier.cs)
  and is persisted on events — value-stable across the codebase.
- Volume pricing brackets in
  [TierCatalog.cs](../../src/actors/Cena.Actors/Subscriptions/TierCatalog.cs):
  `SchoolSkuMonthlyPricePerStudent(int studentCount)`,
  `SchoolSkuMonthlyContractTotal(int studentCount)`,
  `SchoolSkuVolumeBracket(int studentCount)` implement the
  1–499 @ ₪35, 500–1499 @ ₪29, 1500+ @ ₪24 step function. 20 tests
  in `TierCatalogTests` lock the boundaries, monotonic-non-increasing
  invariant, contract-total math, and entry-bracket ≡ anchor.
- SchoolSku feature flags in `TierCatalog`:
  `ParentDashboard: false`, `TutorHandoffPdf: false`,
  `ClassroomDashboard: true`, `TeacherAssignedPractice: true`,
  `Sso: true` — matches the task's "deliberately non-overlapping
  with retail Premium features" design.

**Feature fencing — DoD #2 green**
(`Parent-dashboard endpoints return 403 for school accounts`):

- [SkuFeatureAuthorizer.cs](../../src/actors/Cena.Actors/Subscriptions/SkuFeatureAuthorizer.cs)
  enforces the tier-feature policy. Tests in
  [SkuFeatureAuthorizerTests.cs](../../src/actors/Cena.Actors.Tests/Subscriptions/SkuFeatureAuthorizerTests.cs)
  lock `SchoolSku + ParentDashboard → denied` and
  `SchoolSku + TutorHandoffPdf → denied` (InlineData rows 20-21).
- Three endpoints wire the fence at the boundary:
  - [HouseholdDashboardEndpoints.cs:92](../../src/api/Cena.Student.Api.Host/Endpoints/HouseholdDashboardEndpoints.cs#L92)
    — `CheckParent(state, TierFeature.ParentDashboard)`.
  - [TutorHandoffEndpoints.cs:97](../../src/api/Cena.Student.Api.Host/Endpoints/TutorHandoffEndpoints.cs#L97)
    — `CheckParent(state, TierFeature.TutorHandoffPdf)`.
  - [ParentDashboardEndpoints.cs:70](../../src/api/Cena.Student.Api.Host/Endpoints/ParentDashboardEndpoints.cs#L70)
    — `if (!tierDef.Features.ParentDashboard)` fenced before
    card-source query.
- All three return 403 with a `tier_required` reason when the
  parent's tier fails the check, so a school-SKU parent calling
  any parent-dashboard surface is cleanly rejected with an honest
  reason code the UI can upsell against.

**Classroom infrastructure** (pre-existing, matches `ClassroomDashboard: true`):

- [ClassroomDocument.cs](../../src/shared/Cena.Infrastructure/Documents/ClassroomDocument.cs),
  [ClassroomJoinRequestDocument.cs](../../src/shared/Cena.Infrastructure/Documents/ClassroomJoinRequestDocument.cs),
  `ClassroomEndpoints`, `ClassroomTargetEndpoints`,
  `ClassroomAnalyticsEndpoint` — teacher-assignment + roster + analytics
  backend already present (shipped in prior STB-00b / prior PRRs).

**Teacher role distinct from student role** — `TeacherRole` and
classroom-scoped authz exist via the existing multi-institute +
classroom code paths per ADR-0001.

**DoD #4 (full sln green)** — verified today: 0 errors on
`dotnet build src/actors/Cena.Actors.sln`.

### What is deferred

- **DoD #1 — school-admin onboards 100 students and assigns teachers.**
  Vue admin dashboard (`src/admin/full-version/src/pages/schools/`)
  plus bulk-roster CSV ingest flow are not yet wired. Classroom backend
  exists; what's missing is the admin UX on top + a single
  `POST /api/admin/schools/{id}/roster` batch endpoint that maps
  a CSV row → `ClassroomJoinRequestDocument` + issues teacher
  account invitations. Estimated 1-2 weeks engineering +
  design-partner school for pilot onboarding feedback.
- **DoD #3 — net-30 invoice flow.** Depends on
  [PRR-305](TASK-PRR-305-hebrew-tax-invoice.md) (Hebrew tax invoice
  generator), which is blocked on an accountant-signed invoice
  template + Israel Tax Authority compliance review. PRR-305 ships
  the invoice number sequence + VAT math + PDF generator; this task
  layers net-30 terms (contract-level attribute on the SchoolSku
  activation + dunning worker that escalates at day 31) on top.
- **§5 decision #4 — launch or launch+1.** Product decision
  holder owns whether school-SKU GA is in the launch tranche or
  punts to launch+1. The pricing + feature-fencing backend above is
  launch-tranche-ready either way.

Closing as **Partial** per memory "Honest not complimentary": DoD
items 2 + 4 are green; 1 needs Vue + CSV-ingest engineering; 3 is
downstream-blocked on PRR-305 legal gate.
