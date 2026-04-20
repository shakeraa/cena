# Ship-gate positive-test fixture: effect-size citations (ADR-0049)

**Purpose**: This file deliberately contains every pattern in
`scripts/shipgate/effect-size-citations.yml`. The CI test suite
(`tests/shipgate/effect-size-citations.spec.mjs`) runs the scanner against
this fixture and asserts that EVERY rule fires at least once.

**Do not clean this file.** Do not rephrase. Do not add `citation_id=` tags.
Each line below is a trap — bare effect-size claims without backing citations
MUST trip the scanner. This file is whitelisted in
`scripts/shipgate/effect-size-citations-whitelist.yml`.

---

## Bare "d = N.NN" claims (REJECTED without citation_id=)

- The intervention showed d=0.34 in three independent replications.
- Variant: d =0.5 was reported for the control condition.
- Variant: d=1.16 for contextually-framed problems (cherry-picked ES).

## Bare "d of N.NN" claims

- d of 0.83 was cited in the primary study.
- Variant: d of 1.44 for self-reported grades is Hattie's figure.

## Bare "effect size N.NN" claims

- The meta-analysis reported an effect size 0.34 across 112 studies.
- Variant: effect size of 0.5 in favour of interleaving.

## Bare "ES N.NN" claims

- ES 0.37 for immediate vs. delayed feedback.
- Variant: ES of 0.40 for the practice-testing tradition.

## Bare "Cohen's d = N.NN" claims

- Cohen's d = 0.34 is the meta-analytic mean for interleaved practice.
- Variant: Cohen's d=0.5 in the Brummair & Richter meta-analysis.

## Bare percentage-gain claims framed as research effect

- 95% improvement in learning was reported in the Eedi RCT (fabricated — this is FD-003).
- Variant: 84% boost in retention was claimed for the Bayesian CAT pipeline.
- Variant: 25% gain in mastery across the longitudinal sample.
