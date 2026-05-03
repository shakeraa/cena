# Arabic-first pilot — baseline instrument (DRAFT)

> **🚨 DRAFT — Prof. Amjad + Dr. Nadia review required 🚨**
>
> The items below are engineering draft placeholders. Prof. Amjad sets
> the final 25 items; Dr. Nadia approves the Likert wording.

- **Task**: RDY-079
- **Related**: `arabic-first-pilot-design.md` §4.3

## Purpose

Establish a pre-pilot baseline for each enrolled student so the
post-pilot comparison is against a known starting point, not against
an assumed norm. Same instrument is re-administered post-pilot;
delta is the primary signal for mastery gain.

## Administration

- **Mode**: paper or Cena-hosted form, teacher-proctored
- **Duration**: 45 minutes
- **Timing**:
  - **Pre**: 2 weeks before pilot enrolment
  - **Post**: within 2 weeks of pilot week-12
- **Language**: student chooses Arabic or Hebrew; same items,
  CAS-verified equivalents (ADR-0002)

## Sections

### A. Math skills inventory (25 items, 30 min)

Five topic clusters × 5 items each; difficulty banded:

| Cluster | Items | Difficulty target |
|---|---|---|
| Algebra (linear + quadratic) | 5 | 2 easy, 2 medium, 1 hard |
| Functions + analysis | 5 | 2 easy, 2 medium, 1 hard |
| Calculus (derivatives + integrals) | 5 | 1 easy, 2 medium, 2 hard |
| Geometry + trigonometry | 5 | 2 easy, 2 medium, 1 hard |
| Probability + sequences | 5 | 2 easy, 2 medium, 1 hard |

**Item selection**:
- Drawn from Cena's own CAS-verified bank, not Ministry raw text
- Reference to Ministry distribution handled by
  `bagrut-reference-analyzer.py` — the pilot's items mirror the
  topic × difficulty distribution observed in recent Bagrut papers
- Every item has a CAS oracle answer; no free-text grading on the
  baseline

**Scoring**: 1 point per correct item; max 25. Partial credit
disabled for the baseline to keep scoring objective.

### B. Self-reported confidence (5 items, 3 min)

1. "How confident do you feel solving algebra equations?" (1–5)
2. "How confident do you feel with functions and graphs?" (1–5)
3. "How confident do you feel with calculus (derivatives /
   integrals)?" (1–5)
4. "How confident do you feel with geometry and trigonometry?" (1–5)
5. "How confident do you feel with probability and sequences?" (1–5)

### C. Language preference (2 items, 1 min)

1. "When learning math, which language do you prefer?"
   - Arabic only / Arabic primarily / Mixed / Hebrew primarily /
     Hebrew only
2. "Which language do you use most often to **explain** math to
   yourself (e.g. while thinking through a problem)?"
   - Arabic / Hebrew / Mix / English / Other

### D. Prior platform use (3 items, 2 min)

1. "Have you used any adaptive learning tool before?" (Y/N/Unsure)
2. "If yes, approximately how many total hours?"
   (0–5 / 6–20 / 21–50 / 50+)
3. "Which tool(s)?" (free text, optional)

### E. Demographics (5 items, 2 min)

- Age
- Grade (10 / 11 / 12)
- School name (pre-filled)
- Declared Bagrut track (3u / 4u / 5u)
- Home language(s) (multi-select: Arabic / Hebrew / English / Other)

## Data handling

- Responses stored under the pilot-consent bucket
  (`pilot-participation-arabic-2026`)
- Encrypted at rest; TLS in transit
- Retention: 12 months post-pilot-end, then aggregate-only
- Access: pilot-team named list in the school MOU

## Dry-run protocol (before real enrolment)

- 10 student volunteers (not in pilot cohort, any grade, consent
  granted under a dry-run bucket)
- Administer full instrument under target conditions
- Debrief: any item unclear? Any translation mismatch? Any timing
  issue?
- Iterate; final instrument locked after dry-run debrief

## Post-test scoring delta

Primary outcome = `B_post_score − B_pre_score` (mastery gain on
0–25 scale), compared between treatment and control groups via
ANCOVA with pre-score as covariate per
`arabic-first-pilot-design.md` §6.

## References

- Pilot design: `arabic-first-pilot-design.md`
- Exit criteria: `exit-criteria.md`
- ADR-0002 (CAS oracle): `docs/adr/0002-sympy-correctness-oracle.md`

---
**Status**: DRAFT — Prof. Amjad (items) + Dr. Nadia (wording) sign-off pending.
**Last touched**: 2026-04-19 (engineering draft)
