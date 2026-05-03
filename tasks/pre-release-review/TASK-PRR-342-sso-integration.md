# TASK-PRR-342: SSO integration (SAML / Google Workspace for Education / Microsoft)

**Priority**: P1
**Effort**: L (2-3 weeks)
**Lens consensus**: persona #8 school (SSO is table stakes for B2B)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev (auth) + devops
**Tags**: epic=epic-prr-i, b2b, auth, priority=p1
**Status**: Ready
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch-or-launch+1
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Schools authenticate students + teachers via Google Workspace for Education, Microsoft Entra, or SAML 2.0. Just-in-time provisioning for roster onboarding.

## Scope

- OIDC flow for Google + Microsoft.
- SAML 2.0 for district SSO.
- JIT provisioning.
- Role mapping (teacher / student / admin).
- Session bridge to existing Firebase Auth.

## Files

- `src/backend/Cena.Infra/Auth/SsoAdapter.cs`
- Auth flow integration.
- Tests.

## Definition of Done

- Google Workspace auth works end-to-end.
- Microsoft Entra works.
- SAML 2.0 integration at least one test IdP.

## Non-negotiable references

- Memory "No stubs — production grade".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-340](TASK-PRR-340-school-sku-plan-definition.md)
