# Cena Pre-Release Review Backlog

Tasks synthesized from the 10-persona pre-release review dated 2026-04-20. See [`/pre-release-review/reviews/SYNTHESIS.md`](../../pre-release-review/reviews/SYNTHESIS.md) for full synthesis, [`retired.md`](../../pre-release-review/reviews/retired.md) for killed proposals, [`conflicts.md`](../../pre-release-review/reviews/conflicts.md) for cross-persona disagreements needing human decision.

**Usage note**: Every task file duplicates the senior-architect Implementation Protocol section for skimming convenience. Do not auto-enqueue. Each task must be reviewed and explicitly added to `.agentdb/kimi-queue.db` via `node .agentdb/kimi-queue.js add` before a worker can claim it.

## Tier Structure

| Tier | Count | Location | Meaning |
|---|---|---|---|
| **Pre-launch (MVP)** | 63 tasks + 4 epics | `./` (this folder) | P0 ship-blockers + P1s that must ship before launch + epic-level bundles |
| **Post-launch** | 98 tasks | `./post-launch/` | P2 polish + P1 UX enhancements that can ship after launch |
| **Descoped** | 25 tasks | `./descoped/` | Nice-to-haves with no downstream dependency; not scheduled |
| **Superseded** | 1 task | `./superseded/` | Tasks marked done but superseded by a later ADR + replacement task set |

Total: 186 canonical tasks + 4 epics = 190 entries. Descope rule preserved: nothing from privacy / redteam / ministry lenses was descoped without a covering task; nothing referencing non-negotiables 1-9 was descoped.

### Supersede trail

| Superseded | By | Reason |
|---|---|---|
| [PRR-148](superseded/TASK-PRR-148-student-input-ui-for-adaptivescheduler-deadline-weekly-ti.md) | [PRR-217](TASK-PRR-217-adr-0049-multi-target-exam-plan.md) + [PRR-218](TASK-PRR-218-studentplan-aggregate-events.md) + [PRR-219](TASK-PRR-219-migration-safety-net.md) + [PRR-234](TASK-PRR-234-close-out-prr-148-superseded.md) (EPIC-PRR-F) | ADR-0050 multi-target exam plan replaces single-target `StudentPlanConfig`. |

## Implementation Protocol — Senior Architect

Implementation of every task (and every epic) must be driven by a senior-architect mindset, not a checklist. Before writing any code, the implementer (human or agent) must answer both sets of questions in writing — either in a task-comment, the PR description, or a `docs/decisions/` note.

### Ask why

- **Why does this task exist?** Read the source-doc lines cited in the task body and the persona reviews in `/pre-release-review/reviews/persona-*/` that raised it. If you cannot restate the motivation in one sentence, do not start coding.
- **Why this priority?** Read the lens-consensus list. Understand which persona lens raised it and what evidence they cited.
- **Why these files?** Trace the data flow end-to-end. Verify the files listed are the right seams. A bad seam invalidates the whole task.
- **Why are the non-negotiables above relevant?** Show understanding of how each constrains the solution, not just that they exist.

### Ask how

- **How does this interact with existing aggregates and bounded contexts?** Name them.
- **How does it respect tenant isolation (ADR-0001), event sourcing, the CAS oracle (ADR-0002), and session-scoped misconception data (ADR-0003)?**
- **How will it fail?** What is the runbook at 03:00 on a Bagrut exam morning? If you cannot describe the failure mode, the design is incomplete.
- **How will it be verified end-to-end, with real data?** Not mocks. Query the DB, hit the APIs, compare field names and tenant scoping.
- **How does it honor the <500 LOC per file rule, the no-stubs-in-prod rule, and the full `Cena.Actors.sln` build gate?**

### Before committing

- Full `Cena.Actors.sln` must build cleanly (branch-only builds miss cross-project errors — learned 2026-04-13).
- Tests cover golden path **and** edge cases surfaced in the persona reviews.
- No cosmetic patches over root causes. No 'Phase 1 stub → Phase 1b real' pattern (banned 2026-04-11).
- No dark-pattern copy (ship-gate scanner must pass).

### If blocked

- Fail loudly: `node .agentdb/kimi-queue.js fail <task-id> --worker <you> --reason "<specific blocker>"`.
- Do not silently reduce scope. Do not skip a non-negotiable. Do not bypass a hook with `--no-verify`.

## Epic Bundles (pre-launch)

Five architectural epics bundle sub-tasks that share a substrate and must ship in lock-step:

- [EPIC-PRR-A — ADR-0012 StudentActor decomposition](./EPIC-PRR-A-studentactor-decomposition.md)
- [EPIC-PRR-B — ADR-026 3-tier LLM routing governance](./EPIC-PRR-B-llm-routing-governance.md)
- [EPIC-PRR-C — Parent Aggregate + age-band consent + IDOR enforcement](./EPIC-PRR-C-parent-aggregate-consent.md)
- [EPIC-PRR-D — Ship-gate scanner v2 (banned vocabulary expansion)](./EPIC-PRR-D-shipgate-scanner-v2.md)
- [EPIC-PRR-E — Question-engine UX integration (parametric coverage + hint ladder + step-solver + sidekick)](./EPIC-PRR-E-question-engine-ux-integration.md)

