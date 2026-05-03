# TASK-PRR-253: Extend `IInstitutePricingResolver` with rate-limit overrides

**Priority**: P1 — gates ADR-0059 §5 institute-tunable variant rate-limits
**Effort**: S (1-2 days)
**Lens consensus**: claude-1 PRR-250 finding §1
**Source docs**: [PRR-250 findings](reviews/PRR-250-verification-sweep-findings.md), [ADR-0059 §5](../../docs/adr/0059-bagrut-reference-browse-and-variant-generation.md), [PRR-244 (done)](done/TASK-PRR-244-per-institute-pricing-override.md)
**Assignee hint**: backend (kimi-coder; PRR-244 author has best context)
**Tags**: source=prr-250-finding,epic=epic-prr-n,priority=p1,backend,resolver
**Status**: Ready
**Tier**: launch-adjacent
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

PRR-244 shipped `IInstitutePricingResolver` with three pricing fields (student monthly price, institutional per-seat price, free-tier session cap). ADR-0059 §5 assumed the resolver also surfaced rate-limit overrides for the variant-generation endpoints (parametric + structural daily caps). It does not. Either extend the resolver or amend ADR-0059 §5 to drop institute-tunable rate-limits.

Decision: **extend the resolver**. Rate-limit overrides are a real institutional need (subsidized regional rollouts, enterprise school-network plans) and the resolver is the canonical config surface per PRR-244's "no hard-coded prices" arch ratchet.

## Scope

1. **Extend `ResolvedPricing` record** with two nullable fields:
   - `int? VariantParametricDailyLimit` (null = global default per ADR-0059 §5)
   - `int? VariantStructuralDailyLimit`
2. **Extend the resolver implementation** to surface the new fields from per-institute overrides, with fall-through to global defaults when unset. Mirror the existing pricing field's resolution path.
3. **Extend `InstitutePricingOverridden_V1` event** with the two new fields (nullable; backwards-compatible for historical events). Audit the override exactly like the pricing fields.
4. **Update super-admin UI** (per PRR-244's PRR-244 admin surface) to expose the two new fields with sensible bounds (1..1000 for both; 0 means "denied"; null means "default").
5. **Architecture test extension**: existing `NoHardcodedPricingTest` extends to ban hard-coded variant-rate-limit literals outside the resolver path.
6. **Tests**:
   - Resolver unit test: institute with no override returns nulls (caller applies global defaults).
   - Resolver unit test: institute with override returns the override values.
   - Audit-event test: `InstitutePricingOverridden_V1` round-trips with new fields.

## Files

### Modified
- `src/actors/Cena.Actors/Pricing/InstitutePricingResolver.cs` (or wherever impl lives)
- `src/actors/Cena.Actors/Events/InstitutePricingEvents.cs` — extend `InstitutePricingOverridden_V1`
- `src/admin/full-version/src/pages/admin/institute-pricing.vue` — super-admin UI
- `src/actors/Cena.Actors.Tests/Architecture/NoHardcodedPricingTest.cs` — ban additional literals

### New
- (none — pure extension of existing file/test surfaces)

## Definition of Done

- Resolver returns the two new fields per institute.
- Audit event captures the override.
- Super-admin UI exposes the new fields.
- Arch test catches hard-coded variant-rate-limit literals.
- Full `Cena.Actors.sln` build green.

## Blocking

- None.

## Non-negotiable references

- Memory "No stubs — production grade"
- PRR-244 (the resolver's foundation)
- ADR-0059 §5

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + resolver test sha + audit event test sha>"`
