# TASK-E2E-COV-04: Per-page functional matrix — admin SPA

**Status**: Proposed
**Priority**: P1
**Epic**: Coverage matrix (extends EPIC-E2E-G)
**Tag**: `@coverage @functional @admin @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/EPIC-G-per-page-{area}.spec.ts` (one file per nav area — see breakdown)
**Prereqs**:
- TASK-E2E-INFRA-03 (dynamic-route seed fixture) for the [id]-route subset
- All 5 backend gap fixes (TASK-E2E-BG-01..05) — those routes are dead until the backend ships

## Why this exists

The current admin smoke spec covers **mount-only correctness**: each route loads, no JS error, no 5xx. Real users do more than load pages — they click filter chips, open dialogs, submit forms, approve queue items, drill into details. That layer is **completely uncovered** for the admin SPA today.

This task expands coverage from "39 pages mount" to "each page's primary action works". Per-cell tests, broken into per-nav-area sub-specs so a regression in one area doesn't burn the whole matrix.

## Sub-specs (one per file, each ~150 lines)

| Spec | Surface | Primary actions to assert |
| --- | --- | --- |
| `EPIC-G-permissions.spec.ts` | `/apps/permissions` + `/apps/roles` | grant role, revoke role, search user, role-change endpoint contract |
| `EPIC-G-moderation.spec.ts` | `/apps/moderation/queue` + `/apps/moderation/review/[id]` | approve item, reject with reason, request re-review, cross-tenant guard rejects |
| `EPIC-G-questions.spec.ts` | `/apps/questions/list` + `/apps/questions/edit/[id]` + `/apps/questions/languages` | edit a question, save round-trips, language fallback flag works |
| `EPIC-G-mastery.spec.ts` | `/apps/mastery/dashboard` + `/apps/mastery/student/[id]` + `/apps/mastery/class/[id]` | k-floor enforcement on class drill-down, individual student detail |
| `EPIC-G-pedagogy.spec.ts` | `/apps/pedagogy/methodology` + `/apps/pedagogy/mcm-graph` + `/apps/pedagogy/methodology-hierarchy` | inline-edit cell confidence, save persists, mcm-graph populated state |
| `EPIC-G-system.spec.ts` | `/apps/system/health` + `/apps/system/audit-log` + `/apps/system/dead-letters` + `/apps/system/embeddings` + `/apps/system/explanation-cache` | filter audit log by date, retry dead-letter, view embeddings stats |
| `EPIC-G-experiments.spec.ts` | `/apps/experiments` + `/apps/experiments/[id]` | create experiment, view experiment detail, terminate active experiment |
| `EPIC-G-tutoring.spec.ts` | `/apps/tutoring/sessions` + `/apps/tutoring/sessions/[id]` | filter active sessions, drill into one, see message timeline |
| `EPIC-G-users.spec.ts` | `/apps/user/list` + `/apps/user/view/[id]` | search by email, view user profile, RTBF action |
| `EPIC-G-settings.spec.ts` | `/apps/system/ai-settings` + `/apps/system/settings` + `/apps/system/token-budget` | save AI settings, change provider, see token-budget chart |

## Boundary assertions (per-action)

| Boundary | Assertion |
| --- | --- |
| DOM | The action's UI confirmation appears (toast, dialog close, list refresh) |
| API | The intended endpoint returns 2xx with the expected response shape |
| State | The read-model reflects the change (re-fetch + assert) |
| Bus | If the action emits a domain event, `busProbe.waitFor` catches it |
| Tenant | Cross-tenant attempts return 403 / 404 (RBAC positive case) |

## Regression this catches

- An endpoint contract drift (DTO field renamed) that the SPA tolerates silently because of optional chaining
- A SignalR push that the action depends on dropping its subscription
- A new tenant-isolation regression where a SUPER_ADMIN action leaks across institutes
- A permissions/role-change UI happily showing success but the backend rejected the call (UI error swallow)

## Done when

- [ ] All 10 sub-specs ship with at least one click+assert per primary action listed above
- [ ] Each sub-spec uses the diagnostic-collection pattern (console + page-error + 4xx/5xx)
- [ ] Dynamic-route subset gated on TASK-E2E-INFRA-03 and skipped with `test.fixme` reason until that lands
- [ ] Tagged `@functional @admin @p1`
- [ ] All sub-specs pass on a full-suite run with the dev stack hot