Each epic file lists its absorbed sub-tasks and suggested execution order. Absorbed sub-tasks remain as individual task files — the epic provides coordination.

## Pre-launch Working Set — Standalone Tasks

Tasks NOT rolled up into an epic that must still ship pre-launch. Sorted by priority then ID.

### P0 (ship-blockers) — 6 standalone

| ID | Title | Pri | Lens | Assignee | Epic |
|---|---|---|---|---|---|
| [PRR-001](./TASK-PRR-001-fix-exif-stripping-bug-stop-lying-in-photouploadresponse.md) | Fix EXIF stripping bug — stop lying in PhotoUploadResponse | P0 | 2 | kimi-coder | — |
| [PRR-003](./TASK-PRR-003-hard-delete-misconception-events-on-erasure-not-read-filter.md) | Hard-delete misconception events on erasure (not read-filter) | P0 | 2 | kimi-coder | — |
| [PRR-007](./TASK-PRR-007-irt-theta-architecturally-isolated-from-student-visible-dtos.md) | IRT theta architecturally isolated from student-visible DTOs | P0 | 4 | claude-subagent-theta-isolation | — |
| [PRR-008](./TASK-PRR-008-lock-recreated-items-only-policy-in-exam-simulation-code-pat.md) | Lock 'recreated items only' policy in exam-simulation code path | P0 | 3 | claude-subagent-bagrut-fidelity | — |
| [PRR-010](./TASK-PRR-010-sandbox-sympy-template-evaluation-in-problem-variation-engin.md) | Sandbox SymPy template evaluation in problem-variation engine | P0 | 2 | kimi-coder | — |
| [PRR-011](./TASK-PRR-011-session-jwt-in-httponly-samesite-strict-cookie-not-localstor.md) | Session JWT in httpOnly SameSite=Strict cookie (not localStorage) | P0 | 1 | kimi-coder | — |

### P1 (pre-launch required) — 28 standalone

