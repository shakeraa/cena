# TASK-PRR-390: Support audit view (photo + OCR + CAS + narration + template)

**Priority**: P0
**Effort**: M (1-2 weeks)
**Lens consensus**: persona #9 support
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend + admin-frontend + support lead
**Tags**: epic=epic-prr-j, support, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Support agent can inspect a disputed diagnostic: original photo (must be captured to hash-ledger before photo-delete SLA fires), OCR output, CAS chain, LLM narration, template selected, student's dispute reason.

## Scope

- Tension point: photo is deleted at 5 min (PRR-412); audit view needs evidence after deletion. Solution: **cryptographic hash of photo at upload + capture support-retrievable snapshot for the dispute window only** (agent-approved persisted evidence).
- For disputed diagnostics, snapshot persists 30 days (aligned with misconception session-scope).
- Agent view in admin dashboard.
- Access logged; agent role required.

## Files

- `src/admin/full-version/src/pages/support/diagnostic-audit.vue`
- Backend snapshot service.
- Privacy policy update to disclose dispute-snapshot retention.

## Definition of Done

- Agent can view photo + chain for disputed diagnostic.
- Non-disputed diagnostics have no photo retained.
- Access logged; non-agent rejected.
- Full sln green.

## Non-negotiable references

- [ADR-0003](../../docs/adr/0003-misconception-session-scope.md).
- Israeli Privacy Law — snapshot retention must be disclosed.
- PPL Amendment 13.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-385](TASK-PRR-385-dispute-button.md), [PRR-391](TASK-PRR-391-auto-credit-confirmed-errors.md), [PRR-412](TASK-PRR-412-photo-deletion-sla.md)
