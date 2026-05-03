# CI Prohibited Metrics Rules

**Purpose**: This document specifies the patterns, file targets, and enforcement logic that the CI scanner (`scripts/shipgate/scan.mjs`) must check to prevent banned metric language from shipping in dashboard configurations, analytics event schemas, and Grafana config files. It extends the existing GD-004 content-scan rules (documented in `docs/engineering/shipgate.md`) to cover metrics and instrumentation artefacts specifically.

Do NOT modify `scripts/shipgate/scan.mjs` directly based on this document — this is a specification. Engineering implements the scanner changes in a separate task. PRs that fail these checks may not merge without an allowlist entry (`scripts/shipgate/allowlist.json`) containing a mandatory justification field.

---

## 1. Scope of scan

The CI scanner must scan the following file types and paths on every PR:

| File type | Path glob | What to check |
|-----------|-----------|--------------|
| Grafana dashboard JSON | `ops/grafana/**/*.json` | Metric names, panel titles, legend labels, alert names |
| Analytics event schema files | `src/**/analytics/**/*.json`, `src/**/telemetry/**/*.json` | Event name keys, property name keys |
| Analytics event schema files | `src/**/analytics/**/*.ts`, `src/**/telemetry/**/*.ts` | String literals used as event names or metric keys |
| Locale files | `src/**/locales/en.json`, `src/**/locales/ar.json`, `src/**/locales/he.json` | All string values (existing GD-004 scan — extend to metric-flavoured strings) |
| Vue templates | `src/**/*.vue` | Metric display labels, v-bind strings bound to metric names |
| C# telemetry code | `src/**/*.cs` | String literals in event-name positions, metric-name enums |
| Pre-registration forms | `docs/engineering/metrics-pre-registration-*.md` | Prohibited metric names in the primary metric field (Section 2) |
| Parent digest templates | `src/**/digest/**/*.html`, `src/**/digest/**/*.mjml` | Digest body content |

The scanner does NOT need to scan test files (`.spec.ts`, `.test.cs`, `*Test.cs`, `*Tests.cs`) — false-positive risk outweighs benefit for test utilities that intentionally reference banned names to assert they are banned.

---

## 2. Prohibited term patterns

### 2.1 Streak-family patterns

These patterns catch streak counters, streak-freeze mechanics, and streak-adjacent engagement loops.

```
# Exact substrings (case-insensitive)
streak
days_without_miss
days_missed
chain_length
chain_count
keep_the_chain
lose_streak
break_streak
streak_freeze
restore_streak
streak_multiplier
streak_bonus
consecutive_days
consecutive_sessions
dont_break
don't_break

# Regex patterns (case-insensitive, applied to JSON keys and string literals)
/streak[_\-.]?count/i
/streak[_\-.]?length/i
/streak[_\-.]?days/i
/day[_\-.]?streak/i
/session[_\-.]?chain/i
/freeze[_\-.]?item/i
```

**Allowlist rationale**: the word "streak" may appear legitimately in physics content (electrical discharge). Any allowlist entry for "streak" in a non-metric context requires the justification field to include the physics context and must be in a content file (locale JSON or question bank), not in a metrics/telemetry file. "streak" in any file under `src/telemetry/`, `ops/grafana/`, or a pre-registration form has no legitimate non-metric use and cannot be allowlisted.

### 2.2 Variable-ratio reward patterns

These patterns catch slot-machine engagement mechanics expressed as metric names or dashboard labels.

```
# Exact substrings (case-insensitive)
variable_ratio
random_reward
bonus_multiplier
loot
spin
jackpot
mystery_reward
surprise_reward
reward_probability

# Regex patterns
/reward[_\-.]?rate[_\-.]?variable/i
/engagement[_\-.]?multiplier/i
/bonus[_\-.]?unlock/i
```

### 2.3 Time-in-app maximization patterns

These patterns catch metrics that reward or track time-in-app as a primary signal.

```
# Exact substrings (case-insensitive)
time_in_app
minutes_in_app
hours_in_app
session_duration_maximiz
total_time_spent_maximiz
daily_time_goal
time_on_platform_maximiz
engagement_minutes_target

# Regex patterns
/time[_\-.]?maximiz/i
/duration[_\-.]?target[_\-.]?increas/i
/session[_\-.]?time[_\-.]?goal/i
```