| ID | Title | Pri | Lens | Assignee | Epic |
|---|---|---|---|---|---|
| [PRR-015](./TASK-PRR-015-register-every-new-misconception-pii-store-with-retentionwor.md) | Register every new misconception/PII store with RetentionWorker pre-release | P1 | 3 | kimi-coder | — |
| [PRR-016](./TASK-PRR-016-publish-exam-day-slo-change-freeze-window-in-cd.md) | Publish exam-day SLO + change-freeze window in CD | P1 | 1 | human-architect | — |
| [PRR-017](./TASK-PRR-017-store-mashov-credentials-in-secret-manager-rotation-runbook.md) | Store Mashov credentials in secret manager + rotation runbook | P1 | 3 | kimi-coder | — |
| [PRR-020](./TASK-PRR-020-redis-session-store-health-eviction-alerts-for-misconception.md) | Redis session-store health + eviction alerts for misconception scope | P1 | 1 | kimi-coder | — |
| [PRR-021](./TASK-PRR-021-harden-csv-bulk-roster-import-size-injection-utf-8-normaliza.md) | Harden CSV bulk roster import (size, injection, UTF-8, normalization) | P1 | 3 | kimi-coder | — |
| [PRR-023](./TASK-PRR-023-saga-process-manager-pattern-for-cross-student-collaboration.md) | Saga/process-manager pattern for cross-student collaboration flows | P1 | 2 | claude-subagent-collab-saga | — |
| [PRR-024](./TASK-PRR-024-external-integration-adapter-pattern-adr.md) | External-integration adapter pattern ADR | P1 | 2 | human-architect | — |
| [PRR-025](./TASK-PRR-025-cas-gate-or-teacher-moderate-peer-math-explanations-before-d.md) | CAS-gate or teacher-moderate peer math explanations before delivery | P1 | 4 | claude-subagent-pedagogy | — |
| [PRR-026](./TASK-PRR-026-k-anonymity-floor-k-10-for-classroom-teacher-aggregates.md) | k-anonymity floor (k≥10) for classroom/teacher aggregates | P1 | 5 | kimi-coder | — |
| [PRR-029](./TASK-PRR-029-ld-anxious-friendly-hint-governor-l1-hint-always-show-soluti.md) | LD/anxious-friendly hint-governor: L1 hint always, show-solution escape | P1 | 3 | kimi-coder | — |
| [PRR-030](./TASK-PRR-030-raise-desirable-difficulty-default-to-75-for-il-bagrut-cohor.md) | Raise desirable-difficulty default to 75% for IL Bagrut cohort + ADR | P1 | 3 | human-architect | — |
| [PRR-031](./TASK-PRR-031-localize-matharialabels-cs-for-he-ar-speech-rules-katex-math.md) | Localize MathAriaLabels.cs for he/ar speech rules + KaTeX→MathML gap | P1 | 2 | kimi-coder | — |
| [PRR-032](./TASK-PRR-032-ship-arabic-rtl-math-delta-notation-profile-numerals-toggle.md) | Ship Arabic RTL math delta (notation profile, numerals toggle, function names) | P1 | 3 | kimi-coder | — |
| [PRR-033](./TASK-PRR-033-ministry-bagrut-rubric-dsl-version-pinning-per-track-sign-of.md) | Ministry Bagrut rubric DSL + version pinning + per-track sign-off | P1 | 2 | claude-subagent-bagrut-fidelity | — |
| [PRR-034](./TASK-PRR-034-cultural-context-community-review-board-ops-queue-with-dlq-s.md) | Cultural-context community review board — ops queue with DLQ + SLA | P1 | 4 | human-architect | — |
| [PRR-035](./TASK-PRR-035-sub-processor-registry-dpas-sso-mashov-classroom-twilio-anth.md) | Sub-processor registry + DPAs (SSO, Mashov, Classroom, Twilio, Anthropic) | P1 | 2 | human-architect | — |
| [PRR-036](./TASK-PRR-036-reflective-text-pii-scrub-before-persistence-llm-cross-axis.md) | Reflective-text PII scrub before persistence + LLM (cross-axis) | P1 | 2 | kimi-coder | — |
| [PRR-037](./TASK-PRR-037-grade-passback-policy-adr-teacher-opt-in-veto-whitelist.md) | Grade-passback policy ADR + teacher opt-in veto + whitelist | P1 | 4 | human-architect | — |
| [PRR-039](./TASK-PRR-039-mashov-sync-circuit-breaker-synthetic-probe-staleness-badge.md) | Mashov sync circuit-breaker + synthetic probe + staleness badge | P1 | 1 | kimi-coder | — |
| [PRR-043](./TASK-PRR-043-adr-companion-bot-therapy-scope-boundary.md) | ADR: Companion-bot / therapy-scope boundary | P1 | 2 | human-architect | — |
| [PRR-045](./TASK-PRR-045-tutorpromptscrubber-audit-pii-lint-in-socratic-pipeline.md) | TutorPromptScrubber audit + PII lint in Socratic pipeline | P1 | 2 | kimi-coder | — |
| [PRR-049](./TASK-PRR-049-teacher-analytics-replace-vanity-metrics-with-actionable-das.md) | Teacher analytics: replace vanity metrics with actionable dashboards | P1 | 2 | kimi-coder | — |
| [PRR-050](./TASK-PRR-050-dyscalculia-accommodations-number-line-strip-extended-time-f.md) | Dyscalculia accommodations: number-line strip, extended time, finger-counting allowed | P1 | 2 | kimi-coder | — |
| [PRR-053](./TASK-PRR-053-exam-day-capacity-plan-bagrut-traffic-forecast.md) | Exam-day capacity plan + Bagrut traffic forecast | P1 | 2 | human-architect | — |
| [PRR-152](./TASK-PRR-152-add-new-per-student-projections-to-erasure-cascade.md) | Add new per-student projections to erasure cascade | P1 | 1 | claude-subagent-privacy | — |
| [PRR-154](./TASK-PRR-154-build-if-then-implementation-intentions-planner-f2-with-prev.md) | Build if-then implementation-intentions planner (F2) with preview reminders | P1 | 1 | claude-subagent-pedagogy | — |
| [PRR-158](./TASK-PRR-158-offline-cache-encryption-wipe-on-logout.md) | Offline cache encryption + wipe-on-logout | P1 | 1 | claude-subagent-security | — |
| [PRR-159](./TASK-PRR-159-ship-f5-im-confused-too-anonymous-signal.md) | Ship F5 'I'm Confused Too' anonymous signal | P1 | 1 | claude-subagent-pedagogy | — |

## Post-launch (98 tasks)

Parked until after launch. See [`./post-launch/`](./post-launch/) — each file carries `**Tier**: post-launch` in its frontmatter.

<details><summary>Show 98 post-launch tasks</summary>

