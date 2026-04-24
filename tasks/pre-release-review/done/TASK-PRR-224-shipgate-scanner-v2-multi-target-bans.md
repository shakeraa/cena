# TASK-PRR-224: Shipgate scanner v2 — ban countdown identifiers + amber/red nag CSS

**Priority**: P0
**Effort**: S (2-4 days)
**Lens consensus**: persona-ethics, persona-cogsci
**Source docs**: persona-ethics findings (identifier-name ban), brief §14.2 #8, PRR-019 (existing banned-terms scanner)
**Assignee hint**: kimi-coder
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p0, ship-gate, scanner
**Status**: Ready
**Source**: persona-ethics review
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md), coordinates with [EPIC-PRR-D (shipgate v2)](EPIC-PRR-D-shipgate-scanner-v2.md)

---

## Goal

Extend the shipgate banned-terms scanner (`scripts/shipgate/scan.mjs` + `banned-mechanics.yml`) to catch multi-target-specific dark-pattern risks at the identifier and CSS level, not just at copy level.

## New bans

### Identifier names (JS/TS/Vue/C#)

Forbid on any file in `src/`:
- `daysUntil`, `days_until`, `DaysUntil`, `daysLeft`, `days_left`, `DaysLeft`
- `timeLeft`, `time_left`, `TimeLeft`, `timeRemaining`
- `countdown`, `count_down`, `Countdown`
- `streak`, `streakCount`, `streak_count`, `StreakCount`, `day_streak`
- `deadlinePressure`, `urgency`
- Exception: `plannedDeadline: DateTimeOffset` on data contracts is allowed (no rendering logic); `_scheduled_for` is allowed.

### Copy (i18n keys in `src/student/.../plugins/i18n/locales/*.json`)

Forbid substrings:
- `days until`, `days left`, `time remaining`, `hurry`, `last chance`, `before it's too late`, `don't miss`, `falling behind`
- RTL equivalents: `יום נותר`, `שעות נותרות`, `יום אחרון`, `أيام متبقية`, `آخر فرصة`

### CSS class + inline style

Forbid on onboarding-skip nag and archive-toast paths:
- Colors: `--v-theme-warning`, `text-warning`, `text-error`, hex values in amber/red range (`#f`, `#e` starters when used on background/foreground in these paths).
- Allowlist for legitimate error states: authentication errors, form validation — whitelisted paths only.

## Implementation

- Extend `scripts/shipgate/scan.mjs` with new rule sets:
  - `banned-identifiers`: regex list over `*.ts|*.tsx|*.vue|*.cs`.
  - `banned-copy-multi-target`: substring list over i18n JSON.
  - `banned-css-nag`: path-scoped CSS scan.
- Extend `banned-mechanics.yml` with new entries + exception annotations.
- Update CI (`/.github/workflows/*.yml` or equivalent) to run scanner on PR.

## Negative tests (must be added)

- PR fixture that adds `daysUntil` to a Vue template — must FAIL scanner.
- PR fixture that adds `countdown` class to onboarding nag — must FAIL.
- PR fixture that adds Hebrew `יום נותר` to i18n locale — must FAIL.
- PR fixture that uses legitimate `plannedDeadline: DateTimeOffset` in data contract — must PASS.

## Files

- `scripts/shipgate/scan.mjs` — extend with 3 new rule sets.
- `scripts/shipgate/banned-mechanics.yml` — extend.
- `scripts/shipgate/tests/scan.spec.ts` — add negative fixtures.
- `docs/engineering/shipgate.md` — update with new rule list.

## Definition of Done

- All 3 new rule sets active in scanner.
- Negative test fixtures fail the scanner.
- CI runs scanner on every PR; green on current codebase EXCEPT the pre-existing streak leak (addressed by PRR-225).
- Scanner output references the violating line + rule ID.
- Documentation updated.

## Non-negotiable references

- ADR-0048 (exam-prep framing).
- Memory "Ship-gate banned terms".
- PRR-019 (countdown ban source).

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + scanner test output>"`

## Related

- PRR-019, PRR-225 (fixes the one streak leak that will fail this scanner), EPIC-PRR-D (shipgate v2 meta-epic).
