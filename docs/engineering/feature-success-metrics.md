# Cena Feature Success Metrics Framework

**Status**: Active — applies to all features from Wave A onward.
**Owners**: Dr. Yael (statistical rigor), Dr. Nadia (learning-science validity), Dr. Rami (adversarial review)
**Source**: Panel review 2026-04-17, Round 4 cross-exam item 1; RDY-078
**ADR alignment**: GD-004 (prohibited metrics), ADR-0003 (privacy floor)

---

## 1. Why this document exists

The 2026-04-17 panel review of the user-personas synthesis identified a critical gap: twelve features were proposed for the roadmap with zero per-feature success metrics. Dr. Rami Khalil's adversarial note was precise: "'ship F11 first' without a metric is aspirational, not measurable."

This document establishes the discipline that prevents that from recurring. Every feature that touches code must pre-register at least one primary metric, one safety metric, and its failure threshold before a single line is merged to `main`.

The framework draws on three bodies of research:

- **Goodhart's Law** (Goodhart 1975; expanded by Strathern 1997): "When a measure becomes a target, it ceases to be a good measure." Optimising for a proxy metric corrupts the underlying goal. Every metric listed below carries an explicit Goodhart warning where the proxy risk is non-trivial.
- **Campbell's Law** (Campbell 1976): The more a quantitative social indicator is used for social decision-making, the more it will be subject to corruption pressures and distort the social processes it is intended to monitor. Applied here: the moment a metric appears on a team leaderboard or OKR, it starts to be gamed.
- **Self-Determination Theory** (Deci and Ryan 1985, 2000): intrinsic motivation is undermined by extrinsic surveillance, rewards, and comparisons. Metrics that reward activity without regard to quality destroy intrinsic motivation in learners.
- **"On the folly of rewarding A while hoping for B"** (Kerr 1975): organisations systematically reward the measurable at the expense of the intended. Applies directly to EdTech: optimising for session time when the goal is learning gain is Kerr's folly.

---

## 2. Metric taxonomy

### 2.1 Tier 1 — Learning metrics (primary signal)

These are the metrics that matter. Every feature must connect to at least one Tier 1 metric.

| Metric | Definition | Unit | Direction |
|--------|-----------|------|-----------|
| `mastery_gain` | BKT P(known) delta from session start to session end, per topic-skill pair | [0, 1] delta | Higher is better |
| `mastery_retention` | BKT P(known) at re-test after 7-day gap vs. end-of-session value | [0, 1] ratio | Closer to 1.0 is better |
| `hint_ladder_efficiency` | Correct answer rate after using the hint ladder, vs. control (no hints) | rate ratio | >1.0 with Cohen's d ≥ 0.2 |
| `explanation_quality` | (F1 only) LLM-judge score on student's natural-language explanation, pre-calibrated against 200-label ground truth | [0, 1] score | >0.7 mean in cohort |
| `diagnostic_accuracy` | (F7 only) RMSE between initial theta-hat and post-10-session theta-hat | theta units | <0.3 RMSE |

**Goodhart warning for `mastery_gain`**: A student who sees the same item repeatedly will drive P(known) toward 1 without learning. Guard: mastery gain is only credited when the item set rotates (no item repeats within a session for Tier 1 credit). Implementation must enforce item-diversity constraints.

**Goodhart warning for `hint_ladder_efficiency`**: If the hint ladder makes correct answers too easy to guess, efficiency goes up but learning goes down. Guard: pair with `mastery_retention` at 7 days. A feature that scores well on efficiency but poorly on retention fails both.

### 2.2 Tier 2 — Trust metrics (leading indicator)

These signals predict whether students, parents, and teachers will remain on the platform long enough for learning to occur. They are leading, not lagging. Optimising for them alone is Kerr's folly.

