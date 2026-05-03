# EPIC-E2E-H — Multi-tenant isolation (cross-institute prevention)

**Status**: Proposed
**Priority**: P0 (ADR-0001 is the spine of the data model)
**Related ADRs**: [ADR-0001](../../docs/adr/0001-multi-institute-enrollment.md), ADR-0051 (institute pricing), ADR-0003 (misconception scope)

---

## Why this exists

Every other epic's tenant-isolation leg assumes ADR-0001 is bulletproof. This epic is the *explicit* check that cross-tenant reads, writes, queries, and subscriptions are structurally impossible — not merely accidentally unused. One leak here is a compliance incident and a trust-killer.

## Workflows

### E2E-H-01 — Student of institute A cannot read institute B's questions

**Journey**: student in institute A triggers session → session picks questions from question bank → DB query filters by A's tenant id → B's questions never surface.

**Boundaries**: DB query-level assertion — the actual SQL emitted to Postgres includes a `tenant_id = 'A'` predicate (captured by Marten query log in test mode). Questions in B (pre-seeded) do not appear in A's session.

**Regression caught**: QuestionBank forgot tenant filter → B's content served to A's students.

### E2E-H-02 — Admin A cannot query admin B's users

**Journey**: institute admin for A hits `/api/admin/users` → response contains A's users only → hitting `/api/admin/users/{b-user-id}` → 404 (not 403 — don't leak existence).

**Boundaries**: response body filter, per-id probe returns 404 not 403.

**Regression caught**: 403 leaks existence of the other institute's user; admin can enumerate by id.

### E2E-H-03 — Events published by institute A are not delivered to institute B's NATS subscribers

**Journey**: actor-host publishes `cena.events.student.{studentId-in-A}.mastery-updated` → institute B's parent-digest aggregator (running on same stream) does NOT receive the event.

**Boundaries**: NATS subject naming scopes by tenant; subscriber filter includes tenant predicate; cross-tenant listener receives 0 events during the test window.

**Regression caught**: parent-digest ingests cross-tenant events → wrong family gets wrong kid's numbers (worst-case).

### E2E-H-04 — SUPER_ADMIN can cross tenants; ADMIN cannot

**Journey**: SUPER_ADMIN hits `/api/admin/users?tenant=X` → sees X's data → ADMIN pinned to own tenant hits same endpoint with `?tenant=X` → response filtered to own tenant (query param ignored, not honored).

**Boundaries**: RBAC enforcement (prr-130 consent audit export precedent); audit log records SUPER_ADMIN cross-tenant reads.

**Regression caught**: query param honored for ADMIN (privilege escalation); SUPER_ADMIN cross-tenant reads go un-audited.

### E2E-H-05 — Institute pricing override stays within its institute (prr-244)

**Journey**: SUPER_ADMIN sets override price for institute A → parent of institute B loads `/pricing` → sees DEFAULT price not A's override.

**Boundaries**: IInstitutePricingResolver resolution path honors the caller's tenant; override table queried with tenant predicate; cache keyed by tenant.

**Regression caught**: override leaks to wrong institute (catastrophic — charges wrong price to wrong customer).

### E2E-H-06 — Break-glass overlay scoped to tenant (prr-220)

**Journey**: admin disables a feature family for institute A (break-glass) → institute B continues to use that family unaffected.

**Boundaries**: admin action tenant-scoped, DB BreakGlass row per tenant, runtime feature-flag gate respects tenant-scoped override.

**Regression caught**: break-glass flips globally (disables feature for everyone); admin of B can override A's break-glass.

### E2E-H-07 — Firebase claim tenant matches backend tenant

**Journey**: user authenticates with Firebase → backend reads `tenant_id` from JWT custom claim → every backend write tags with that claim's tenant — never a query-param-supplied tenant, never a localStorage-supplied tenant.

**Boundaries**: backend refuses writes where the path `{tenantId}` differs from the JWT claim; audit log records the mismatch attempt.

**Regression caught**: URL-tampering allows tenant override; JWT-less write paths; write path reads tenant from request body (attacker-controlled).

## Out of scope

- Row-level Postgres security — belongs in a separate data-layer epic; this epic asserts the application-layer guarantees
- LDAP / SAML tenant mapping — we don't use it

## Definition of Done

- [ ] 7 workflows green in strict cross-tenant fixtures (2 sibling tenants created fresh per test)
- [ ] Every workflow runs **both directions** (A→B AND B→A) — asymmetric regressions are common
- [ ] Tagged `@tenant @p0` — blocks merge if red
- [ ] H-03 (bus) and H-05 (pricing) are the load-bearing ones — tag as `@compliance` too