| ID | Title | Pri | Lens | Assignee | Epic |
|---|---|---|---|---|---|
| [PRR-153](./post-launch/TASK-PRR-153-ban-reward-inflation-emoji-in-learning-ui.md) | Ban reward-inflation emoji (🔥, ⚡) in learning UI | P1 | 1 | claude-subagent-policy | D |
| [PRR-054](./post-launch/TASK-PRR-054-saga-timeout-compensation-for-peer-collab-sessions.md) | Saga timeout + compensation for peer collab sessions | P2 | 2 | claude-subagent-collab-saga | — |
| [PRR-055](./post-launch/TASK-PRR-055-ethics-review-checklist-template-for-new-features.md) | Ethics review checklist template for new features | P2 | 1 | human-architect | — |
| [PRR-056](./post-launch/TASK-PRR-056-ugc-moderation-queue-dlq-sla.md) | UGC moderation queue DLQ + SLA | P2 | 2 | kimi-coder | — |
| [PRR-058](./post-launch/TASK-PRR-058-teacher-view-accommodations-badge-read-only.md) | Teacher view: accommodations badge (read-only) | P2 | 2 | kimi-coder | — |
| [PRR-059](./post-launch/TASK-PRR-059-adr-minimum-anonymity-k-10-as-platform-primitive.md) | ADR: Minimum-anonymity (k≥10) as platform primitive | P2 | 2 | human-architect | — |
| [PRR-060](./post-launch/TASK-PRR-060-mother-tongue-hints-cultural-review-sign-off-pipeline.md) | Mother-tongue hints: cultural-review sign-off pipeline | P2 | 2 | claude-subagent-pedagogy | — |
| [PRR-061](./post-launch/TASK-PRR-061-secret-scanner-in-pre-commit-hook.md) | Secret scanner in pre-commit hook | P2 | 2 | kimi-coder | — |
| [PRR-062](./post-launch/TASK-PRR-062-admin-audit-log-for-privileged-actions-roster-change-grade-o.md) | Admin audit-log for privileged actions (roster change, grade override) | P2 | 2 | kimi-coder | — |
| [PRR-064](./post-launch/TASK-PRR-064-retire-peer-voice-explanation-circles-privacy-moderation-ris.md) | Retire peer voice-explanation circles (privacy/ moderation risk) | P2 | 2 | claude-subagent-doc-remediation | — |
| [PRR-065](./post-launch/TASK-PRR-065-strategy-discrimination-scores-in-adaptivescheduler-session.md) | Strategy-discrimination scores in AdaptiveScheduler (session-scoped) | P2 | 2 | claude-subagent-pedagogy | A |
| [PRR-066](./post-launch/TASK-PRR-066-retrieval-prompt-reframe-formulas-application-triggers.md) | Retrieval prompt reframe: formulas → application triggers | P2 | 1 | kimi-coder | — |
| [PRR-067](./post-launch/TASK-PRR-067-self-regulation-pomodoro-break-nudges-without-loss-aversion.md) | Self-regulation: pomodoro/break nudges without loss aversion | P2 | 2 | kimi-coder | — |
| [PRR-068](./post-launch/TASK-PRR-068-goal-setting-ui-smart-frame-no-gamified-metaphors.md) | Goal-setting UI: SMART frame, no gamified metaphors | P2 | 2 | kimi-coder | — |
| [PRR-069](./post-launch/TASK-PRR-069-screen-reader-support-for-teacher-dashboard.md) | Screen-reader support for teacher dashboard | P2 | 2 | kimi-coder | — |
| [PRR-070](./post-launch/TASK-PRR-070-reduced-motion-toggle-respected-across-animations.md) | Reduced-motion toggle respected across animations | P2 | 1 | kimi-coder | — |
| [PRR-071](./post-launch/TASK-PRR-071-item-difficulty-drift-detector-irt.md) | Item-difficulty drift detector (IRT) | P2 | 2 | claude-subagent-pedagogy | — |
| [PRR-072](./post-launch/TASK-PRR-072-item-bank-coverage-matrix-vs-bagrut-syllabus.md) | Item bank coverage matrix vs Bagrut syllabus | P2 | 2 | claude-subagent-bagrut-fidelity | — |
| [PRR-073](./post-launch/TASK-PRR-073-assistant-messaging-no-therapeutic-claims.md) | Assistant messaging: no therapeutic claims | P2 | 1 | kimi-coder | D |
| [PRR-074](./post-launch/TASK-PRR-074-crisis-keyword-handoff-path-school-counselor-escalation.md) | Crisis keyword handoff path + school counselor escalation | P2 | 2 | human-architect | — |
| [PRR-075](./post-launch/TASK-PRR-075-dp-analytics-epsilon-budget-per-teacher-dashboard.md) | DP analytics epsilon budget per teacher dashboard | P2 | 1 | kimi-coder | — |
| [PRR-076](./post-launch/TASK-PRR-076-reviewer-roster-onboarding-cultural-context-board.md) | Reviewer roster onboarding: cultural-context board | P2 | 2 | human-architect | — |
| [PRR-077](./post-launch/TASK-PRR-077-bagrut-track-sign-off-ui-in-admin-dashboard.md) | Bagrut-track sign-off UI in admin dashboard | P2 | 2 | kimi-coder | — |
| [PRR-078](./post-launch/TASK-PRR-078-admin-dashboard-a11y-scan-coverage.md) | Admin-dashboard A11y scan coverage | P2 | 1 | kimi-coder | — |
| [PRR-079](./post-launch/TASK-PRR-079-retention-policy-doc-published-for-parents.md) | Retention-policy doc published for parents | P2 | 2 | human-architect | — |
| [PRR-080](./post-launch/TASK-PRR-080-data-portability-export-student-self-service.md) | Data-portability export (student self-service) | P2 | 1 | kimi-coder | — |
| [PRR-081](./post-launch/TASK-PRR-081-tenant-scope-fuzzer-in-integration-tests.md) | Tenant-scope fuzzer in integration tests | P2 | 2 | kimi-coder | — |
| [PRR-082](./post-launch/TASK-PRR-082-webhook-signature-verification-replay-protection.md) | Webhook signature verification + replay protection | P2 | 2 | kimi-coder | — |
| [PRR-083](./post-launch/TASK-PRR-083-teacher-feedback-widget-bug-content-accommodation.md) | Teacher feedback widget (bug/content/accommodation) | P2 | 1 | kimi-coder | — |
| [PRR-084](./post-launch/TASK-PRR-084-cost-alert-llm-spend-per-institute-breach.md) | Cost alert: LLM spend per institute breach | P2 | 2 | kimi-coder | B |
| [PRR-085](./post-launch/TASK-PRR-085-offline-sync-ledger-conflict-resolution-ux.md) | Offline sync ledger conflict resolution UX | P2 | 2 | kimi-coder | — |
| [PRR-086](./post-launch/TASK-PRR-086-device-fingerprint-purge-on-account-deletion.md) | Device fingerprint purge on account deletion | P2 | 1 | kimi-coder | — |
| [PRR-087](./post-launch/TASK-PRR-087-reviewer-facing-item-qa-dashboard.md) | Reviewer-facing item QA dashboard | P2 | 2 | kimi-coder | — |
| [PRR-088](./post-launch/TASK-PRR-088-sso-login-saml-oauth2-hardening.md) | SSO login: SAML + OAuth2 hardening | P2 | 2 | kimi-coder | — |
| [PRR-089](./post-launch/TASK-PRR-089-hebrew-gate-locale-inference-qa-hardening.md) | Hebrew gate + locale inference QA hardening | P2 | 2 | kimi-coder | — |
| [PRR-090](./post-launch/TASK-PRR-090-teacher-override-workflow-for-cas-fail-items.md) | Teacher-override workflow for CAS-fail items | P2 | 2 | claude-subagent-bagrut-fidelity | — |
| [PRR-092](./post-launch/TASK-PRR-092-adr-prediction-surface-ban-as-architecture.md) | ADR: Prediction-surface ban as architecture | P2 | 2 | human-architect | — |
| [PRR-093](./post-launch/TASK-PRR-093-multi-institute-onboarding-runbook.md) | Multi-institute onboarding runbook | P2 | 1 | human-architect | — |
| [PRR-094](./post-launch/TASK-PRR-094-localeinference-privacy-no-ip-retention.md) | LocaleInference privacy: no IP retention | P2 | 1 | kimi-coder | — |
| [PRR-095](./post-launch/TASK-PRR-095-runbook-llm-vendor-outage-failover.md) | Runbook: LLM vendor outage failover | P2 | 2 | human-architect | B |
| [PRR-096](./post-launch/TASK-PRR-096-admin-ui-parental-consent-management.md) | Admin UI: parental-consent management | P2 | 2 | kimi-coder | C |
| [PRR-097](./post-launch/TASK-PRR-097-student-web-error-boundary-sentry-wiring.md) | Student-web error boundary + Sentry wiring | P2 | 1 | kimi-coder | — |
| [PRR-098](./post-launch/TASK-PRR-098-admin-roster-bulk-rollback-soft-delete.md) | Admin roster bulk rollback + soft-delete | P2 | 2 | kimi-coder | — |
| [PRR-099](./post-launch/TASK-PRR-099-locale-aware-number-date-format-for-hebrew-arabic.md) | Locale-aware number/date format for Hebrew/Arabic | P2 | 1 | kimi-coder | — |
| [PRR-100](./post-launch/TASK-PRR-100-collaboration-feature-flag-default-off-pre-launch.md) | Collaboration feature flag: default OFF pre-launch | P2 | 2 | human-architect | — |
| [PRR-101](./post-launch/TASK-PRR-101-synthetic-probe-for-exam-simulation-endpoint.md) | Synthetic probe for exam-simulation endpoint | P2 | 1 | kimi-coder | — |
| [PRR-102](./post-launch/TASK-PRR-102-retire-emotional-state-profile-fields-session-only.md) | Retire 'emotional state' profile fields (session-only) | P2 | 2 | claude-subagent-adr-authoring | A |
| [PRR-103](./post-launch/TASK-PRR-103-session-replay-tooling-for-tutor-debugging-pii-scrubbed.md) | Session replay tooling for tutor debugging (PII-scrubbed) | P2 | 2 | kimi-coder | — |
| [PRR-104](./post-launch/TASK-PRR-104-classroom-google-integration-adapter-scoped.md) | Classroom.google integration adapter (scoped) | P2 | 2 | kimi-coder | — |
| [PRR-105](./post-launch/TASK-PRR-105-tutor-turn-budget-enforcement-from-adr-0002.md) | Tutor turn-budget enforcement from ADR-0002 | P2 | 2 | kimi-coder | B |
| [PRR-106](./post-launch/TASK-PRR-106-accommodation-audit-exports-for-parents-on-request.md) | Accommodation audit exports for parents (on request) | P2 | 2 | kimi-coder | C |
| [PRR-107](./post-launch/TASK-PRR-107-content-authoring-difficulty-calibration-peer-review.md) | Content authoring: difficulty calibration peer-review | P2 | 1 | claude-subagent-pedagogy | — |
| [PRR-108](./post-launch/TASK-PRR-108-whatsapp-twilio-adapter-opt-out-enforcement.md) | WhatsApp Twilio adapter opt-out enforcement | P2 | 2 | kimi-coder | C |
| [PRR-109](./post-launch/TASK-PRR-109-admin-export-misconception-catalog-internal.md) | Admin: export misconception catalog (internal) | P2 | 1 | kimi-coder | — |
| [PRR-110](./post-launch/TASK-PRR-110-classroom-aggregate-not-enough-data-fallback-ui.md) | Classroom aggregate: 'not enough data' fallback UI | P2 | 2 | kimi-coder | — |
| [PRR-111](./post-launch/TASK-PRR-111-cross-language-spec-3-unit-bagrut-end-to-end-smoke.md) | Cross-language spec: 3-unit-Bagrut end-to-end smoke | P2 | 2 | claude-subagent-bagrut-fidelity | — |
| [PRR-112](./post-launch/TASK-PRR-112-admin-ui-cost-per-feature-per-cohort.md) | Admin UI: cost per feature per cohort | P2 | 2 | kimi-coder | B |
| [PRR-113](./post-launch/TASK-PRR-113-tenant-scoped-search-index-rebuild-tooling.md) | Tenant-scoped search index rebuild tooling | P2 | 2 | kimi-coder | — |
| [PRR-114](./post-launch/TASK-PRR-114-student-self-service-data-access-log.md) | Student self-service data-access log | P2 | 2 | kimi-coder | — |
| [PRR-115](./post-launch/TASK-PRR-115-tutor-safety-guard-red-team-prompt-injection-canaries.md) | Tutor safety guard: red-team prompt-injection canaries | P2 | 1 | kimi-coder | — |
| [PRR-116](./post-launch/TASK-PRR-116-rate-limit-admin-bulk-endpoints.md) | Rate-limit: admin bulk endpoints | P2 | 2 | kimi-coder | — |
| [PRR-117](./post-launch/TASK-PRR-117-misconception-catalog-governance-add-remove-entries.md) | Misconception catalog governance (add/remove entries) | P2 | 2 | claude-subagent-pedagogy | — |
| [PRR-118](./post-launch/TASK-PRR-118-opt-in-analytics-consent-gate-student-web.md) | Opt-in analytics consent gate (student-web) | P2 | 2 | kimi-coder | — |
| [PRR-119](./post-launch/TASK-PRR-119-admin-ui-institute-level-feature-flags.md) | Admin UI: institute-level feature flags | P2 | 1 | kimi-coder | — |
| [PRR-120](./post-launch/TASK-PRR-120-content-moderation-multi-language-profanity-filter.md) | Content moderation: multi-language profanity filter | P2 | 2 | kimi-coder | — |
| [PRR-121](./post-launch/TASK-PRR-121-retire-fd-011-d-1-16-fabricated-claim.md) | Retire FD-011 (d=1.16 fabricated claim) | P2 | 2 | claude-subagent-doc-remediation | D |
| [PRR-122](./post-launch/TASK-PRR-122-reference-only-ministry-corpus-read-path-hardening.md) | Reference-only Ministry corpus read path hardening | P2 | 2 | claude-subagent-bagrut-fidelity | — |
| [PRR-123](./post-launch/TASK-PRR-123-privacy-policy-parent-student-dual-version.md) | Privacy policy: parent + student dual-version | P2 | 2 | human-architect | C |
| [PRR-124](./post-launch/TASK-PRR-124-admin-ui-grade-passback-dry-run-preview.md) | Admin UI: grade passback dry-run preview | P2 | 2 | kimi-coder | — |
| [PRR-125](./post-launch/TASK-PRR-125-collab-whiteboard-cas-gate-equations-posted.md) | Collab whiteboard: CAS-gate equations posted | P2 | 2 | claude-subagent-pedagogy | — |
| [PRR-126](./post-launch/TASK-PRR-126-per-cohort-content-difficulty-profile-il-3-4-5-unit.md) | Per-cohort content difficulty profile (IL 3/4/5 unit) | P2 | 2 | claude-subagent-pedagogy | — |
| [PRR-127](./post-launch/TASK-PRR-127-teacher-mark-item-for-review-one-click.md) | Teacher: mark-item-for-review one-click | P2 | 1 | kimi-coder | — |
| [PRR-128](./post-launch/TASK-PRR-128-chaos-test-kill-tutor-sidecar-mid-session.md) | Chaos-test: kill tutor sidecar mid-session | P2 | 2 | kimi-coder | — |
| [PRR-129](./post-launch/TASK-PRR-129-content-authoring-sympy-verified-variant-diversity.md) | Content authoring: sympy-verified variant diversity | P2 | 1 | claude-subagent-pedagogy | — |
| [PRR-130](./post-launch/TASK-PRR-130-admin-consent-audit-export-per-student.md) | Admin: consent audit export per student | P2 | 2 | kimi-coder | C |
| [PRR-131](./post-launch/TASK-PRR-131-adr-cross-student-saga-pattern.md) | ADR: Cross-student saga pattern | P2 | 1 | human-architect | — |
| [PRR-132](./post-launch/TASK-PRR-132-privacy-dsar-fulfillment-runbook.md) | Privacy DSAR fulfillment runbook | P2 | 1 | human-architect | — |
| [PRR-133](./post-launch/TASK-PRR-133-mashov-sync-partial-failure-reconciliation.md) | Mashov sync: partial-failure reconciliation | P2 | 2 | kimi-coder | — |
| [PRR-134](./post-launch/TASK-PRR-134-student-progress-export-self-service-pdf.md) | Student progress export (self-service, PDF) | P2 | 2 | kimi-coder | — |
| [PRR-135](./post-launch/TASK-PRR-135-font-loading-optimization-for-he-ar.md) | Font loading optimization for he/ar | P2 | 2 | kimi-coder | — |
| [PRR-136](./post-launch/TASK-PRR-136-image-upload-ssrf-filetype-sniffing.md) | Image-upload SSRF + filetype sniffing | P2 | 1 | kimi-coder | — |
| [PRR-137](./post-launch/TASK-PRR-137-admin-emulator-session-replay-for-support.md) | Admin: Emulator session replay for support | P2 | 2 | kimi-coder | — |
| [PRR-138](./post-launch/TASK-PRR-138-content-hint-level-1-copy-audit-application-trigger.md) | Content: hint-level 1 copy audit (application-trigger) | P2 | 2 | kimi-coder | — |
| [PRR-139](./post-launch/TASK-PRR-139-readme-coverage-for-multi-agent-coord-protocol.md) | Readme coverage for multi-agent coord protocol | P2 | 1 | human-architect | — |
| [PRR-140](./post-launch/TASK-PRR-140-cena-companion-scope-adr-guardrail-tests.md) | Cena Companion scope ADR + guardrail tests | P2 | 2 | human-architect | — |
| [PRR-141](./post-launch/TASK-PRR-141-privacy-session-fixation-hardening.md) | Privacy: session fixation hardening | P2 | 2 | kimi-coder | — |
| [PRR-142](./post-launch/TASK-PRR-142-education-friendly-error-messages-no-blame.md) | Education-friendly error messages (no blame) | P2 | 2 | kimi-coder | D |
| [PRR-143](./post-launch/TASK-PRR-143-observability-trace-id-on-every-llm-call.md) | Observability: trace id on every LLM call | P2 | 2 | kimi-coder | B |
| [PRR-144](./post-launch/TASK-PRR-144-retire-cheating-alert-family-of-features.md) | Retire 'cheating alert' family of features | P2 | 2 | claude-subagent-doc-remediation | D |
| [PRR-145](./post-launch/TASK-PRR-145-adr-hint-generation-model-tier-selection.md) | ADR: Hint generation model-tier selection | P2 | 2 | human-architect | B |
| [PRR-150](./post-launch/TASK-PRR-150-mentor-tutor-override-aggregate-for-schedule.md) | Mentor/tutor override aggregate for schedule | P2 | 3 | human-architect | A |
| [PRR-160](./post-launch/TASK-PRR-160-add-bagrut-chapter-aligned-transfer-map-for-f7-near-to-far-t.md) | Add Bagrut-chapter-aligned transfer map for F7 near-to-far transfer | P2 | 1 | claude-subagent-policy | — |
| [PRR-161](./post-launch/TASK-PRR-161-aria-live-polite-for-f6-real-time-misconception-tags.md) | aria-live='polite' for F6 real-time misconception tags | P2 | 1 | claude-subagent-a11y | — |
| [PRR-173](./post-launch/TASK-PRR-173-pre-problem-retrieval-prompts-f3-as-session-local-low-stakes.md) | Pre-problem retrieval prompts (F3) as session-local low-stakes recall | P2 | 1 | claude-subagent-pedagogy | D |
| [PRR-180](./post-launch/TASK-PRR-180-synthesis-note-combine-dr-nadia-pass-dr-rami-warn-reject-ver.md) | Synthesis note — combine Dr. Nadia PASS + Dr. Rami WARN/REJECT verdicts | P2 | 1 | claude-subagent-security | — |
| [PRR-181](./post-launch/TASK-PRR-181-throttle-confidence-check-ins-to-1-per-5-problems-high-lever.md) | Throttle confidence check-ins to ≤1 per 5 problems, high-leverage only | P2 | 1 | claude-subagent-pedagogy | — |
| [PRR-184](./post-launch/TASK-PRR-184-translation-review-gate-for-therapy-medical-language-fd-017.md) | Translation review gate for therapy/medical language (FD-017) | P2 | 1 | claude-subagent-a11y | — |
| [PRR-186](./post-launch/TASK-PRR-186-manual-triage-partial-match-orphans-and-handoffs.md) | Manual triage — 4 partial-match orphans + 16 partial-match handoffs | P2 | 1 | human-architect | — |

