# Readiness Task Index

> Generated 2026-04-13 from expert panel audit ([READINESS-PANEL-AUDIT.md](READINESS-PANEL-AUDIT.md))
> Updated 2026-04-13 with cross-persona review refinements + Rami's adversarial review

## Tier 0 — Ship-Blockers (blocks ALL deployment)

| ID | Title | Effort (Rami) | Depends On | Status |
|----|-------|---------------|------------|--------|
| [RDY-001](../../tasks/readiness/done/RDY-001-csam-moderation-failclosed.md) | CSAM Detection + Moderation Fail-Closed | 2-3 weeks | — | **Done** |
| [RDY-002](../../tasks/readiness/done/RDY-002-rtl-enable-regression.md) | Enable RTL + Visual Regression | 5-7 days | — | **Done** |
| [RDY-003](../../tasks/readiness/done/RDY-003-prerequisite-graph.md) | Populate Prerequisite Graph | 2-3 weeks | — | **Done** |
| [RDY-004](../../tasks/readiness/RDY-004-arabic-translations.md) | Arabic Translations (Top 200) | 4-8 weeks | RDY-027 | Pending |
| [RDY-032](../../tasks/readiness/RDY-032-pilot-data-export.md) | Pilot Data Export Pipeline | 1 week | — | Pending |

> **Rami additions**: RDY-032 (pilot data export) added as Tier 0 — it's a prerequisite for RDY-007, RDY-024, and RDY-028. All effort estimates revised upward per adversarial review.

## Tier 1 — Critical (blocks launch with real students)

| ID | Title | Effort (Rami) | Depends On | Status |
|----|-------|---------------|------------|--------|
| [RDY-005](../../tasks/readiness/RDY-005-legal-compliance-docs.md) | Legal Compliance Docs (expanded) | 8-16 weeks | — | Pending |
| [RDY-006](../../tasks/readiness/done/RDY-006-ml-exclusion-tag.md) | ML Exclusion Tag + Runtime Enforcement | 2 days | — | **Done** |
| [RDY-007](../../tasks/readiness/done/RDY-007-dif-analysis-pipeline.md) | DIF Analysis Pipeline | 3-4 weeks | RDY-003 | **Done** |
| [RDY-008](../../tasks/readiness/done/RDY-008-aggregate-decomposition-adr.md) | Aggregate Decomposition ADR | 3 days | — | **Done** |
| [RDY-011](../../tasks/readiness/done/RDY-011-health-probes.md) | Health Probes Check Dependencies | 3-4 days | — | **Done** |
| [RDY-012](../../tasks/readiness/done/RDY-012-http-circuit-breakers.md) | HTTP Client Circuit Breakers | 1 week | — | **Done** |
| [RDY-026](../../tasks/readiness/RDY-026-arabic-input-normalization.md) | Arabic Variable Input Normalization | 3-5 days | — | In Progress |
| [RDY-027](../../tasks/readiness/RDY-027-glossary-validation.md) | Math/Physics Glossary Curation | 2-3 weeks | — | Pending |
| [RDY-033](../../tasks/readiness/RDY-033-error-pattern-matching.md) | Error Pattern Matching Infrastructure | 2-3 weeks | — | Pending |

> **Rami additions**: RDY-033 (error pattern matching) is a prerequisite for RDY-014. RDY-005 effort doubled after legal cost analysis ($15-30K). RDY-007 is data-dependent — consider splitting into implementation + post-pilot calibration.

## Tier 2 — High (blocks quality, not launch)