| Metric | Definition | Unit | Direction |
|--------|-----------|------|-----------|
| `parent_digest_open_rate` | Fraction of sent digests opened within 48 hours | rate | >0.40 within 4 weeks |
| `parent_digest_satisfaction` | One-question post-open rating ("Was this useful?") | [1, 5] | >3.8 mean |
| `teacher_console_dau` | Distinct teachers opening the heatmap console on a given day | count | >60% of enrolled teachers daily in pilot |
| `student_return_rate` | Students who return for a second session within 7 days | rate | >0.55 in first pilot cohort |
| `unsubscribe_rate` | Parents who opt out of digest within first 4 weeks | rate | <0.05 |

**Goodhart warning for `student_return_rate`**: Return rate optimised naively produces dark patterns (notifications, streaks, loss-aversion copy — all banned under GD-004). Guard: return rate is only a success metric when measured *without* any notification push to returning users during the measurement window. If the team sends a re-engagement push, the measurement window resets.

**Campbell warning for `parent_digest_open_rate`**: Once open rate becomes an OKR, teams start writing clickbait subject lines. Guard: subject lines are reviewed against the banned-terms list (see section 6) before each digest template ships.

### 2.3 Tier 3 — Safety metrics (ship-blockers)

These are not success metrics — they are pass/fail gates. Any feature in production that breaches a safety metric is immediately flagged for rollback review.

| Metric | Threshold | Action on breach |
|--------|-----------|-----------------|
| `shipgate_violation_count` | 0 per release | Block merge; fix before re-opening PR |
| `privacy_leak_incidents` | 0 misconception data outside session boundary | Immediate incident; ADR-0003 response protocol |
| `abuse_report_rate` | <0.001 per active student-day (F10 only) | Pause F10 peer features; DPIA review |
| `lljm_judge_false_positive_rate` | <0.10 (F1 only) | Disable explain-it-back prompting; re-calibrate |
| `accessibility_regression_count` | 0 WCAG AA regressions per release | Block merge |

### 2.4 Tier 4 — Engagement metrics (watch-list only)

These metrics are useful for diagnosing usability problems, but they are explicitly **not** primary success metrics. No team OKR, no feature review, no go/no-go decision should be made on Tier 4 metrics alone. They are watch-list metrics — anomalies trigger investigation, not celebration or alarm by themselves.

| Metric | What it tells you | Goodhart risk |
|--------|------------------|--------------|
| `session_duration_mean` | Are sessions completing naturally or cutting off? | HIGH — never optimise for time-in-app; that is Kerr's folly |
| `items_attempted_per_session` | Is pacing reasonable? | MEDIUM — more items != more learning |
| `hint_request_rate` | Are students stuck? | MEDIUM — high rate could mean confusion; low rate could mean avoidance |
| `completion_rate` | Do sessions finish? | MEDIUM — students who quit a too-easy session look like dropouts |
| `feature_activation_rate` | Is the feature being discovered? | LOW — funnel health only |

**Any proposal to use a Tier 4 metric as a primary success metric requires written sign-off from Dr. Yael and Dr. Nadia with a documented rationale. No exceptions.**

---

## 3. Instrumentation standards

### 3.1 Event schema

All analytics events emitted by Cena features follow this schema. Events are session-scoped per ADR-0003.

```json
{
  "event_name": "<tier>.<feature>.<action>",
  "session_id": "<uuid — session scope, not student PII>",
  "cohort_tag": "control | treatment-<feature-id>",
  "timestamp_utc": "<ISO-8601>",
  "feature_id": "<RDY-NNN feature code>",
  "metric_name": "<taxonomy name from section 2>",
  "metric_value": "<number or boolean>",
  "wave": "<A | B | C | D>",
  "institute_id": "<uuid — tenant scope; never student-id>",
  "schema_version": "1.0"
}
```

**Field rules**:
- `session_id` is a UUID generated at session start. It is the only correlation key for analytics. It must NOT be stored alongside student name, device identifier, or any other PII. See ADR-0003 section Decision 1.
- `institute_id` is required for tenant scoping. It may appear in analytics — it is not student PII.
- `student_id` is PROHIBITED in analytics events. If a feature currently emits `student_id` in any analytics path, that is a bug to be fixed before the feature enters a pilot.
- `cohort_tag` is required for all A/B measurements. If a session is not under active experiment, set to `control`.
- Events carrying misconception data MUST have tag `[ml-excluded]` in the event metadata (see ADR-0003, Decision 3). No misconception event type may appear in any analytics pipeline that feeds a machine-learning training corpus.