</details>

## Descoped (25 tasks)

Cut from scope. See [`./descoped/`](./descoped/) and [`../../pre-release-review/reviews/descoped-log.md`](../../pre-release-review/reviews/descoped-log.md) for the rule applied per item.

<details><summary>Show 25 descoped tasks</summary>

| ID | Title | Pri | Lens | Assignee | Epic |
|---|---|---|---|---|---|
| [PRR-057](./descoped/TASK-PRR-057-onboarding-self-assessment-clarity-adr-0037.md) | Onboarding self-assessment clarity (ADR-0037) | P2 | 2 | kimi-coder | — |
| [PRR-063](./descoped/TASK-PRR-063-retire-subitizing-trainer-feature-insufficient-evidence.md) | Retire subitizing trainer feature (insufficient evidence) | P2 | 2 | claude-subagent-doc-remediation | — |
| [PRR-091](./descoped/TASK-PRR-091-student-visible-progress-framing-no-scores-as-identity.md) | Student-visible progress framing (no scores-as-identity) | P2 | 2 | kimi-coder | D |
| [PRR-146](./descoped/TASK-PRR-146-admin-feature-retirement-audit-log.md) | Admin: feature-retirement audit log | P2 | 2 | kimi-coder | — |
| [PRR-147](./descoped/TASK-PRR-147-pre-release-review-close-out-publish-tasks-jsonl-to-queue.md) | Pre-release review close-out: publish tasks.jsonl to queue | P2 | 1 | human-architect | — |
| [PRR-162](./descoped/TASK-PRR-162-author-provenance-schema-unification.md) | Author provenance schema unification | P2 | 1 | claude-code | — |
| [PRR-163](./descoped/TASK-PRR-163-cohort-context-copy-lockdown-positive-frame-only.md) | Cohort-context copy lockdown: positive-frame only | P2 | 1 | claude-subagent-policy | D |
| [PRR-164](./descoped/TASK-PRR-164-f2-breathing-opener-confirm-before-route-on-mood-tap.md) | F2 breathing opener: confirm-before-route on mood tap | P2 | 1 | claude-subagent-policy | D |
| [PRR-165](./descoped/TASK-PRR-165-f2-watermark-session-id-not-student-id.md) | F2 watermark: session-id not student-id | P2 | 1 | claude-subagent-policy | D |
| [PRR-166](./descoped/TASK-PRR-166-f3-rubric-review-how-this-scored-framing-not-lost-points.md) | F3 rubric review: 'how this scored' framing, not 'lost points' | P2 | 1 | claude-subagent-policy | D |
| [PRR-167](./descoped/TASK-PRR-167-f6-explicit-ban-on-language-proficiency-inference.md) | F6 explicit ban on language-proficiency inference | P2 | 1 | claude-subagent-policy | D |
| [PRR-168](./descoped/TASK-PRR-168-focus-ritual-mood-adjustment-copy-familiar-pattern-not-easie.md) | Focus-ritual mood-adjustment copy: 'familiar pattern' not 'easier' | P2 | 1 | claude-subagent-pedagogy | D |
| [PRR-169](./descoped/TASK-PRR-169-formalize-intrinsic-motivation-risk-map-as-design-review-too.md) | Formalize 'intrinsic motivation risk map' as design-review tool | P2 | 1 | claude-subagent-policy | D |
| [PRR-170](./descoped/TASK-PRR-170-honest-es-in-product-copy-interleaving-d-0-34-not-0-5-0-8.md) | Honest ES in product copy (Interleaving d=0.34, not 0.5-0.8) | P2 | 1 | claude-subagent-finops | D |
| [PRR-171](./descoped/TASK-PRR-171-journey-path-no-animation-no-sound-pause-friendly-copy.md) | Journey path: no animation, no sound, pause-friendly copy | P2 | 1 | claude-subagent-policy | D |
| [PRR-172](./descoped/TASK-PRR-172-misconception-tag-ui-diagnostic-offer-framing-dismissible.md) | Misconception tag UI: diagnostic-offer framing, dismissible | P2 | 1 | claude-subagent-policy | D |
| [PRR-174](./descoped/TASK-PRR-174-re-audit-55-findings-borderline-count-with-ethics-lens.md) | Re-audit 55 findings BORDERLINE count with ethics lens | P2 | 1 | claude-subagent-policy | — |
| [PRR-175](./descoped/TASK-PRR-175-re-bucket-top-10-quick-wins-by-evidence-quality-tier.md) | Re-bucket 'Top-10 Quick Wins' by evidence-quality tier | P2 | 1 | claude-subagent-pedagogy | — |
| [PRR-176](./descoped/TASK-PRR-176-retire-new-heatmap-proposal-extend-existing-projection.md) | Retire 'new heatmap' proposal — extend existing projection | P2 | 1 | claude-code | — |
| [PRR-177](./descoped/TASK-PRR-177-retire-idle-pulse-animation-in-stuck-ask-button.md) | Retire idle-pulse animation in Stuck? Ask button | P2 | 1 | claude-subagent-policy | D |
| [PRR-178](./descoped/TASK-PRR-178-session-type-menu-rename-challenge-round.md) | Session type menu: rename 'Challenge Round' | P2 | 1 | claude-subagent-policy | D |
| [PRR-179](./descoped/TASK-PRR-179-student-facing-go-gentler-today-control-for-difficulty-adjus.md) | Student-facing 'go gentler today' control for difficulty adjuster | P2 | 1 | claude-subagent-policy | D |
| [PRR-182](./descoped/TASK-PRR-182-top-10-shortlist-carry-forward-critique-annotation.md) | Top-10 Shortlist: carry-forward critique annotation | P2 | 1 | claude-subagent-policy | D |
| [PRR-183](./descoped/TASK-PRR-183-translate-nadias-ship-with-caveat-findings-into-schema-impac.md) | Translate Nadia's SHIP-with-caveat findings into schema impact list | P2 | 1 | claude-code | — |
| [PRR-185](./descoped/TASK-PRR-185-transparency-report-add-banned-mechanic-compliance-section.md) | Transparency report: add banned-mechanic compliance section | P2 | 1 | claude-subagent-policy | D |

</details>

## Cross-references

- [Full synthesis](../../pre-release-review/reviews/SYNTHESIS.md)
- [Retired proposals (38)](../../pre-release-review/reviews/retired.md)
- [Conflicts needing human decision (18)](../../pre-release-review/reviews/conflicts.md)
- [Audit archive](../../pre-release-review/reviews/audit/)
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl)
