# TASK-PRR-330: IRT difficulty calibration (replace hand-assigned Bloom levels with cohort attempt data)

**Priority**: normal
**Effort**: depends on tag (content/legal = days-to-weeks; architecture/backend = 1-3 days; frontend = 1-2 days)
**Source docs**: claude-code "what is needed to make it perfect" audit 2026-04-29 — Phase 5 honest gap list. The mock-exam runner is production-grade Phase 5; this task closes a named gap on the path from "production-grade" to "perfect".
**Assignee hint**: unassigned
**Tags**: source=exam-prep-perfect-gap-2026-04-29,epic=epic-prr-n,pedagogy
**Status**: Ready
**Tier**: post-launch-polish (see DoD for promotion criteria to launch-blocking)
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

IRT difficulty calibration (replace hand-assigned Bloom levels with cohort attempt data).

This task is one of the 18 items I (claude-code) called out in my "what is needed to make it perfect?" audit on 2026-04-29 after shipping the mock-exam runner Phase 1A through Phase 5 (BagrutPaperStructure catalog → multi-part Q's → save-and-resume → 24 unit tests → 5 e2e specs). The runner is shipped + production-grade at the contract layer; this task closes the named feature/quality/architectural gap that takes it from "production-grade" to "world-class educational product".

## Scope

Item-level scope is defined by the title. See the audit summary in the conversation transcript for the full reasoning per item, or the persona-cogsci / persona-ministry research at `pre-release-review/reviews/` for category-level rationale.

Cross-references:
- ADR-0001 (multi-institute / DDD bounded contexts) for architecture-tagged tasks
- ADR-0002 (CAS oracle) for grading-tagged tasks
- ADR-0048 (no streak / no loss-aversion) for adaptive-coaching-tagged tasks
- ADR-0050 (multi-target exam plan) for ExamTarget binding
- ADR-0059 (Bagrut reference + variants) for legal / content tasks

## DoD

Item is feature-complete + tested + documented + landed on origin/main.

For tasks tagged `legal`: Shaker + counsel sign-off on the artifact.
For tasks tagged `ops`: tabletop drill executed + post-drill log filed in IncidentResponseLog.
For tasks tagged `architecture`: arch-test (where applicable) + tests verify the invariant.

## Promotion criteria

This task is **post-launch-polish** by default. Promote to **launch-blocking** if:
- A regulator action (Ministry takedown) requires the gap closed pre-launch (PRR-320, PRR-321)
- A privacy / compliance audit requires it pre-launch (PRR-313 sibling cluster)
- A user-facing failure mode is observed in production (e.g., mobile use is dominant — PRR-331 promotes)

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<commit sha + verification ref>"`
