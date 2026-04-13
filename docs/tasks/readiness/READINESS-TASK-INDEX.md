# Readiness Task Index

> Generated 2026-04-13 from expert panel audit ([READINESS-PANEL-AUDIT.md](READINESS-PANEL-AUDIT.md))
> Updated 2026-04-13 with cross-persona review refinements + Rami's adversarial review

## Tier 0 — Ship-Blockers (blocks ALL deployment)

| ID | Title | Effort (panel) | Effort (Rami) | Owner Personas | Status |
|----|-------|----------------|---------------|----------------|--------|
| [RDY-001](../../tasks/readiness/RDY-001-csam-moderation-failclosed.md) | CSAM Detection + Moderation Fail-Closed | 1 week | **2-3 weeks** | Ran, Dina | Pending |
| [RDY-002](../../tasks/readiness/RDY-002-rtl-enable-regression.md) | Enable RTL + Visual Regression | 2-3 days | **5-7 days** | Tamar, Amjad, Lior | Pending |
| [RDY-003](../../tasks/readiness/RDY-003-prerequisite-graph.md) | Populate Prerequisite Graph | 1-2 weeks | **2-3 weeks** | Amjad, Nadia, Yael | Pending |
| [RDY-004](../../tasks/readiness/RDY-004-arabic-translations.md) | Arabic Translations (Top 200) | 4-6 weeks | **4-8 weeks** | Amjad | Pending |
| [RDY-032](../../tasks/readiness/RDY-032-pilot-data-export.md) | Pilot Data Export Pipeline | — | **1 week** | Dina | Pending |

> **Rami additions**: RDY-032 (pilot data export) added as Tier 0 — it's a prerequisite for RDY-007, RDY-024, and RDY-028. All effort estimates revised upward per adversarial review.

## Tier 1 — Critical (blocks launch with real students)

| ID | Title | Effort (panel) | Effort (Rami) | Owner Personas | Status |
|----|-------|----------------|---------------|----------------|--------|
| [RDY-005](../../tasks/readiness/RDY-005-legal-compliance-docs.md) | Legal Compliance Docs (expanded) | 4-8 weeks | **8-16 weeks** | Ran (legal) | Pending |
| [RDY-006](../../tasks/readiness/RDY-006-ml-exclusion-tag.md) | ML Exclusion Tag + Runtime Enforcement | 2 days | 2 days | Ran | Pending |
| [RDY-007](../../tasks/readiness/RDY-007-dif-analysis-pipeline.md) | DIF Analysis Pipeline | 2-3 weeks | **3-4 weeks** | Yael, Amjad | Pending |
| [RDY-008](../../tasks/readiness/RDY-008-aggregate-decomposition-adr.md) | Aggregate Decomposition ADR | 3 days | 3 days + workshop | Dina, Oren | Pending |
| [RDY-026](../../tasks/readiness/RDY-026-arabic-input-normalization.md) | Arabic Variable Input Normalization | 3-5 days | 3-5 days | Amjad | Pending |
| [RDY-027](../../tasks/readiness/RDY-027-glossary-validation.md) | Math/Physics Glossary Curation & Validation | 2-3 weeks | 2-3 weeks | Amjad | Pending |
| [RDY-033](../../tasks/readiness/RDY-033-error-pattern-matching.md) | Error Pattern Matching Infrastructure | — | **2-3 weeks** | Nadia | Pending |

> **Rami additions**: RDY-033 (error pattern matching) is a prerequisite for RDY-014. RDY-005 effort doubled after legal cost analysis ($15-30K). RDY-007 is data-dependent — consider splitting into implementation + post-pilot calibration.

## Tier 2 — High (blocks quality, not launch)

