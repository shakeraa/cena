# TASK-PRR-400: Per-tier upload counter + enforcement

**Priority**: P0
**Effort**: M (1 week)
**Lens consensus**: persona #10 CFO
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend-dev
**Tags**: epic=epic-prr-j, tier-enforcement, priority=p0
**Status**: Ready (depends on PRR-310 SubscriptionTier propagation)
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Count diagnostic uploads per student per month, enforce per-tier caps: Basic=0 (upgrade prompt), Plus=20/mo (hard), Premium=100/mo soft + 300/mo hard.

## Scope

- Atomic counter at upload intake (before MSP vision call, to avoid paying for rejected uploads).
- Monthly reset aligned with billing cycle.
- Soft cap reached → fire upsell modal, allow continue.
- Hard cap reached → block with support CTA.
- Metrics emitted.

## Files

- `src/backend/Cena.Diagnostic/Tier/UploadCounter.cs`
- Redis/DB counter.
- Tests.

## Definition of Done

- Counter atomically increments per upload.
- Basic → 403 with upgrade prompt.
- Plus at 20 → 403 with "upgrade to Premium" CTA.
- Premium 100 → soft modal.
- Premium 300 → support CTA.

## Non-negotiable references

- Memory "No stubs".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-310](TASK-PRR-310-subscription-tier-propagation.md), [PRR-312](TASK-PRR-312-per-tier-photo-diagnostic-caps.md), [PRR-386](TASK-PRR-386-soft-cap-reached-ux.md), [PRR-401](TASK-PRR-401-soft-cap-upsell-trigger.md), [PRR-402](TASK-PRR-402-hard-cap-support-ux.md)
