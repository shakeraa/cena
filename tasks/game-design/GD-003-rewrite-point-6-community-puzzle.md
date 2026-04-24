# GD-003: Rewrite proposal Point 6 — daily Wordle → community puzzle

## Goal
Rewrite Point 6 of the game-design proposal and any product-surface copy that implements a "daily challenge" so that it matches the **community puzzle** pattern, not the Wordle/Duolingo loss-aversion pattern.

## Source
`docs/research/cena-sexy-game-research-2026-04-11.md` — Track 10 finding that Wordle's retention mechanism IS loss aversion (confirmed by Duolingo engineering blog). Point 6 as originally written internally contradicts Point 7 (zero streak guilt, now a ship-gate). Track 1 Duolingo FTC dark-pattern exposure.

## Work to do
1. Rewrite the product-surface spec for the daily challenge:
   - One shared problem per day
   - No personal streak counter anywhere
   - No "don't break the chain" copy, no "X days streak" badge
   - Optional participation (no notification nagging if skipped)
   - Community result aggregation: "42% of students chose vertex at (2, −1); here's why that's the right/wrong intuition" — post-solve reveal, not competitive rank
   - Archive of past days, browsable without penalty
2. Update any existing admin templates, notification copy, i18n strings, student UI affordances.
3. File location: `docs/proposal/game-design-proposal.md` (or wherever Point 6 currently lives — audit and locate)
4. Add an example daily-puzzle spec with shape + data (same schema as question bank so no new infra is needed).

## Non-negotiables
- No loss-aversion language in any locale
- Zero streak mechanics, not a "soft" streak, not a "friendly" streak
- Track 7 Israeli Bagrut context: daily engagement is culturally uncomfortable during exam season — opt-in, not default-on

## DoD
- Rewritten spec merged
- Copy audit across EN/AR/HE locales shows no streak/loss-aversion language
- Cross-referenced in the main research synthesis doc

## Reporting
Complete with branch + file list of copy strings changed.