| ID | Title | Effort (Rami) | Depends On | Status |
|----|-------|---------------|------------|--------|
| [RDY-009](../../tasks/readiness/RDY-009-openapi-swagger.md) | OpenAPI/Swagger | 2-3 days | — | In Progress |
| [RDY-010](../../tasks/readiness/RDY-010-api-versioning.md) | REST API Versioning | 5-7 days | RDY-009 | Pending |
| [RDY-013](../../tasks/readiness/done/RDY-013-worked-examples-ui.md) | Worked Examples UI | 1-2 weeks | — | **Done** |
| [RDY-014](../../tasks/readiness/done/RDY-014-misconception-detection-pipeline.md) | Misconception Detection Pipeline | 2-3 weeks | RDY-006, RDY-013 | **Done** (enhanced by RDY-033) |
| [RDY-015](../../tasks/readiness/done/RDY-015-a11y-sweep.md) | A11y Sweep (2 sprints) | 2-3 weeks | RDY-002 | **Done** |
| [RDY-016](../../tasks/readiness/done/RDY-016-celebration-flow-state.md) | Celebration + Flow State UX | 2 weeks | — | **Done** (enhanced by RDY-034) |
| [RDY-017](../../tasks/readiness/done/RDY-017-nats-dlq-tls.md) | NATS DLQ Stream + TLS | 1 week | — | **Done** |
| [RDY-018](../../tasks/readiness/done/RDY-018-sympson-hetter-exposure.md) | Sympson-Hetter Full Implementation | 2-3 weeks | RDY-003 | **Done** |
| [RDY-028](../../tasks/readiness/RDY-028-bagrut-calibration-baseline.md) | Bagrut Calibration Baseline | 2-3 weeks | RDY-019, RDY-032 | Pending |
| [RDY-029](../../tasks/readiness/RDY-029-security-hardening.md) | Security Hardening Bundle (12 sub-tasks) | 3-4 weeks | — | Pending |
| [RDY-030](../../tasks/readiness/RDY-030-a11y-test-automation.md) | A11y Test Automation | 3-5 days | RDY-015 | Pending |
| [RDY-034](../../tasks/readiness/RDY-034-flow-state-backend-api.md) | Flow State Backend API | 1-2 weeks | RDY-020 | Pending |

## Tier 3 — Medium (polish, calibration, DX)

| ID | Title | Effort (Rami) | Depends On | Status |
|----|-------|---------------|------------|--------|
| [RDY-017a](../../tasks/readiness/RDY-017a-dlq-followups.md) | DLQ Follow-ups | 2-3 days | RDY-017 | Pending |
| [RDY-019](../../tasks/readiness/RDY-019-bagrut-corpus-ingestion.md) | Bagrut Corpus Ingestion + Taxonomy | 4-6 weeks | — | Pending |
| [RDY-019a](../../tasks/readiness/RDY-019a-bagrut-content-followups.md) | Bagrut Content Follow-ups | 1 week | RDY-019 | Pending |
| [RDY-020](../../tasks/readiness/done/RDY-020-signalr-event-bridge.md) | SignalR Event Push-Back Bridge | 1 week | — | **Done** |
| [RDY-021](../../tasks/readiness/done/RDY-021-projection-idempotence.md) | Projection Idempotence Tests | 3-5 days | — | **Done** |
| [RDY-022](../../tasks/readiness/done/RDY-022-session-timer-fatigue.md) | Session Timer + Fatigue UI | 3 days | — | **Done** |
| [RDY-023](../../tasks/readiness/done/RDY-023-diagnostic-onboarding.md) | Diagnostic Quiz (IRT theta init) | 1 week | — | **Done** |
| [RDY-024](../../tasks/readiness/done/RDY-024-bkt-calibration.md) | BKT Calibration Phase A | 3-4 weeks | RDY-023 | **Done** |
| [RDY-024b](../../tasks/readiness/RDY-024b-bkt-calibration-phase-b.md) | BKT Calibration Phase B | 1-2 weeks | RDY-024, RDY-032 | Pending (blocked on pilot data) |
| [RDY-025](../../tasks/readiness/RDY-025-deployment-manifests.md) | Deployment Manifests (K8s/Docker) | 3-4 weeks | — | Pending |
| [RDY-031](../../tasks/readiness/done/RDY-031-task-dependency-graph.md) | Task Dependency Graph | 1 day | — | **Done** |

## Summary

