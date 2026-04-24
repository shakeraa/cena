# TASK-PRR-347: Terms & Conditions + Privacy Policy drafting (TAIL)

**Priority**: P0 — launch-blocker (legal gate)
**Effort**: L (3-4 weeks legal + 1 week integration)
**Lens consensus**: tail (all compliance-adjacent personas)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: legal + product + backend-dev (integration)
**Tags**: epic=epic-prr-i, legal, compliance, priority=p0, legal-gate, tail
**Status**: Not Started — **legal gate**
**Source**: tail addition 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Drafted, legal-approved, published: Terms of Service, Privacy Policy, Refund Policy, Subscription Agreement, Data Processing Notice — all in HE primary + AR + EN.

## Scope

- Legal drafting aligned with: Israeli Privacy Law, PPL Amendment 13, Consumer Protection Law, GDPR-parity (for EU users transiting), direct-marketing law.
- Version-controlled; parents sign off at signup on current version.
- Amendment-notification workflow for material changes.
- Accessible pages at `/legal/terms`, `/legal/privacy`, `/legal/refund`.

## Files

- Legal content (draft then HTML).
- `src/student/full-version/src/pages/legal/{terms,privacy,refund,subscription-agreement,data-processing}.vue`
- Version-tracking in DB.

## Definition of Done

- Drafts legal-approved and published.
- Accept-on-signup wired.
- Amendment notifications working.

## Non-negotiable references

- Israeli Privacy Law, PPL A13.
- Consumer Protection Law.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-333](TASK-PRR-333-consumer-protection-compliance.md)
