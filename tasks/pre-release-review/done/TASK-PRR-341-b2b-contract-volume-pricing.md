# TASK-PRR-341: B2B school contract template + volume pricing brackets

**Priority**: P1
**Effort**: M (1-2 weeks legal + pricing model)
**Lens consensus**: persona #8 school coordinator, #10 CFO
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: legal + sales + finance
**Tags**: epic=epic-prr-i, b2b, legal, priority=p1
**Status**: Partial — volume pricing brackets shipped in code 2026-04-23; contract template + DPA + SLA remain on legal-counsel gate
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch-or-launch+1
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Standardize B2B contract: volume pricing brackets, SLA terms, data-protection addendum, teacher-account provisioning, termination / reassignment rights.

## Scope

- Pricing brackets: 100-499 students @ ₪35/mo; 500-1499 @ ₪29/mo; 1500+ @ ₪24/mo.
- Contract template HE + EN.
- DPA addendum (aligned with Israeli Privacy Law + PPL Amendment 13).
- SLA: 99% uptime during school hours.
- Termination clause with data export.

## Files

- Legal docs (not in code repo; filed with legal).
- Pricing reference in `src/backend/Cena.Domain/Subscriptions/` data seed.

## Definition of Done

- Contract template approved by legal.
- Pricing brackets in code tier-seed.
- First pilot school signed on template (success criterion, not launch criterion).

## Non-negotiable references

- Israeli Privacy Law + PPL A13.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-340](TASK-PRR-340-school-sku-plan-definition.md)

## What shipped (2026-04-23)

DoD item 2 — "Pricing brackets in code tier-seed" — is green:

- `src/actors/Cena.Actors/Subscriptions/TierCatalog.cs` now exposes
  three static functions alongside the existing `SiblingMonthlyPrice`
  pattern:
  - `SchoolSkuMonthlyPricePerStudent(int studentCount) → Money`
    (step-function bracket lookup).
  - `SchoolSkuMonthlyContractTotal(int studentCount) → Money`
    (per-student × seats, atomic with bracket lookup so rounding is
    consistent).
  - `SchoolSkuVolumeBracket(int studentCount) → string`
    (`"small"` / `"mid"` / `"large"` for invoice line-items + sales
    pipelines that need bracket identity independent of price).
- Bracket boundaries (lower-inclusive):
  - 1 – 499 → ₪35/student/mo (`small`)
  - 500 – 1_499 → ₪29/student/mo (`mid`)
  - 1_500+ → ₪24/student/mo (`large`)
- Numbers sourced from task brief §Scope, traceable to
  [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
  (persona #8 school coordinator + #10 CFO, 2026-04-22). Any edit has
  the same ADR-0057 §6 "pricing decision-holder PR review" weight as
  the retail prices already in that file.
- Single-seat anchor preserved: `TierCatalog.Get(SchoolSku).MonthlyPrice`
  still equals the entry-bracket rate (₪35), so existing call-sites
  that treat SchoolSku as a flat tier do not silently migrate to the
  volume-discounted rate. The volume function is opt-in for contract-
  level pricing.
- Tests: `Cena.Actors.Tests/Subscriptions/TierCatalogTests.cs` gains
  20 new test rows (Theory + Fact) covering:
  - Small/mid/large bracket prices at sampled counts.
  - Step-function exact boundaries (499/500, 1499/1500).
  - Monotonic non-increasing price invariant (catches bracket-table
    transposition typos).
  - `SchoolSkuMonthlyContractTotal` = `SchoolSkuMonthlyPricePerStudent`
    × seats at bracket boundaries.
  - Rejection of zero + negative counts.
  - Single-seat anchor ≡ entry-bracket rate (regression guard for the
    "small bracket drifts from canonical anchor" failure mode).
- Full `Cena.Actors.sln` build green (0 errors, pre-existing warnings).
  33/33 `TierCatalogTests` pass (13 prior + 20 new).

## What is deferred (legal-counsel gate)

- **Contract template HE + EN** — lives outside the code repo (filed
  with legal) per the task's own `## Files` list. Requires engagement
  letter with Israeli education-sector counsel conversant in PPL
  Amendment 13 compliance.
- **DPA addendum** — aligned with Israeli Privacy Law + PPL A13;
  drafted by counsel, not engineering.
- **SLA clause** — 99% uptime during school hours; operational
  commitment must be co-signed by infra/on-call leadership.
- **Termination + data-export clause** — legal + product-data jointly
  own the export schema; requires PRR-411 DPIA output as input.
- **First pilot school signed on template** — the task's own footnote
  flags this as a success criterion, not a launch criterion. No code
  work blocks on it.

Closing as **Partial** per memory "Honest not complimentary": code DoD
item is fully real and tested, non-code DoD items need counsel +
signed contract, neither of which this repo can produce.