Note: `session_duration_mean` and `session_duration_p95` are NOT banned — measuring session duration for diagnostic purposes (is the session too long? too short?) is allowed. What is banned is using duration as a success metric or target. The distinguishing test: if the metric name appears in a Grafana panel titled "Goals" or "Success Metrics", it fails. If it appears in a panel titled "Health" or "Diagnostics", it passes.

To implement this context-sensitive check, the scanner must look at the Grafana panel `title` field alongside the metric name. See rule 4.1 below.

### 2.4 Comparative ranking patterns

These patterns catch leaderboards, percentile ranks, and social comparison displays.

```
# Exact substrings (case-insensitive)
leaderboard
percentile_rank
rank_position
class_rank
top_percent
bottom_percent
ranked_vs_peers
compared_to_class
ahead_of_peers
behind_peers
better_than_average
worse_than_average

# Regex patterns
/rank[_\-.]?(?:among|vs|compared)/i
/percentile[_\-.]?(?:rank|position|display)/i
/student[_\-.]?comparison/i
/peer[_\-.]?ranking/i
```

### 2.5 FOMO and urgency patterns

These patterns catch artificial urgency metrics and displays.

```
# Exact substrings (case-insensitive)
fomo_trigger
urgency_score
limited_time
countdown_pressure
scarcity_signal
users_active_now_pressure
only_N_left
expires_soon

# Regex patterns
/limited[_\-.]?time[_\-.]?(?:metric|score|signal)/i
/urgency[_\-.]?(?:metric|score|trigger)/i
/countdown[_\-.]?(?:to|pressure)/i
```

### 2.6 Predicted Bagrut score patterns (pre-calibration gate)

These patterns catch premature display of predicted Bagrut scores before the calibration cohort exists. This is an ADR-0002 + Dr. Yael panel decision enforcement point.

```
# Exact substrings (case-insensitive)
predicted_bagrut_score
bagrut_prediction
predicted_grade
expected_bagrut
forecast_bagrut
estimated_exam_score

# Regex patterns
/bagrut[_\-.]?(?:score|grade|prediction|forecast|estimate)[_\-.]?(?:point|numeric|display)/i
/predicted[_\-.]?(?:exam|bagrut|grade)[_\-.]?(?:score|number)/i
```

**Allowlist condition**: once the Calibration Data Plan is approved by Dr. Yael and committed to `docs/engineering/`, these patterns may be allowlisted specifically for the F8 mastery-trajectory feature. The allowlist entry must reference the Calibration Data Plan document path. Until that document exists, these patterns are unconditionally blocked.

### 2.7 Misconception cross-student comparison patterns

These patterns catch any metric that derives cross-student or cross-cohort signals from misconception event data without k-anonymity enforcement. This is an ADR-0003 enforcement point.

```
# Exact substrings (case-insensitive)
misconception_by_student
student_misconception_rank
misconception_leaderboard
per_student_buggy_rule

# Regex patterns
/misconception[_\-.]?(?:profile|timeline|history)[_\-.]?(?:export|display|api)/i
/buggy[_\-.]?rule[_\-.]?per[_\-.]?student/i
```

---

## 3. Grafana JSON specific rules

Grafana dashboard JSON has several locations where a metric name can appear. The scanner must check all of them:

```
# JSON paths to scan within ops/grafana/**/*.json
$.panels[*].title                          # panel title — context-sensitive
$.panels[*].targets[*].expr               # Prometheus/InfluxDB query expression
$.panels[*].targets[*].legendFormat       # legend label text
$.panels[*].fieldConfig.defaults.displayName  # display name override
$.panels[*].options.legend.displayMode    # check for "leaderboard" context
$.annotations[*].name                     # annotation names
$.panels[*].alerts[*].name               # alert names
$.rows[*].panels[*].title                # nested panel titles (older Grafana format)
$.templating.list[*].name               # variable names
$.templating.list[*].label              # variable labels
```

**Context-sensitive rule for `session_duration`**: if a panel title contains any of the strings `goal`, `target`, `success`, `metric`, `kpi` (case-insensitive) AND the panel's metric expression contains `session_duration`, the combination fails. Add this as a compound rule in the scanner.

---

## 4. Analytics event name rules

Analytics events emitted by the application are subject to the following naming constraints beyond the banned-term scan:

### 4.1 Event name format enforcement

Event names must match the pattern:
```
/^(learning|trust|safety|engagement)\.[a-z0-9_]+\.[a-z0-9_]+$/
```

Any event name that does not match this pattern fails the CI check. This enforces the taxonomy prefix requirement from `docs/engineering/feature-success-metrics.md` section 3.1.

