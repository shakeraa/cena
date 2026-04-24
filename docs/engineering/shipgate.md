# Ship-gate: dark-pattern ban enforcement

## Policy

Cena prohibits engagement mechanics that exploit loss aversion, variable-ratio reward schedules, artificial urgency, or social pressure on minors. This is not a design preference — it is a legal floor established by:

| Enforcement | Date | Key ruling |
|-------------|------|-----------|
| FTC v. Epic | 2022 | $245M — default-on social for minors |
| FTC v. Edmodo | 2023 | "Affected Work Product" — models trained on child data deleted |
| FTC COPPA Final Rule | 2025 | Explicit data minimization + notification consent for minors |
| ICO v. Reddit | Feb 2026 | £14.47M — per-user behavioral profiles of minors = profiling |
| Israel PPL Amendment 13 | In force | Applies to all Cena users in Israel |

## What is banned

1. **Streak counters** that can go to zero (loss aversion)
2. **Variable-ratio rewards** on answer correctness (slot-machine pattern)
3. **Loss-aversion copy**: "don't break", "you'll lose", "keep the chain", FOMO urgency
4. **Guilt/shame push notifications**: "your tutor misses you"
5. **Confetti/haptics/audio** tied to non-learning events (reward inflation)
6. **Default-on social matchmaking** or public leaderboards ranking minors
7. **Streak-freeze currency** of any kind

## What is allowed

- Positive-frame daily cadence signal (Apple Fitness rings style — shows progress, no punishment for missing)
- Co-op study pods (opt-in, no ranking)
- Community puzzles (shared challenge, no personal streak)
- Session completion celebration (brief, proportional)

## CI enforcement

Two scanners run side-by-side on every PR:

### 1. String-based rulepack scanner (`scripts/shipgate/scan.mjs` + `rulepack-scan.mjs`)

- Scans locale files (en.json, ar.json, he.json) for banned terms.
- Scans Vue templates and TypeScript source for banned patterns.
- Scans C# backend code for banned patterns.
- Checks against the allowlist at `scripts/shipgate/allowlist.json`.

Registered packs (each pack = one YAML rule file + whitelist + positive-test
fixture under `shipgate/fixtures/`):

- `citations` — [PRR-005] Dr. Rami-rejected citations.
- `mechanics` — [PRR-006/019/040] Crisis-Mode / countdown / predicted-Bagrut copy.
- `effect-size` — [PRR-071] uncited effect sizes.
- `therapeutic-claims`, `progress-framing`, `error-blame`, `cheating-alert`, `reward-emoji` — [EPIC-PRR-D 2nd-wave].
- `positive-framing-extended` — [EPIC-PRR-D P2 tail].
- `multi-target-mechanics` — [PRR-224] multi-target cohort-shaming + ADR-0050 §10 identifier bans (daysUntil / countdown / streak / deadlinePressure anywhere in `src/`). Covers cross-target framings like "don't miss your Bagrut — only N days", "falling behind in Physics", and cross-target cohort shaming. Extended by [PRR-264] with hide-reveal timer / auto-hide identifier + copy bans (`autoHideOptionsAfter`, `optionsRevealTimer`, `revealCountdown`, `scheduled_hide_at`, "options disappear in N seconds", "you have N seconds before the options hide") across en/he/ar — closes the STUDENT-INPUT-MODALITIES-002 §3.1 persona-ethics red line on time-pressure leakage into the hide-reveal primitive.

### 2. UX-surface DOM-aware scanner v2 (`scripts/shipgate/ux-surface-scan.mjs`, prr-211)

Targets the new UX surfaces introduced by EPIC-PRR-E: `HintLadder`,
`StepSolverCard`, `Sidekick`, `MathInput`, `FreeBodyDiagramConstruct`.
The structural rules encoded here cannot be expressed by the string-only
scanner — they involve DOM-anchor + class-token pairs (e.g. "no
`text-danger` ON a `.hint-ladder-rung` element"), per-rung emoji bans,
and aria-marker requirements. Rule pack: `scripts/shipgate/shipgate-ux-surfaces.yml`.

**Extended lexicon enforced by v2 (per-surface):**

#### HintLadder

- String bans: `penalty` / `penalties`, `lost points`, `Wrong!`, `Hint N of M`, `(N remaining)`, `reveal XP`.
- DOM-coupled: warning-semantic color classes (`text-danger`, `bg-warning`, `text-error`, etc.) AND Vuetify `color="warning"` / `color="error"` / `color="danger"` props on rung elements are banned.
- Emoji: codepoints inside the L1 (first) rung are banned.
- Aria: region must carry `role="region"` OR `aria-live="polite"` / `aria-live="assertive"` OR `aria-label`.

#### StepSolverCard

- Countdown / timer-pressure copy (`time is running out`, `countdown`, `timer pressure`, `hurry`).
- Percentile comparisons (`42nd percentile`, `compared to peers`, `ahead of N%`).
- `Wrong!` / `Incorrect!` shaming; `penalty`.
- Urgency color classes (`text-danger`, `bg-warning`, `animate-pulse`) on step-status elements.

#### Sidekick

- `streak`; `daily quota`; `catch up` / `falling behind`.
- Loss-aversion (`don't lose your`, `you'll lose`, `don't miss out`).

#### MathInput

- `Wrong!` / `Incorrect!`; shame-laden rejection (`that's not right`, `try harder`, `no, that's`).

#### FreeBodyDiagramConstruct

- `chance to score`; variable-ratio reward language (`lucky bonus`, `jackpot`, `surprise reward`, `bonus round`); `penalty`.

**Graceful missing-file handling:** if a named surface's canonical `.vue` file
does not yet exist (e.g. `Sidekick.vue`), the scanner logs a warning and exits 0.
Enforcement turns on automatically the moment the file lands. Pass
`--strict-missing` to flip this to a hard failure.

**Architectural rationale for a separate rulepack** is documented at the top
of `scripts/shipgate/shipgate-ux-surfaces.yml` — in short, the existing
string scanner cannot express structure-dependent rules (class × DOM
position) without growing a DOM parser. The v2 scanner is a separate
binary; the two run side-by-side in CI.

**Architecture ratchet:**
`src/actors/Cena.Actors.Tests/Architecture/ShipgateScannerV2CoversNewSurfacesTest.cs`
asserts the YAML rulepack names all five surfaces at their canonical
paths and that the scanner script + positive-test fixture exist.

**Fixtures:** `shipgate/fixtures/ux-surfaces-sample.vue` deliberately
contains every banned pattern; `tests/shipgate/ux-surfaces.spec.mjs`
asserts every rule fires against the fixture and clean against the real
repo. Run:

```bash
node scripts/shipgate/ux-surface-scan.mjs
node scripts/shipgate/ux-surface-scan.mjs --fixture-mode --json
node --test tests/shipgate/ux-surfaces.spec.mjs
```

### Allowlist

If a legitimate use is flagged (e.g. a physics question about "streak currents"), add an entry to `scripts/shipgate/allowlist.json`:

```json
{
  "file": "path/relative/to/repo/root",
  "line": 42,
  "term": "streak",
  "justification": "Physics term: electrical streak discharge"
}
```

Allowlist entries are reviewed in PR. The justification field is mandatory.

## PR template

Every PR includes a dark-pattern checklist (`.github/PULL_REQUEST_TEMPLATE.md`). All items must be checked before merge.