| ID | Title | Effort (panel) | Effort (Rami) | Owner Personas | Status |
|----|-------|----------------|---------------|----------------|--------|
| [RDY-009](../../tasks/readiness/RDY-009-openapi-swagger.md) | OpenAPI/Swagger | 2-3 days | 2-3 days | Oren | Pending |
| [RDY-010](../../tasks/readiness/RDY-010-api-versioning.md) | REST API Versioning | 3-5 days | **5-7 days** | Oren | Pending |
| [RDY-011](../../tasks/readiness/RDY-011-health-probes.md) | Health Probes Check Dependencies | 2 days | **3-4 days** | Dina | Pending |
| [RDY-012](../../tasks/readiness/RDY-012-http-circuit-breakers.md) | HTTP Client Circuit Breakers | 1 week | 1 week | Dina | Pending |
| [RDY-013](../../tasks/readiness/RDY-013-worked-examples-ui.md) | Worked Examples UI | 3-5 days | **1-2 weeks** | Nadia, Lior, Tamar | Pending |
| [RDY-014](../../tasks/readiness/RDY-014-misconception-detection-pipeline.md) | Misconception Detection Pipeline | 1-2 weeks | **2-3 weeks** | Nadia | Pending |
| [RDY-015](../../tasks/readiness/RDY-015-a11y-sweep.md) | A11y Sweep (2 sprints) | 3d + 1w | **2-3 weeks** | Tamar | Pending |
| [RDY-016](../../tasks/readiness/RDY-016-celebration-flow-state.md) | Celebration + Flow State UX | 1 week | **2 weeks** | Lior | Pending |
| [RDY-017](../../tasks/readiness/RDY-017-nats-dlq-tls.md) | NATS DLQ Stream + TLS | 1 week | 1 week | Dina | Pending |
| [RDY-018](../../tasks/readiness/RDY-018-sympson-hetter-exposure.md) | Sympson-Hetter Full Implementation | 1-2 weeks | **2-3 weeks** | Yael | Pending |
| [RDY-028](../../tasks/readiness/RDY-028-bagrut-calibration-baseline.md) | Bagrut Calibration Baseline | 2-3 weeks | 2-3 weeks | Amjad, Yael | Pending |
| [RDY-029](../../tasks/readiness/RDY-029-security-hardening.md) | Security Hardening Bundle (12 sub-tasks) | 3-4 weeks | 3-4 weeks | Ran, Dina | Pending |
| [RDY-030](../../tasks/readiness/RDY-030-a11y-test-automation.md) | A11y Test Automation | 3-5 days | 3-5 days | Tamar | Pending |
| [RDY-034](../../tasks/readiness/RDY-034-flow-state-backend-api.md) | Flow State Backend API | — | **1-2 weeks** | Lior, Dina | Pending |

> **Rami additions**: RDY-034 (flow state backend API) is a prerequisite for RDY-016. Multiple effort estimates revised upward.

## Tier 3 — Medium (polish, calibration, DX)

| ID | Title | Effort (panel) | Effort (Rami) | Owner Personas | Status |
|----|-------|----------------|---------------|----------------|--------|
| [RDY-019](../../tasks/readiness/RDY-019-bagrut-corpus-ingestion.md) | Bagrut Corpus Ingestion + Taxonomy | 3-4 weeks | **4-6 weeks** | Amjad | Pending |
| [RDY-020](../../tasks/readiness/RDY-020-signalr-event-bridge.md) | SignalR Event Push-Back Bridge | 3-5 days | **1 week** | Oren | Pending |
| [RDY-021](../../tasks/readiness/RDY-021-projection-idempotence.md) | Projection Idempotence Tests | 2-3 days | **3-5 days** | Oren, Dina | Pending |
| [RDY-022](../../tasks/readiness/RDY-022-session-timer-fatigue.md) | Session Timer + Fatigue UI | 3 days | 3 days | Lior, Nadia | Pending |
| [RDY-023](../../tasks/readiness/RDY-023-diagnostic-onboarding.md) | Diagnostic Quiz (IRT theta init) | 1 week | 1 week | Nadia, Lior | Pending |
| [RDY-024](../../tasks/readiness/RDY-024-bkt-calibration.md) | BKT Parameter Calibration (2 phases) | 2-3 weeks | **3-4 weeks** | Nadia | Pending |
| [RDY-025](../../tasks/readiness/RDY-025-deployment-manifests.md) | Deployment Manifests (K8s/Docker) | 1-2 weeks | **3-4 weeks** | Dina | Pending |
| [RDY-031](../../tasks/readiness/RDY-031-task-dependency-graph.md) | Task Dependency Graph | 1 day | 1 day | Nadia | Pending |

## Summary

- **Total tasks**: 34 (was 25 → 31 from cross-review → 34 from Rami)
- **Tier 0 (ship-blockers)**: 5 tasks (+RDY-032 pilot data export)
- **Tier 1 (critical)**: 7 tasks (+RDY-033 error pattern matching)
- **Tier 2 (high)**: 14 tasks (+RDY-034 flow state backend API)
- **Tier 3 (medium)**: 8 tasks

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