**Event name convention**: `<tier>.<feature-id>.<action>` where tier is one of `learning`, `trust`, `safety`, `engagement`. Examples:
- `learning.f11.hint_ladder_completed`
- `trust.f5.digest_opened`
- `safety.f10.abuse_reported`
- `engagement.f3.accommodations_toggled` (Tier 4 — watch list only)

### 3.2 Cohort tagging

Experiments use cohort tags assigned at session initialisation. Rules:
- A session is in exactly one cohort. No session is in both `control` and `treatment`.
- Cohort assignment is randomised at the institute level, not the student level, to avoid contamination where students in the same class compare experiences. Exception: F3 (accommodations) is not randomised — accommodations are opt-in per student need, not experimental.
- The randomisation seed is logged (not the student ID) to allow reproducibility audits.
- Experiment duration is fixed in the pre-registration form (see section 5). Do not extend an experiment mid-run because the result is inconvenient. That is p-hacking.

### 3.3 Privacy bounds (ADR-0003 enforcement)

| Data type | Retention in analytics pipeline | Notes |
|-----------|-------------------------------|-------|
| Session events (non-misconception) | 90 days raw; then aggregate-only | Raw deleted by `RetentionWorker` |
| Misconception events | 30 days raw; then deleted | Governed by ADR-0003 Decision 2 |
| Aggregated cohort stats | Indefinite | Must satisfy k-anonymity: k ≥ 10. Any cohort slice with fewer than 10 sessions is suppressed. |
| Student-level analytics | Prohibited | `student_id` must not appear in analytics events |

k-anonymity floor: if a metric is computed for a cohort slice (e.g. "Arabic-speaking students in school X"), and that slice contains fewer than 10 sessions, suppress the result. Do not report it, even internally. This is not optional — it is a COPPA + GDPR-K compliance requirement.

---

## 4. Statistical rules

These rules apply to any interpretation of metric data that influences a go/no-go decision.

### 4.1 Minimum sample size

Before interpreting any metric as a signal, the following minimums must be met:

| Metric tier | Minimum sessions | Minimum calendar days |
|------------|-----------------|----------------------|
| Tier 1 (learning) | 200 completed sessions | 14 days |
| Tier 2 (trust) | 100 parent interactions or 50 teacher interactions | 14 days |
| Tier 3 (safety) | No minimum — any single breach triggers action | N/A |
| Tier 4 (engagement) | 500 sessions | 7 days |

Power analysis basis: Tier 1 learning metrics target Cohen's d ≥ 0.2 (small but educationally meaningful effect per Hattie's Visible Learning meta-analysis). At 80% power, two-tailed alpha 0.05, detecting d = 0.2 requires approximately 197 sessions per arm. The 200-session floor rounds up to a safe margin.

For features targeting a specific subgroup (e.g. Arabic-first cohort for F2), the minimum applies to that subgroup specifically, not the total user count.

### 4.2 Significance thresholds

A feature is declared successful on a Tier 1 metric when BOTH of the following are true:
1. p < 0.05 (two-tailed, uncorrected for multiple comparisons when testing a single pre-registered primary metric)
2. Cohen's d ≥ 0.2 (or equivalent effect size for non-continuous metrics: odds ratio ≥ 1.25 for binary outcomes; rank-biserial correlation ≥ 0.15 for ordinal)

A feature that achieves p < 0.05 but d < 0.2 is statistically detectable but educationally trivial. Do not declare it a success.

A feature that achieves d ≥ 0.2 but p ≥ 0.05 has insufficient evidence. Do not declare it a success. Extend data collection if still within the pre-registered window.

### 4.3 Multiple testing guardrails

Each feature pre-registers exactly ONE primary Tier 1 metric. Secondary metrics are exploratory and may not be used to declare success.

