# TASK-PRR-291: Tier feature-matrix as data (drives page + checkout + upgrade prompts)

**Priority**: P0 — launch-blocker
**Effort**: S (3-5 days)
**Lens consensus**: all personas (implicit — data-driven tier definitions prevent drift across surfaces)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev (domain model) + frontend (consumers)
**Tags**: epic=epic-prr-i, commercial, priority=p0, domain-model, launch-blocker
**Status**: Ready
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Define `TierDefinition` as a single source of truth consumed by pricing card, checkout, upgrade prompts, LLM router, diagnostic caps, and parent dashboard visibility. No hardcoded tier-name strings scattered across surfaces.

## Scope

- Domain model: `TierDefinition { tierId, priceIlsMonthly, priceIlsAnnual, featureFlags, usageCaps, visibility }`.
- `usageCaps` includes: `sonnetEscalationsPerWeek`, `photoDiagnosticsPerMonth`, `hintRequestsPerMonth`, `softCap vs hardCap`.
- `featureFlags`: `parentDashboard`, `tutorHandoffPdf`, `prioritySupport`, `arabicDashboard`.
- `visibility`: where tier appears (`retail`, `b2bSchool`, `hidden`).
- Seed data for Basic / Plus / Premium + School SKU (launch+1) in migration.
- API: `GET /api/tiers` returns retail tiers; admin API returns all.
- Pricing page, checkout, upgrade prompts, LLM router all consume from this.

## Files

- `src/backend/Cena.Domain/Subscriptions/TierDefinition.cs` (new)
- `src/backend/Cena.Domain/Subscriptions/UsageCaps.cs` (new)
- `src/backend/Cena.StudentApi/Controllers/TiersController.cs` (new)
- Migration: seed Basic/Plus/Premium
- `src/student/full-version/src/composables/useTiers.ts` (new)
- Tests: serialization round-trip, API contract, all 3 retail tiers present.

## Definition of Done

- `GET /api/tiers` returns 3 retail tiers with full shape.
- Pricing card consumes from composable, no hardcoded feature lists.
- Admin can toggle `visibility` without code change.
- Full `Cena.Actors.sln` green.

## Non-negotiable references

- Memory "No stubs — production grade" — seed data is real, not placeholder.
- Memory "Labels match data" — tier names in API match pricing page.
- [ADR-0026](../../docs/adr/0026-llm-three-tier-routing.md) — usageCaps consumed by LLM router.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + API response JSON>"`

## Related

- [PRR-290](TASK-PRR-290-three-tier-pricing-card.md) — consumer
- [PRR-310](TASK-PRR-310-subscription-tier-propagation.md) — propagates to student-api/actor-host
- [PRR-311](TASK-PRR-311-per-tier-llm-routing-policy.md) — LLM router consumer
- [PRR-312](TASK-PRR-312-per-tier-photo-diagnostic-caps.md) — diagnostic caps consumer
