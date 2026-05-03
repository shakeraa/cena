# GD-004: Elevate Point 7 to engineering acceptance criterion (ship-gate)

## Goal
Convert "zero streak guilt / zero confetti inflation / zero variable-ratio rewards" from a design posture into an enforced engineering rule with CI checks and a PR review checklist.

## Source
`docs/research/cena-sexy-game-research-2026-04-11.md` — Executive summary finding that Point 7 is no longer best-practice but **legal floor**. Track 8 trajectory: FTC v. Epic → Amazon → Edmodo → COPPA 2025 → Reddit ICO £14.47M Feb 2026.

## Work to do
1. **PR template update**: add a "Dark-pattern ship-gate" checkbox block that PR authors must complete. Items:
   - [ ] No new streak counter
   - [ ] No new variable-ratio reward
   - [ ] No new loss-aversion copy ("don't break", "you'll lose")
   - [ ] No new confetti / haptics / audio tied to non-learning events
   - [ ] No notification nagging scheduled without user opt-in
   - [ ] No leaderboard scoped beyond co-op pods
2. **Static checker**: a node script `scripts/shipgate/scan.js` that greps the repo for banned terms in en.json / ar.json / he.json locales + Vue templates + Flutter widgets. Fails CI on match. Banned terms:
   - English: `streak`, `don't break`, `keep the chain`, `lose your`, `daily streak`, `hearts`, `lives` (when used as reward currency, not physics)
   - Arabic: سلسلة (when used as streak), لا تفقد
   - Hebrew: רצף יומי, אל תשבור
3. **Wire into `backend.yml` + `student-web.yml` CI**: shipgate scan runs on every PR, must be green to merge.
4. **Documentation**: new file `docs/engineering/shipgate.md` that explains the policy, the legal basis (one-liner per enforcement case), and the appeal path for legitimate uses (e.g. a physics question about electrical `streak currents` — allowlist).
5. **Memory update**: add a pattern to CLAUDE.md memory so future agents know the rule without re-reading the research doc.

## Non-negotiables
- CI must be blocking, not advisory
- Allowlist must be an explicit enumerated list with justifications, not a regex escape hatch
- Applies to locale files AND code

## DoD
- CI green on main after scanner installed
- At least one test PR demonstrates the scanner catches a violation
- PR template updated
- Memory file updated

## Reporting
Complete with branch + scanner script path + PR template diff link.