If a feature tests multiple subgroup hypotheses (e.g. "does F11 work equally well for anxious students vs. confident students?"), apply Benjamini-Hochberg FDR correction. Do not apply Bonferroni — it is too conservative for exploratory subgroup analysis and will suppress real signals. The correction procedure:
1. Sort all p-values for the k subgroup tests from smallest to largest: p(1), p(2), ..., p(k)
2. Find the largest i such that p(i) ≤ (i / k) × 0.05
3. Reject all null hypotheses for indices 1 through i

Pre-planned subgroup analyses are included in the pre-registration form (section 5). Any subgroup analysis not in the pre-registration is exploratory and labeled as such. Exploratory findings generate hypotheses for the next experiment; they do not generate product decisions.

### 4.4 Confidence intervals over p-values

Report 95% confidence intervals alongside effect sizes. A finding of "d = 0.22, 95% CI [0.08, 0.36]" communicates uncertainty. A finding of "p = 0.043" does not. Internal team communications must include CIs. Dashboard displays showing only p-values are prohibited.

### 4.5 Interim analysis and early stopping

Experiments may include one pre-registered interim analysis at 50% of the planned sample size. Rules:
- Use O'Brien-Fleming alpha spending (critical value approximately 0.0054 at interim, 0.0492 at final) to preserve overall alpha at 0.05
- If the interim shows p < 0.0054 AND d ≥ 0.2, early stopping is permitted
- If the interim shows a safety concern (any Tier 3 breach), the feature is paused regardless of learning results
- No additional interim analyses beyond the one pre-registered. "Peeking" between the interim and the final is p-hacking

---

## 5. Pre-registered hypothesis flow

Before any feature merges to `main`:

1. Feature owner fills out `docs/engineering/metrics-pre-registration-template.md`
2. Template is submitted as a file in the same PR as the feature code
3. Dr. Yael reviews the statistical plan. Dr. Nadia reviews the learning-science validity. Dr. Rami reviews for honesty.
4. Template is locked (read-only in git history) on merge. Changes after merge require a new version with explicit amendment notes.
5. At the pre-registered observation window close, results are compared against the locked hypothesis. If the feature misses its pre-registered target, the team writes a public (intra-team) post-mortem. The miss is not silently dropped.

The discipline here is borrowed from clinical trial pre-registration (CONSORT, ClinicalTrials.gov), adapted for a software feature context. The academic basis: pre-registration reduces false-positive rates in exploratory research by approximately 50% (Nosek et al. 2018, "The pre-registration revolution," PNAS).

---

## 6. Prohibited metrics

The following are explicitly banned as primary, secondary, or watch-list metrics for any feature shipped on the Cena platform. Using them as metrics — in code, dashboards, Grafana JSON, analytics event names, OKR documents, or team discussions — is a GD-004 violation and a ship-gate block.

### 6.1 Complete banned list

| Prohibited metric | Why banned | GD-004 rule | Allowed alternative |
|------------------|-----------|-------------|-------------------|
| Streak count (any definition) | Loss aversion; the streak going to zero causes distress that is unrelated to learning (Deci & Ryan SDT: external control undermines intrinsic motivation) | GD-004 rule 1 | Daily cadence signal (absolute progress, no zero-state punishment) |
| Streak restoration or freeze mechanic | Variable-ratio reward schedule; slot-machine engagement pattern | GD-004 rule 1 | None — no direct alternative; remove the mechanic |
| Time-in-app maximization | Kerr (1975) folly: rewarding time when the goal is learning. ICO Children's Code Standard 4 flags this explicitly for children's products | GD-004 rule 3 | Session completion rate (was the session finished? Not how long did it take?) |
| Variable-ratio reward rate | Slot-machine mechanics; produces compulsive engagement without learning gain | GD-004 rule 2 | Fixed-schedule mastery celebration (session completion only, brief) |
| Comparative percentile display ("top 20% of students") | Social comparison on minors; FTC v. Epic precedent; ICO Children's Code Standard 6 | GD-004 rule 6 | Absolute mastery level (high / medium / developing); no comparison to other students |
| Public leaderboard of any kind | Social pressure on minors; ranking of academic performance is a harm pattern | GD-004 rule 6 | None — leaderboards are banned entirely |
| "Days without missing" counter | Loss-aversion framing | GD-004 rule 1 | Absolute days practiced (positive frame, no zero-punishment) |
| FOMO urgency metrics ("students practicing now", "limited time") | Artificial urgency; dark-pattern engagement | GD-004 rule 3 | None — remove the mechanic |
| Shame-adjacent re-engagement signals ("your tutor misses you") | Guilt-based motivation; contradicts SDT | GD-004 rule 4 | Neutral content-based notifications only, strictly opt-in |
| Predicted Bagrut score before calibration | Dishonest number; implies IRT-to-Bagrut calibration that does not yet exist (Dr. Yael panel note) | Not GD-004 — ADR-0002 | Mastery trajectory (high / medium / developing) with stated confidence |
| Any metric derived from cross-student comparison of misconception data | Reveals identifiable learning struggles; ADR-0003 violation | ADR-0003 | k-anonymized catalog-level misconception frequency (k ≥ 10) |

