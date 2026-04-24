# TASK-PRR-348: Pricing page marketing-site integration + SEO (TAIL)

**Priority**: P1
**Effort**: S (3-5 days)
**Lens consensus**: tail (persona #2 high-SES + #9 growth)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: frontend + marketing
**Tags**: epic=epic-prr-i, marketing, seo, priority=p1, tail
**Status**: Ready
**Source**: tail addition 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Ensure the pricing page is SEO-accessible, cross-linked from marketing site, open-graph tagged, analytics-instrumented.

## Scope

- Meta tags + open-graph per locale.
- Schema.org `Product` / `Offer` JSON-LD.
- Cross-links from landing, FAQ, /about, blog.
- Analytics events: `pricing_page_viewed`, `tier_hovered`, `cta_clicked`, `checkout_started`.

## Files

- `src/student/full-version/src/pages/pricing/index.vue` meta
- `src/student/full-version/public/robots.txt` + `sitemap.xml`
- Analytics wiring.

## Definition of Done

- SEO audit passes.
- OG preview correct HE/AR/EN.
- Analytics events fire.

## Non-negotiable references

- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md) — no time-pressure copy in marketing.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-290](TASK-PRR-290-three-tier-pricing-card.md)