- **Total tasks**: 37 (25 original + 6 cross-review + 3 Rami + 3 sub-tasks)
- **Done**: 20 (54%) | **In Progress**: 2 | **Pending**: 15
- **Tier 0 (ship-blockers)**: 5 tasks (3 done, 2 pending)
- **Tier 1 (critical)**: 9 tasks (4 done, 1 in-progress, 4 pending)
- **Tier 2 (high)**: 12 tasks (7 done, 1 in-progress, 4 pending)
- **Tier 3 (medium)**: 11 tasks (6 done, 5 pending)
- **Dependency graph**: [READINESS-DEPENDENCY-GRAPH.md](READINESS-DEPENDENCY-GRAPH.md) | `npx tsx scripts/readiness-dependency-check.ts`

## Effort Comparison: Panel vs. Rami

| Metric | Panel Estimate | Rami's Estimate | Delta |
|--------|---------------|-----------------|-------|
| Tier 0 total | ~7 weeks | ~11 weeks | +57% |
| Tier 1 total | ~10 weeks | ~18 weeks | +80% |
| Tier 2 total (parallel) | ~10 weeks | ~14 weeks | +40% |
| Tier 3 total (parallel) | ~8 weeks | ~12 weeks | +50% |
| **Honest pilot timeline** | 6-8 weeks | **10-12 weeks** | +60% |

> **Rami's verdict**: "20 of 25 original tasks underestimate effort. The audit is 85% accurate on problem identification but HIGH-RISK on execution due to effort sandbagging, content/legal blocking factors, and vague acceptance criteria."

## Immediate Actions (This Week)

1. **Contact PhotoDNA** — confirm API availability in Israel, get test hash set (de-risks RDY-001)
2. **Contact Anthropic Legal** — ask about DPA template, timeline for signature (de-risks RDY-005)
3. **Confirm Amjad's calendar** — prerequisite graph authoring needs curriculum expertise (de-risks RDY-003)
4. **Source translators** — Hebrew-to-Arabic math specialist(s), confirm budget (de-risks RDY-004)

## Duplicate Flags

| Readiness Task | Pre-Pilot Task | Resolution |
|----------------|----------------|------------|
| RDY-001 | PP-001 (CSAM detection wire) | Clarify: is PP-001 completed? If not, merge into RDY-001. PP-001 has more detail. |
| RDY-005 | PP-002 (Legal compliance) | RDY-005 is more comprehensive. Retire PP-002. |

## Key Cross-Review Changes

| Change | Source | Rationale |
|--------|--------|-----------|
| RDY-004 → Tier 0 | Amjad | Arabic is 80% of target users — not optional |
| RDY-004 effort → 4-6 weeks | Amjad | 22-32 min/question, need 2 translators |
| RDY-015 split into 2 sprints | Tamar | Sprint 1 is legal compliance, Sprint 2 is quality |
| RDY-023 init IRT theta first | Nadia | Diagnostic should estimate theta, then derive BKT P_Initial |
| RDY-005 scope expanded | Ran | 7 missing legal documents identified |
| RDY-005 effort → 8-16 weeks | Rami | Legal process, not engineering. $15-30K counsel cost. |
| RDY-006 runtime enforcement added | Ran | Attribute alone doesn't prevent egress leaks |
| 20/25 effort estimates revised up | Rami | Adversarial review found systemic sandbagging |
| +RDY-026 Arabic input norm | Amjad | Students type Arabic chars for variables |
| +RDY-027 Glossary validation | Amjad | Prerequisite to accurate translations |
| +RDY-028 Bagrut baseline | Amjad, Yael | Anchor IRT params to real exam data |
| +RDY-029 Security bundle | Ran | 12 missing security controls |
| +RDY-030 A11y automation | Tamar | Prevent regressions after sweep |
| +RDY-031 Dependency graph | Nadia | 7+ undocumented task dependencies |
| +RDY-032 Pilot data export | Rami | Prerequisite for DIF, BKT cal, Bagrut baseline |
| +RDY-033 Error pattern matching | Rami | Prerequisite for misconception detection |
| +RDY-034 Flow state backend | Rami | Prerequisite for flow state UX |