### 6.2 Naming conventions for prohibited signals

The CI scanner (`scripts/shipgate/scan.mjs`) checks event names, dashboard JSON keys, and Grafana metric labels against a banned-term list (see `docs/engineering/ci-prohibited-metrics-rules.md`). Event names that contain the following substrings are prohibited:

`streak`, `days_missed`, `chain`, `bonus_multiplier`, `time_spent_maximiz`, `ranking`, `leaderboard`, `percentile_rank`, `fomo`, `urgency`, `lose`, `miss`, `freeze`, `restore_streak`

If a legitimate use of a banned term exists (e.g. "streak" appearing in a physics question about electrical phenomena), it must be added to `scripts/shipgate/allowlist.json` with a mandatory justification field reviewed in PR.

---

## 7. Retiring a metric

Metrics become unreliable proxies. When a metric is no longer measuring what it was designed to measure, it must be retired. The retirement process:

1. **Identify the proxy drift**: document the evidence that the metric is now measuring something other than its stated intent (Goodhart, Campbell). Minimum bar: two consecutive observation windows where the metric moves without corresponding movement in the underlying Tier 1 goal.
2. **Propose a replacement**: the replacement metric must go through the pre-registration flow as if it were a new feature metric. Do not simply swap metric names in a dashboard.
3. **Migration period**: run both old and new metrics in parallel for one observation window. Document the correlation. If they are strongly correlated (|r| > 0.7), the retirement is validated. If they are not correlated, the new metric may be measuring something different — investigate before completing the switch.
4. **Archive, don't delete**: retired metrics remain in the event schema with status `deprecated`. Historical data is preserved. Dashboards are updated to show the new metric with a note referencing the old one.
5. **Notify**: communicate the retirement to all teams that cited the metric in their feature pre-registration. Those teams must file amended pre-registration forms.

---

## 8. Concrete examples — F11, F5, F8

### F11 — Anxiety-safe hint ladder

**Primary Tier 1 metric**: `hint_ladder_efficiency` — correct-answer rate after using any hint in the ladder, relative to the counterfactual of no-hint available.

**Pre-registered hypothesis**: Within 4 weeks and 200 completed sessions in the treatment arm, students who use the hint ladder achieve a correct-answer rate ≥ 0.20 higher than the control arm (no ladder), with p < 0.05 and d ≥ 0.2.

**Retention check**: `mastery_retention` at 7 days — treatment students must retain ≥ 85% of the BKT gain achieved during the session. If efficiency is up but retention drops, the hint ladder is providing answers, not scaffolding — fix the prompt design.

**Safety metric**: no WCAG AA regressions on hint display components. No GD-004 violations in hint copy (hint text must not shame for needing help, must not badge for refusing help).

**Goodhart watch**: if hint requests per session rises above 80% of all steps, the ladder is being used as a shortcut, not a scaffold. Flag for UX investigation.

**Prohibited proxy**: do not measure "number of hints used" as a success metric. High hint usage is not success; it is a signal to investigate.

