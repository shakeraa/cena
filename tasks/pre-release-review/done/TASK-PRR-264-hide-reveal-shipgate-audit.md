# TASK-PRR-264: Hide-reveal shipgate audit — ban countdown/timer/auto-hide language

**Priority**: P1 — persona-ethics
**Effort**: S (2-3 days)
**Lens consensus**: persona-ethics, persona-cogsci
**Source docs**: [STUDENT-INPUT-MODALITIES-002-discussion.md §3.1](../../docs/design/STUDENT-INPUT-MODALITIES-002-discussion.md), ties to [PRR-224 shipgate scanner v2](TASK-PRR-224-shipgate-scanner-v2-multi-target-bans.md)
**Assignee hint**: kimi-coder
**Tags**: source=student-input-modalities-002, epic=epic-prr-f, priority=p1, ship-gate, scanner, q2
**Status**: Ready (coordinates with PRR-224)
**Source**: 10-persona 002-brief review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md), coordinates with [EPIC-PRR-D](EPIC-PRR-D-shipgate-scanner-v2.md)

---

## Goal

Extend the shipgate scanner (PRR-224 extension) to reject any hide-reveal code or copy that introduces timer-based auto-hide, countdown language, or pedagogy-driven silent redaction. These are ADR-0048 + CLAUDE.md Design Non-Negotiable #3 violations.

## New bans

### Identifier names

- `autoHideOptionsAfter`, `autoHideAfterSeconds`, `optionsRevealTimer`, `hideAfterMs`, `revealCountdown`.

### Copy (i18n keys)

- "options disappear in", "you have X seconds", "hurry before options hide", RTL equivalents.

### Behavior assertions (code-level)

- No `setTimeout` / `setInterval` in hide-reveal code paths (sampled via AST).
- No `scheduled_hide_at: DateTimeOffset` in session state.
- No server-side force-hide without a visible banner (references [PRR-261](TASK-PRR-261-classroom-redacted-projection.md) banner requirement).

### Negative tests

- PR fixture introducing `autoHideOptionsAfter` → scanner FAILS.
- PR fixture with "options disappear in 10s" copy → scanner FAILS.
- PR fixture with `setTimeout` in `McOptionsGate.vue` → scanner FAILS.
- PR fixture with student-opted hide-reveal + banner-less server enforcement → scanner FAILS.

## Files

- `scripts/shipgate/scan.mjs` — extend rules.
- `scripts/shipgate/banned-mechanics.yml` — extend banned terms list.
- `scripts/shipgate/tests/scan.spec.ts` — add fixtures.
- `docs/engineering/shipgate.md` — document new rules.

## Definition of Done

- All new rules active; fixtures fail correctly.
- CI runs scanner on every PR.
- Documented in shipgate.md.
- Coordinates with PRR-224 — shared banned-mechanics.yml file does not regress.

## Non-negotiable references

- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md).
- CLAUDE.md Design Non-Negotiable #3.
- Memory "Ship-gate banned terms".
- [PRR-224](TASK-PRR-224-shipgate-scanner-v2-multi-target-bans.md).

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + scanner output>"`

## Related

- PRR-260, PRR-261, PRR-224.
- Persona-ethics 002 findings (§3.1 option C rejection + §3.3 banner requirement).
