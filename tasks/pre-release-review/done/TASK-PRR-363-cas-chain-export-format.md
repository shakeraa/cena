# TASK-PRR-363: CAS chain export format (for teacher-auditable view)

**Priority**: P0
**Effort**: S (3-5 days)
**Lens consensus**: persona #3 teacher (auditable CAS chain prevents black-box trust issues)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend-dev
**Tags**: epic=epic-prr-j, priority=p0, teacher-facing
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Export verification result in a format that renders as an expandable "show my work" view — teachers + mathematically-curious students can inspect the SymPy transformation tree.

## Scope

- Structured JSON export: per-step input, applied operation, output, equivalence-check result.
- Pretty-render adapter for `DiagnosticShowMyWorkView.vue` ([PRR-382](TASK-PRR-382-show-my-work-view.md)).
- Hebrew/Arabic/English labels for operations ("expand", "factor", "simplify").

## Files

- `src/backend/Cena.Diagnostic/Export/CasChainExporter.cs`
- Tests.

## Definition of Done

- Export round-trips with verifier.
- Renders correctly in show-my-work view.
- Locale-correct labels.

## Non-negotiable references

- [ADR-0002](../../docs/adr/0002-sympy-correctness-oracle.md).

## Reporting

complete via: standard queue complete.

## Related

- [PRR-360](TASK-PRR-360-step-chain-verifier.md), [PRR-382](TASK-PRR-382-show-my-work-view.md)
