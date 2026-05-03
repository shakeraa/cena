# Banned-mechanics historical scan report

**Run date**: 2026-04-20 (updated 2026-04-20 by prr-006 follow-up)
**Scanner**: `scripts/shipgate/rulepack-scan.mjs` (pack=mechanics)
**Rule pack**: `scripts/shipgate/banned-mechanics.yml` (30 rules, en + he + ar)
**Source authority**: TASK-PRR-006 (Crisis Mode rename), TASK-PRR-019
(predicted-Bagrut ban), TASK-PRR-040 (locale coverage),
`docs/engineering/shipgate.md`
**Scope**: production surfaces + locale bundles (en/he/ar) across both
`src/student/full-version/` and `src/admin/full-version/`, plus actor/api/
shared C# code, feature specs, engineering docs, design docs. Excluded:
`tests/**` (legit DoesNotContain assertions), `fixtures/**` (positive-test
traps), research docs, and whitelisted policy docs.

## Rule coverage summary

- **English (14 rules)**: crisis-mode-label, crisis-near-exam,
  countdown-generic, countdown-standalone, days-remaining, run-out-of-time,
  last-chance, practice-streak, keep-your-streak, predicted-bagrut-score,
  predicted-exam-score, exam-readiness-score, plus supporting variants.
- **Hebrew (6 rules, advisory — awaiting native-speaker sign-off)**:
  he-crisis-mode, he-countdown, he-days-left, he-last-chance,
  he-time-running-out, he-predicted-bagrut.
- **Arabic (6 rules, advisory — awaiting native-speaker sign-off)**:
  ar-crisis-mode, ar-countdown, ar-days-remaining, ar-last-chance,
  ar-time-running-out, ar-predicted-bagrut.

## Locale pre-scan (en / he / ar, student-web + admin)

| Locale bundle | Hits |
|---|---|
| `src/student/full-version/src/plugins/i18n/locales/en.json` | 0 |
| `src/student/full-version/src/plugins/i18n/locales/he.json` | 0 |
| `src/student/full-version/src/plugins/i18n/locales/ar.json` | 0 |
| `src/admin/full-version/src/plugins/i18n/locales/en.json` | 0 |
| `src/admin/full-version/src/plugins/i18n/locales/he.json` | 0 |
| `src/admin/full-version/src/plugins/i18n/locales/ar.json` | 0 |

**Locale bundles are clean on landing.**

## Production-surface hits

**Total violations after whitelist**: 1

### FOLLOWUP-PRR-D-mechanics-1 (MUST FIX)

| File | Line | Rule | Match |
|---|---|---|---|
| `src/student/full-version/src/plugins/fake-api/handlers/student-notifications/index.ts` | 30 | `keep-your-streak` | `Keep your streak going with a quick review session.` |

**Severity**: P1. This is a genuine shipgate violation in production code. It
lives in the fake-api notification handler (feature-mocked backend), meaning
(a) a developer wrote streak copy into a notification, and (b) if the fake
API is used for Storybook / demo builds, the copy could reach a reviewer's
eye. Fix: rewrite the notification body to positive-frame copy such as
`"3 review items are ready — tap to practice."` No streak metaphor.

**Follow-up task**: enqueue under EPIC-PRR-D (cluster D2, prr-006) as:

> **FOLLOWUP-PRR-D-fake-api-streak**: replace `Keep your streak going` in
> `student-notifications/index.ts:30` with positive-frame copy. 5-minute
> fix. Blocks the shipgate from being flipped to enforcing mode for this
> rule pack.

## Policy-doc and test-assertion hits (whitelisted — no action required)

The following files contain banned-mechanic patterns as part of their
policy-documentation role (describing what's banned) or as negation
assertions in tests (verifying the pattern is NOT rendered). All are
whitelisted in `scripts/shipgate/banned-mechanics-whitelist.yml`:

| File | Role |
|---|---|
| `docs/engineering/shipgate.md` | Ship-gate policy doc |
| `docs/engineering/ci-prohibited-metrics-rules.md` | CI rules catalog |
| `docs/engineering/feature-success-metrics.md` | Metrics policy |
| `docs/engineering/mastery-trajectory-honest-framing.md` | Honest-framing design |
| `docs/engineering/offline-sync-design.md` | Design doc (negation copy) |
| `docs/design/SESSION-UX-002-progressive-disclosure.md` | Design ban statement |
| `docs/design/compression-diagnostic-design.md` | Design anti-pattern |
| `docs/design/daily-community-puzzle.md` | Design ban statement |
| `docs/design/microinteractions-emotional-design.md` | Timer countdown rejection |
| `src/actors/Cena.Actors/ParentalControls/TimeBudget.cs` | Rationale comments |
| `src/actors/Cena.Actors/Accommodations/AccommodationProfile.cs` | Rationale comments |
| `src/student/full-version/src/components/InstallPrompt.vue` | Negation comment |
| `src/student/full-version/src/components/session/QuestionCard.vue` | Negation comment |
| `src/actors/Cena.Actors.Tests/Mastery/CompressionDiagnosticTests.cs` | `DoesNotContain` assertion |
| `src/api/Cena.Student.Api.Host.Tests/Endpoints/TrajectoryEndpointsTests.cs` | `DoesNotContain` assertion |

## Interpretation

1. **Locale bundles are clean** — no English, Hebrew, or Arabic
   student-facing or admin-facing translation currently contains banned
   mechanic copy.
2. **One fake-api notification body violates the streak ban** — must be
   fixed before the rule pack flips from advisory to enforcing.
3. **Policy + test + rationale references are correctly whitelisted**.

## Hebrew / Arabic rule verification

The Hebrew and Arabic patterns in `banned-mechanics.yml` are machine
translations. Before flipping the rule pack to enforcing mode for he/ar
locale files, a native-speaker review pass is required. Suggested
reviewers:

- Hebrew: the translation team that signed off on existing
  `src/student/full-version/src/plugins/i18n/locales/he.json` content.
- Arabic: Prof. Amjad (who owns the Arabic math-lexicon review per
  `docs/content/arabic-math-lexicon.md`).

**Follow-up task**: enqueue under EPIC-PRR-D (cluster D4, prr-040) as:

> **FOLLOWUP-PRR-D-locale-review**: native-speaker review of Hebrew and
> Arabic banned-mechanic patterns in `scripts/shipgate/banned-mechanics.yml`.
> Confirm or replace each. Report back with a confirmed list and this scan
> report will be re-run.

## Reproducibility

```bash
node scripts/shipgate/rulepack-scan.mjs --pack=mechanics --json
```

Exit 0 means clean.
