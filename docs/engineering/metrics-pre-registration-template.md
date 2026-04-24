# Feature Metrics Pre-Registration Form

**Instructions**: Fill out this form before any feature code merges to `main`. Include this file as a commit in the same PR as the feature. The form is locked on merge — amendments require a new version filed in a separate PR with explicit change notes. See `docs/engineering/feature-success-metrics.md` for the full framework this form is based on.

Reviewers: Dr. Yael (statistical plan), Dr. Nadia (learning-science validity), Dr. Rami (honesty check).

---

## Section 1 — Feature identity

| Field | Value |
|-------|-------|
| Feature name | |
| RDY task ID | |
| Feature code (F1–F12 or later) | |
| Wave | A / B / C / D |
| PR URL | |
| Pre-registration author | |
| Pre-registration date | |
| Statistical reviewer (Dr. Yael or delegate) | |
| Learning-science reviewer (Dr. Nadia or delegate) | |

---

## Section 2 — Primary metric

**Rule**: exactly one primary metric. It must be Tier 1 (learning) unless the feature has no direct learning-outcome pathway, in which case Tier 2 (trust) is allowed with written justification. Tier 4 (engagement) is never permitted as a primary metric.

| Field | Value |
|-------|-------|
| Primary metric name (from taxonomy) | |
| Metric tier | 1 / 2 |
| Justification if Tier 2 | |
| Measurement unit | |
| Baseline value (current or estimated) | |
| Target value (minimum threshold for success) | |
| Expected direction of change | increase / decrease |
| Effect size floor | Cohen's d ≥ ___ OR odds ratio ≥ ___ |

---

## Section 3 — Hypothesis statement

Write the hypothesis in plain language, exactly as it will be evaluated at observation window close. No wiggle room.

> "We expect [primary metric] to [increase / decrease] from [baseline] to [target] within [N] weeks, measured over [M] completed sessions in the treatment arm. If this threshold is not met, we will [describe the failure action: revisit the feature design / extend the window once by N weeks / retire the feature]."

Fill in:

**Hypothesis**: We expect ________ to ________ from ________ to ________ within _____ weeks, measured over _____ completed sessions in the treatment arm. If this threshold is not met, we will ________.

---

## Section 4 — Statistical plan

| Field | Value |
|-------|-------|
| Minimum sessions per arm | (minimum 200 for Tier 1; 100 for Tier 2) |
| Minimum calendar days | (minimum 14) |
| Significance threshold | p < 0.05 (two-tailed) |
| Effect size floor | d ≥ 0.2 (or equivalent; see framework section 4.2) |
| Pre-registered interim analysis? | yes / no |
| Interim analysis at (% of planned sessions) | 50% (if yes) |
| Interim stopping rule | O'Brien-Fleming (alpha ≈ 0.0054 at interim) |
| Multiple comparisons correction (if >1 subgroup) | Benjamini-Hochberg FDR |
| Observation window end date | |

---

## Section 5 — Safety and prohibited-metric check

| Check | Status |
|-------|--------|
| Tier 3 safety metric identified | yes / no |
| Safety metric threshold defined | yes / no — threshold: |
| Event names reviewed against banned-term list | yes / no |
| No `student_id` in analytics events | confirmed / not applicable |
| k-anonymity floor (k ≥ 10) enforced for all cohort slices | confirmed |
| Misconception events tagged `[ml-excluded]` (if applicable) | confirmed / not applicable |
| No prohibited metric in primary or secondary position | confirmed |
| Shipgate CI scan passed locally before PR opened | yes / no |

---

## Section 6 — Secondary and exploratory metrics

List any secondary metrics here. Secondary metrics are exploratory — they generate hypotheses for future experiments but do not determine go/no-go for this feature.

| Metric name | Tier | What it tells us | Risk of proxy corruption |
|-------------|------|-----------------|-------------------------|
| | | | |
| | | | |

Pre-planned subgroup analyses (include now to avoid post-hoc fishing):

| Subgroup | Metric | Hypothesis | Correction applied |
|----------|--------|-----------|-------------------|
| | | | Benjamini-Hochberg |
| | | | Benjamini-Hochberg |

---

## Section 7 — Failure protocol

If the primary metric misses its pre-registered threshold at observation window close:

1. Who writes the post-mortem? ________ (must be the feature owner or team lead)
2. Where is it posted? intra-team Slack / Notion / docs/post-mortems/ (delete inapplicable)
3. Deadline for post-mortem after window close: _____ days
4. Is a one-time extension permitted? yes / no — if yes, maximum additional days: _____
5. If the feature fails twice (original window + one extension), what happens? ________ (retire / redesign / escalate to Dr. Rami for adversarial review)

**Rule**: post-mortems are not optional. A feature that misses its pre-registered target and produces no post-mortem is a process violation. The coordinator tracks this.

---

## Section 8 — Approvals

| Reviewer | Role | Approved (yes / no) | Date | Notes |
|----------|------|---------------------|------|-------|
| | Statistical (Dr. Yael or delegate) | | | |
| | Learning science (Dr. Nadia or delegate) | | | |
| | Adversarial (Dr. Rami or delegate) | | | |
| | Feature owner | | | (self-sign) |

---

*This form is version 1.0. Do not modify it after merge without filing an amendment. Amendment PRs must include change notes and re-approval from all three reviewers.*
