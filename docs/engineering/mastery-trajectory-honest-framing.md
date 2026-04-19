# Mastery Trajectory — Honest Framing Contract (RDY-071)

> **Status**: LOCKED for Phase 1A. Any relaxation of the banned-phrase
> list requires sign-off from Dr. Yael (psychometrics) + Dr. Rami
> (honesty lens) + a fresh CI rules review.

- **Task**: RDY-071
- **Related**: RDY-080 (θ→Bagrut calibration) — the *only* future path
  that can unblock numeric Bagrut-score display
- **Shipgate scanner**: `scripts/shipgate/scan.mjs`
- **Domain code**: `src/actors/Cena.Actors/Mastery/AbilityEstimate.cs`
  + `MasteryTrajectoryProjection.cs`

## 1. The contract

Until the RDY-080 concordance study produces an approved
`ConcordanceMapping` (`F8PointEstimateEnabled == true`), student- and
parent-facing surfaces MUST:

1. Show mastery as a **qualitative bucket** — HIGH / MEDIUM / LOW /
   Inconclusive — derived from θ with an 80% CI guard.
2. Show **explicit sample-size caption** — "based on N problems over
   M weeks" — authored server-side so the numbers and the copy stay
   in lockstep.
3. Show **"keep practicing" copy** when the 80% CI straddles a bucket
   boundary or SampleSize < minimum (default 30).
4. NEVER show a numeric predicted score, grade, percentile, or
   timescale-to-Bagrut estimate.

## 2. Banned phrases (CI-enforced)

The shipgate scanner rejects any of these phrases appearing in:
- `src/admin/full-version/**/*.{vue,ts,js}`
- `src/student/full-version/**/*.{vue,ts,js}`
- `src/shared/Cena.Infrastructure/**/*.cs` (email / notification templates)
- `src/api/**/*.cs` (response strings, error copy)
- i18n locale files (`src/**/locales/*.json`)

Excluded:
- `docs/**` — this very document + psychometrics design notes quote
  banned phrases by necessity to explain what is banned
- `tests/**` — regression tests assert "this string does NOT appear
  in a render" and need to reference the banned string
- `tasks/**` — backlog bodies may discuss banned framing when
  describing what NOT to build

### Phrases

| Pattern (case-insensitive) | Why |
|---|---|
| `predicted bagrut` | implies a calibrated mapping we do not have |
| `your bagrut score` | same |
| `bagrut score will be` | same, future-tense assertion |
| `expected grade` | dishonest forward extrapolation |
| `expected score` | dishonest forward extrapolation |
| `we predict you['']ll score` | same, conversational tone |
| `your score will be` | same |
| `you will score` | same |
| `you['']ll get a \d` | numeric prediction |
| `predicted score\s*:?` | same |
| `bagrut prediction` | same |
| `grade prediction` | same |
| `₪\s*\d+\s+by moed` | implies we predict cutoff / ₪-denominated goal |
| `\d{2,3}\s+on (your|the) bagrut` | numeric prediction |

### Arabic / Hebrew analogs

| Arabic | Hebrew | English gloss |
|---|---|---|
| علامة البجروت المتوقعة | ציון הבגרות החזוי | predicted Bagrut score |
| درجتك ستكون | הציון שלך יהיה | your score will be |
| نتوقع أن تحصل على | אנו צופים שתקבל | we predict you'll get |
| درجتك المتوقعة | הציון הצפוי שלך | your expected grade |

## 3. Allowlist / what IS permitted

- "Mastery level: HIGH (80% confidence, 142 problems, 6 weeks)"
- "Based on 58 problems over 4 weeks — we need more data for a clear read."
- "You've mastered the core of <topic>. Try a harder set."
- "Let's work on <topic> — your answers suggest the ideas haven't
  clicked yet."
- Trajectory chart with a y-axis labelled "Mastery (Cena scale)" —
  NOT "Bagrut score" or "Grade". The Cena scale does not claim to be
  a Bagrut scale.

## 4. How the gate flips

The shipgate banned-phrase rules are permanent. The gate on the
**numeric-Bagrut-score view** (F8 point estimate) flips through code,
not through a config file:

```csharp
// src/actors/Cena.Actors/Services/Calibration/ConcordanceMapping.cs
public bool F8PointEstimateEnabled
    => ApprovedBy is not null
       && SupersededAtUtc is null
       && Adequacy.ClearsBar;
```

Front-end surfaces that render a numeric predicted score MUST gate on
that property (via an API response field, never via a client-side env
flag). The shipgate rule below enforces that any `predicted Bagrut`
string appearing in the rendered output is a regression — because even
after F8 flips on, the string should be phrased as "Your current
mapping estimate: 88 ± 5" (factual, not forward-predictive).

## 5. Review checkpoints

- **Monthly**: product + Dr. Rami review the trajectory dashboard
  render in all three locales (en/ar/he) for any accidental drift into
  forbidden framing
- **Before every F8 toggle flip**: Dr. Yael reviews the current
  mapping's adequacy report + signs `ApprovedBy`
- **Pilot debrief**: if students / parents describe the trajectory
  bucket as "equivalent to Bagrut X", update copy to reinforce the
  distinction

## References

- Calibration study: `docs/psychometrics/calibration-study-design.md`
- Panel review: `docs/research/cena-panel-review-user-personas-2026-04-17.md`
  (Round 2.F8 + Round 4 Item 2 — Dr. Yael's non-negotiable)
- ADR-0032: `docs/adr/0032-irt-2pl-calibration.md`
- Shipgate: `docs/engineering/shipgate.md`