### 4.2 Banned prefixes in event names

The following event name prefixes are prohibited (they imply a metric is primary when it should not be):

```
engagement.streak_
engagement.chain_
engagement.leaderboard_
engagement.rank_
trust.ranking_
trust.comparative_
```

### 4.3 Student ID leak detection

The scanner must flag any analytics event emission in TypeScript or C# source code where the event object literal contains a property named any of:

```
studentId
student_id
userId (in a student-context file)
user_id (in a student-context file)
learnerId
learner_id
```

"Student-context file" is any file under `src/actors/`, `src/student-web/`, or `src/shared/` that imports from a student aggregate or student session namespace. The scanner can detect this via import-statement analysis — if a file imports `StudentState`, `LearningSessionState`, or any type from `Cena.Actors.Students`, it is a student-context file for this purpose.

This check targets a specific failure mode: a developer adds a student ID to an analytics event for debugging, ships it, and inadvertently creates a student-identifiable analytics record in violation of ADR-0003.

---

## 5. Parent digest content rules

Parent digest templates (HTML/MJML) are subject to the same GD-004 content scan as locale files, plus the following digest-specific additions:

```
# Additional banned strings in digest content
Amir hasn't practiced          # shame-adjacent framing
hasn't practiced in            # shame-adjacent framing
0 sessions this week           # shame (must be replaced with compassionate reframe)
behind the class               # comparative shaming
behind schedule                # comparative shaming
below average                  # comparative ranking
only N minutes                 # scarcity/urgency framing
streak                         # (already in main list)
```

The scanner must also check that digest templates do not contain raw BKT P(known) values without a mastery-level label. A number like "0.73" in isolation in a digest is ambiguous and confusing; it must be accompanied by a label ("Mastery level: Medium"). This is a UX rule, not strictly a privacy rule, but it prevents the kind of "number theater" that Dr. Yael flagged for F8.

---

## 6. Allowlist management

The allowlist file is `scripts/shipgate/allowlist.json`. Format per entry:

```json
{
  "file": "path/relative/to/repo/root",
  "line": 42,
  "term": "streak",
  "justification": "Physics term: electrical streak discharge in question body",
  "approved_by": "claude-code",
  "approved_date": "2026-04-19",
  "expires": null
}
```

Rules:
- `justification` is mandatory. A PR with an empty justification field fails.
- Metrics and telemetry files (`src/telemetry/`, `ops/grafana/`, analytics schemas) may not use the `justification` field to allowlist streak or leaderboard terms. Those terms have no legitimate use in those files.
- Allowlist entries with `"expires": "<date>"` are automatically re-flagged after that date. Use expiry dates for temporary allowances (e.g., during a migration period).
- The coordinator reviews all new allowlist entries. Any entry for a metrics file is escalated to Dr. Rami for adversarial review before merge.

---

## 7. Failure modes and exit codes

| Exit code | Meaning | PR action |
|-----------|---------|-----------|
| 0 | All checks pass | PR may proceed |
| 1 | Banned term found in non-allowlisted file | Block merge; developer must either remove the term or file an allowlist entry with justification |
| 2 | Analytics event name fails format check | Block merge; rename the event |
| 3 | Student ID found in analytics event | Block merge; this is an ADR-0003 violation; escalate to coordinator |
| 4 | Predicted Bagrut score pattern found without Calibration Data Plan | Block merge; Dr. Yael gate not satisfied |
| 5 | Misconception cross-student metric pattern found | Block merge; ADR-0003 violation |

Exit codes 3, 4, and 5 are escalation-level failures. They must be reported to the coordinator via the queue messaging system, not just surfaced as a CI check failure.

---

## 8. Relationship to existing GD-004 scan

`docs/engineering/shipgate.md` describes the existing CI scan for dark-pattern content in locale files, Vue templates, and C# source. The rules in this document extend that scan to cover metrics-specific artefacts (Grafana JSON, analytics schemas, event name strings, digest templates). They do not replace the existing scan — both run on every PR.

The implementation should extend the existing `scan.mjs` scan loop rather than creating a parallel scanner. The banned-term lists in this document should be merged with the existing lists in a structured way (e.g., a `metrics-banned-terms.json` file that `scan.mjs` imports alongside the existing list).

---

*This document is version 1.0. Changes require a PR with a comment explaining why the scan rule is being modified, reviewed by the coordinator and at least one of Dr. Yael, Dr. Nadia, or Dr. Rami.*
