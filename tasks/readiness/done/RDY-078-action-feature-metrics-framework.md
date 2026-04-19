# RDY-078 — Action: Per-feature success metrics framework

- **Wave**: 0 (runs in parallel, blocks nothing, but unblocks everything's honest measurement)
- **Priority**: HIGH
- **Effort**: 1 engineer-week + 1 product-review week
- **Dependencies**: none
- **Source**: [panel review](../../docs/research/cena-panel-review-user-personas-2026-04-17.md) Round 4 — Dr. Rami's cross-exam, Item 1

## Problem

The user-personas synthesis proposed 12 features with zero success metrics. Dr. Rami's adversarial review flagged: "'ship F11 first' without a metric is aspirational, not measurable." Every feature task (RDY-065 through RDY-077) has metrics drafted — but they need a unified framework: definitions, instrumentation approach, statistical-significance thresholds, cohort-sampling rules.

## Scope

**Deliverable**: `docs/engineering/feature-success-metrics.md` — a durable metrics contract covering:

1. **Metric taxonomy**
   - Engagement metrics (retention, completion rate) — watch for Goodhart
   - Learning metrics (mastery gain, grade delta) — the real signal
   - Trust metrics (parent NPS, unsubscribe rate)
   - Safety metrics (ship-gate violations, abuse reports)

2. **Instrumentation standards**
   - Event schema for cross-feature comparability
   - Cohort tagging (control vs treatment)
   - Privacy bounds (session-scoped per ADR-0003)

3. **Statistical rules**
   - Minimum sample size before calling a signal
   - Significance thresholds (p < 0.05 OR effect size > X)
   - Guardrails against p-hacking and multiple testing

4. **Pre-registered hypotheses**
   - Before any feature ships, team writes down: "we expect this metric to move in this direction by at least X within Y weeks. If not, we revisit."

5. **Prohibited metrics** (anti-dark-pattern)
   - Streak count, variable-ratio engagement, time-in-app maximization — all banned as success metrics

## Files to Create / Modify

- `docs/engineering/feature-success-metrics.md` — the framework
- `docs/engineering/metrics-pre-registration-template.md` — per-feature hypothesis form
- `src/shared/Cena.Infrastructure/Telemetry/MetricTaxonomy.cs` — typed events
- `ops/grafana/cena-feature-metrics-overview.json` — cross-feature dashboard

## Acceptance Criteria

- [ ] Metrics framework doc reviewed + approved by Dr. Yael (statistical rigor), Dr. Nadia (learning-science relevance), Dr. Rami (adversarial honesty)
- [ ] Each of RDY-065 through RDY-077 updated to cite the framework for its metrics
- [ ] Pre-registration template adopted for next feature shipped
- [ ] Grafana overview dashboard deployed
- [ ] Prohibited metrics enforced via CI (no "streak" dashboards will merge)

## Success Metrics

- **Feature metrics compliance rate**: target 100% of new features pre-register hypotheses
- **Post-ship honesty**: when a feature misses its pre-registered target, team publicly reports it (intra-team), not silently drops

## ADR Alignment

- GD-004: prohibited metrics enforced
- ADR-0003: all metrics session-scoped or aggregate-only; no student-identifiable analytics

## Out of Scope

- Production analytics infrastructure upgrade (separate task)
- External benchmark comparisons (publish internal first)

## Assignee

Unassigned; Dr. Yael + Dr. Rami co-lead; product+eng leads implement.
