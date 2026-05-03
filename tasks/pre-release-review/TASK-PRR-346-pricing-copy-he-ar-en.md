# TASK-PRR-346: Pricing page + checkout copy HE/AR/EN (TAIL)

**Priority**: P0 — launch-blocker
**Effort**: M (1 week content + native-reviewer passes)
**Lens consensus**: tail (persona #6 Arabic, general i18n)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: content + native-language reviewers
**Tags**: epic=epic-prr-i, content, i18n, priority=p0, tail
**Status**: Ready
**Source**: tail addition 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Full copy pass for pricing-adjacent surfaces (pricing page, checkout, account billing) in HE primary, AR + EN parity. Native-speaker review for AR; shipgate scanner for all.

## Scope

- Tier names + feature bullets.
- CTA copy.
- Guarantee + refund language.
- VAT/tax disclaimers.
- Error states (failed payment, declined card).
- Shipgate-compliant framing (positive, not scarcity).
- Single-source glossary for tier names (to keep API ↔ UI aligned — memory "Labels match data").

## Files

- `src/student/full-version/src/i18n/{he,ar,en}/pricing.json` + `billing.json` + `checkout.json`
- Content glossary doc.

## Definition of Done

- All 3 locales complete.
- Native Arabic sign-off.
- Shipgate passes.
- No placeholder strings.

## Non-negotiable references

- Memory "Math always LTR" (prices are numerals → LTR inside RTL).
- Memory "Labels match data".
- Memory "Ship-gate banned terms".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-290](TASK-PRR-290-three-tier-pricing-card.md)
