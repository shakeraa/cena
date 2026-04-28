# TASK-PRR-257: ADR-0059 §1-§6 rewrite to match §15 (eliminate supersession cheat)

**Priority**: P1 — docs correctness; ADR is misleading as written
**Effort**: S (1 day; documentation surgery)
**Source docs**: claude-code self-audit 2026-04-28, [ADR-0059](../../docs/adr/0059-bagrut-reference-browse-and-variant-generation.md)
**Assignee hint**: claude-code (coordinator) or any worker with ADR-0059 + ADR-0050 review context
**Tags**: source=claude-code-audit-2026-04-28,epic=epic-prr-n,priority=p1,docs,adr-cleanup
**Status**: Ready
**Tier**: launch-adjacent
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

ADR-0059 today reads: §1-§6 (the original draft with broken cadence numbers, fictional `RateLimitedEndpoint`, missing ARIA, missing retention) followed by §15 ("supersedes §1-§6 where conflicting"). A future reader sees the broken design first; only by reading §15 do they learn what the actual design is. This is a maintenance hazard and an honesty cheat.

Rewrite §1-§6 directly so they reflect the post-persona-review design. Mark §15 as historical-of-revision (or fold it into the new §1-§6 with appropriate cross-refs).

## Scope

1. **Rewrite §1** to incorporate §15.1 changes (opaque `variantQuestionId` HMAC + pepper, endpoint ownership gate).
2. **Rewrite §2** unchanged but cross-ref new §15.10 normative Accessibility for affordance-stripping enforcement.
3. **Rewrite §3** to incorporate §15.3 changes (24h wire HMAC token, 90d event-sourced fact, one-click revoke on reference page).
4. **Rewrite §4** unchanged from current.
5. **Rewrite §5** to incorporate §15.5 changes (per-source caps + per-(student/institute/IP) rate limits + structural-default-on-free + Redis single-flight).
6. **Rewrite §6** unchanged from current but cross-ref §15.9 BKT discounting.
7. **Add new sections** as normative: §"Retention" (from §15.7), §"Browse-history scope limitation" (from §15.6), §"Accessibility" (from §15.10).
8. **Mark §14 + §15** as "Persona Review Synthesis (historical-of-revision)" — preserved for audit but no longer authoritative; the new §1-§6+§Retention+§Browse-scope+§Accessibility is the canonical design.
9. Update §History with the rewrite date + rationale.

## Definition of Done

- ADR-0059 reads top-to-bottom as the canonical design without "supersedes §X" forward-references.
- §14 + §15 retained for audit, clearly marked historical.
- All operational rules in §"For code review" remain in force.
- No diff to ADR-0050 / ADR-0043 / ADR-0060 cross-refs.

## Blocking

- None.

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + ADR-0059 sha post-rewrite>"`
