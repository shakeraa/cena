# Ship-gate positive-test fixture: banned citations

**Purpose**: This file deliberately contains every pattern in
`scripts/shipgate/banned-citations.yml`. The CI test suite
(`tests/shipgate/banned-citations.spec.mjs`) runs the scanner against this
fixture and asserts that EVERY rule fires at least once. If a rule does not
fire, either the rule is broken or this fixture has drifted.

**Do not clean this file.** Do not rephrase. Do not quote. Each line below is
a trap. This file is whitelisted in
`scripts/shipgate/banned-citations-whitelist.yml` so the scanner ignores it
on normal runs.

---

## FD-003 — 95% misconception resolution (REJECTED)

- Marketing copy claiming 95% misconception resolution is banned.
- Variant: 95 percent misconception resolution.
- Variant: ninety-five percent misconception resolution.
- Precision trap: 95.4% misconception resolution figure (fabricated).

## FD-008 — Yu et al. 2026 (REJECTED — unverifiable)

- Citation: Yu et al. 2026 — unverifiable.
- Variant: Yu 2026 finding.
- Variant: Yu, 2026 — as cited in axis docs.
- Variant: Yu and colleagues 2026.

## FD-011 — d=1.16 effect size (REJECTED — fabricated)

- Effect size d=1.16 for Socratic dialogue is fabricated.
- Variant: d = 1.16 reported in partial-credit marketing.
- Variant: effect size 1.16 cited in feature specs.
- Variant: d of 1.16 from the rejected citation.

## Hattie d=1.44 misuse (REJECTED — misapplied)

- Hattie's 1.44 effect size in a self-reported grades context is misapplied.
- Variant: d=1.44 self-reported — this is the original rubric, not a
  planning/reflective context.
- Variant: d=1.44 near reflective self-assessment — misuse.
- Variant: d=1.44 metacognitive planning — misuse.

## Interleaving d=0.5-0.8 (REJECTED — inflated)

- Interleaving d=0.5-0.8 overstates the meta-analytic effect (~0.34).
- Variant: interleaving d of 0.5 to 0.8.
- Variant: 0.5-0.8 interleaving benefit.