---

### F5 — Weekly parent digest (email)

**Primary Tier 2 metric**: `parent_digest_satisfaction` — one-question post-open rating "Was this useful?" with a target of ≥ 3.8 mean within 4 weeks.

**Pre-registered hypothesis**: Within 4 weeks and 100 parent interactions, email digests achieve ≥ 3.8 mean satisfaction rating, with at least 40% of sent digests opened (open rate is Tier 2 watch-list only — it is not the primary metric, but collapse below 0.20 triggers a UX review).

**Tier 1 linkage**: F5's downstream learning hypothesis is that parents who receive digests have higher student return rates. Secondary metric (exploratory, not primary): `student_return_rate` for students whose parents opened the digest vs. parents who did not. This is exploratory — no product decision is made on it without its own pre-registration cycle.

**Prohibited content in digest**: the digest content CI check (see `docs/engineering/ci-prohibited-metrics-rules.md`) must pass before each digest template version merges. Banned in digest body: any streak count, comparative ranking, FOMO urgency, or shame-adjacent framing ("Amir hasn't practiced in X days" without compassionate reframe). Dr. Nadia's panel note applies: content must be absolute and self-referential, never comparative.

**Prohibited proxy**: open rate is NOT the primary metric. Optimising for open rate produces clickbait subject lines (Campbell's Law). Open rate is a health indicator; satisfaction is the metric.

---

### F8 — Mastery trajectory (formerly "grade prediction")

**Panel decision (Dr. Yael, 2026-04-17)**: F8 is renamed from "grade prediction" to "mastery trajectory" because Cena does not yet have a calibrated IRT-to-Bagrut scale score mapping. Any display of a predicted Bagrut score before that calibration exists is a prohibited metric (see section 6.1, row 10).

**What ships until calibration exists**:
- Mastery level: High / Medium / Developing (not a numeric score)
- 80% BKT confidence interval displayed as a range band, not a point estimate
- Explicit label on the UI: "Cena mastery estimate — not a predicted Bagrut score"

**Primary Tier 1 metric (post-calibration)**: `diagnostic_accuracy` — RMSE between initial theta-hat and post-10-session theta-hat, targeting < 0.3 RMSE. This metric is not reportable until a calibration cohort (students who used Cena AND sat a real Bagrut) exists.

**Primary Tier 2 metric (current phase)**: `parent_digest_satisfaction` on the mastery-trajectory section specifically — a sub-question on the parent digest asking "Was the mastery summary useful?"

**Pre-registered hypothesis for current phase**: within 4 weeks and 100 parent digest opens, ≥ 3.5 mean rating on the mastery-trajectory section (lower bar than F5 full digest because this is a sub-component).

**Hard gate**: the team must file a Calibration Data Plan (a one-page doc in `docs/engineering/`) before F8 is allowed to display any numeric Bagrut score prediction. Dr. Yael must approve. No code review, no PR, no deployment unblocks this gate.

---

## 9. Compliance checklist for feature owners

Before opening a PR for any feature in RDY-065 through RDY-077:

- [ ] Pre-registration form filed (see `docs/engineering/metrics-pre-registration-template.md`) and included in the PR
- [ ] Primary Tier 1 metric identified and connected to a learning outcome
- [ ] Safety metric threshold confirmed (Tier 3)
- [ ] No banned terms in event names, dashboard keys, or analytics schemas (CI scanner will catch this — review before pushing)
- [ ] `session_id` is the only correlation key in analytics events — no `student_id`
- [ ] k-anonymity floor verified: no cohort slice smaller than 10 sessions will be surfaced
- [ ] Misconception events (if any) tagged `[ml-excluded]` and excluded from analytics aggregation
- [ ] Observation window and sample-size minimum stated in the pre-registration form
- [ ] Failure condition documented: "if we don't hit this threshold within this window, we do X"

---

*This document is version 1.0. Amendment protocol: file a PR that modifies this document, include the reason for the change, and get sign-off from Dr. Yael, Dr. Nadia, and Dr. Rami before merge.*
