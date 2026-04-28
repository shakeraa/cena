# TASK-PRR-258: Revert PRR-249 §2 coordinator-ratification; restore unchecked posture

**Priority**: P0 — legal/compliance correctness; coordinator should not have signed a legal box
**Effort**: XS (1 hour; documentation revert)
**Source docs**: claude-code self-audit 2026-04-28, [docs/legal/bagrut-corpus-display-delta.md](../../docs/legal/bagrut-corpus-display-delta.md)
**Assignee hint**: claude-code (coordinator) — self-correcting
**Tags**: source=claude-code-audit-2026-04-28,epic=epic-prr-n,priority=p0,legal,docs-revert
**Status**: Ready
**Tier**: launch-adjacent
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

`docs/legal/bagrut-corpus-display-delta.md` §2 was checkboxed by coordinator (claude-code) on 2026-04-28 under user "do all" delegation. Coordinator is not legal counsel; this is the wrong actor for a legal posture statement. Revert the checkbox, restore the three unchecked options, leave §6 sign-off blank for counsel + Shaker. Add a coordinator note explaining why the ratification was reverted.

## Scope

1. Revert §2 to the original three-option unchecked state.
2. Add a coordinator note above §2: "2026-04-28 coordinator (claude-code) initially ratified the conditional posture under user 'do all' delegation. Reverted same day per self-audit: posture statements require legal counsel + project owner, not coordinator. PRR-258 records the revert."
3. Confirm §6 sign-off block is empty (counsel + Shaker).
4. Update PRR-249 task body status to reflect the revert.
5. Update ADR-0059 §History with a one-line entry noting the revert.

## Definition of Done

- §2 unchecked.
- Coordinator note in place explaining the revert.
- §6 awaits counsel + Shaker.
- Feature-flag flip-on remains gated on §6 sign-off (unchanged from prior state).

## Blocking

- None.

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<commit sha of revert>"`
