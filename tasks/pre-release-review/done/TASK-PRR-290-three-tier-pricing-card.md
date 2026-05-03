# TASK-PRR-290: Three-tier pricing card (HE/AR/EN + RTL)

**Priority**: P0 — launch-blocker
**Effort**: M (1-2 weeks)
**Lens consensus**: persona-#1 cost-sensitive, #2 high-SES, #3 engineer-parent, #6 Arabic-Israeli (pricing-review 2026-04-22)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: frontend-dev (Vue 3 + Vuexy) + content (HE/AR/EN copy)
**Tags**: epic=epic-prr-i, commercial, priority=p0, i18n, launch-blocker
**Status**: Ready (pending §5 decision #1 — price anchors)
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Render Cena's three-tier pricing card on student-spa landing + parent onboarding: Basic ₪79, Plus ₪229 (decoy), Premium ₪249 (target), with proper RTL, VAT-inclusive display, feature matrix, and CTA per tier.

## Scope

- Three-tier layout with Premium visually prominent (border accent, "recommended" badge — shipgate-compliant copy only, no scarcity terms).
- Plus tier displays its unique differentiator explicitly (unlimited diagnostic + Sonnet-for-complex) so it doesn't read as strawman (persona #3).
- All three locales: Hebrew (primary), Arabic (parity — persona #6), English (fallback). RTL mirrors the card order correctly — Premium stays visually-right-anchored in RTL.
- Price display: "₪249 לחודש, כולל מע״מ" / "249 ₪ شهرياً، شامل الضريبة" / "₪249/month, VAT incl."
- Numeric LTR even in RTL paragraphs (per memory "Math always LTR" — applies to price numerals via `<bdi dir="ltr">`).
- Feature matrix sourced from [PRR-291](TASK-PRR-291-tier-feature-matrix-data.md) (data, not hardcoded strings).
- CTA per tier routes to checkout with plan preselected.
- Sibling-add and annual-prepay are NOT on the main card — they live on post-purchase and checkout respectively (persona-growth #9).

## Files

- `src/student/full-version/src/pages/pricing/index.vue` (new)
- `src/student/full-version/src/components/pricing/TierCard.vue` (new)
- `src/student/full-version/src/i18n/he/pricing.json`, `ar/pricing.json`, `en/pricing.json` (new)
- Tests: locale switching preserves correct numerals, RTL order correct, CTA routes with correct plan slug.

## Definition of Done

- Card renders in HE/AR/EN with correct RTL/LTR.
- VAT-inclusive label in every locale.
- Plus differentiator visible (1-line callout).
- Shipgate scanner passes (no "limited time", "don't miss", streak language).
- Contrast audit passes (Vuexy #7367F0 locked color per memory).
- Full `Cena.Actors.sln` + PWA build green.

## Non-negotiable references

- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md) — no scarcity/countdown copy on pricing page.
- [`docs/engineering/shipgate.md`](../../docs/engineering/shipgate.md) — banned terms scanner.
- Memory "Math always LTR" — `<bdi>` on numerals.
- Memory "Labels match data" — tier name must match feature set.
- Memory "Primary color locked" — no palette drift on tier highlight.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + screenshot HE/AR/EN>"`

## Related

- [PRR-291](TASK-PRR-291-tier-feature-matrix-data.md) — consumes feature matrix
- [PRR-292](TASK-PRR-292-annual-prepay-checkout.md) — annual toggle at checkout
- [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)
