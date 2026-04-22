# TASK-PRR-427: Integration with existing hint-request flow (TAIL)

**Priority**: P1
**Effort**: M (1 week)
**Lens consensus**: tail — prevents feature fragmentation
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend + UX
**Tags**: epic=epic-prr-j, integration, priority=p1, tail
**Status**: Ready
**Source**: tail addition 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Clarify the relationship between existing hint-request flow (PRR-262 stem-grounded hints) and photo-diagnostic. They should complement, not conflict.

## Scope

- Decision model: hint = before attempt, diagnostic = after wrong attempt.
- Share misconception taxonomy: when the diagnostic surfaces a template, the subsequent hint request for the retry uses the same misconception context.
- No double-count against diagnostic cap when the same session reopens the hint modal.
- Memory continuity across retry attempts in a session.

## Files

- Integration glue across `HintFlowController` and `DiagnosticFlowController`.
- Tests.

## Definition of Done

- Session carries misconception context across flows.
- Caps not double-counted.
- Full sln green.

## Non-negotiable references

- [ADR-0003](../../docs/adr/0003-misconception-session-scope.md) — session scope.
- Memory "No stubs".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-262](TASK-PRR-262-scaffolding-stem-grounded-hints.md), [PRR-381](TASK-PRR-381-post-reflection-narration.md)
