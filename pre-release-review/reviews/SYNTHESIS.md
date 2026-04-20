# Cena Pre-Release Review — Synthesis (11th-agent output)

Date: 2026-04-20
Synthesizer: persona-synthesizer
Inputs: 150 YAML reviews (10 personas × 15 docs) under `pre-release-review/reviews/<persona-id>/`
Source corpus: 15 feature-research docs under `pre-release-review/*.md` (total 7,411 lines)

---

## TL;DR — Ten Cross-Cutting Themes

1. **The biggest ship-blocker is the StudentActor god-aggregate (ADR-0012).** Enterprise lens flagged ≥2,969 LOC of partial classes; ≥60% of pedagogy/motivation/self-regulation proposals default-drop state onto StudentActor, which violates the 500-LOC rule and makes downstream decomposition harder every week. Split decision must precede the next 20 pedagogy tickets.
2. **ADR-026 (3-tier routing) is cited project-wide but does not exist as a file.** FinOps, enterprise, SRE, red-team all flagged it. Routing lives only in `contracts/llm/routing-config.yaml`. Without the ADR promotion + CI scanner, "honors 3-tier routing" is an unenforced norm.
3. **Misconception/retention model is partially a lie:** `StripExifMetadata` is a stub but `ExifStripped: true` is returned; the "session-misconception purge" path is read-filter rather than hard-delete. Both flagged P0 by red-team AND privacy. Both violate non-negotiable #8 (no stubs) and ADR-0003.
4. **"Crisis Mode" + Bagrut countdown appears in ≥3 docs and in every one violates the ship-gate.** Ethics, educator, ministry, cogsci all converge: rename, reframe as progress (not loss), never couple to Bagrut-score prediction, and make opt-in per family.
5. **Prediction-surface leakage (theta/readiness/at-risk) threatens Ministry defensibility.** Ministry, red-team, ethics, cogsci all converge on: IRT theta never reaches any student/teacher/parent DTO; "at-risk" reframed to engagement-risk only; Bagrut-readiness outputs clamped to ordinal categories. RDY-080 guardrails must become architecture tests, not copy rules.
6. **Fabricated / inflated effect-size citations are everywhere.** Dr. Rami's REJECTs (FD-003 95%, FD-008 Yu 2026, FD-011 d=1.16, interleaving d=0.5-0.8 vs meta-analytic 0.34, Hattie d=1.44 misuse) show up across axis docs. Cogsci + educator + privacy + redteam + sre converge on: Ship-gate rule to block features resting on REJECTED citations; honest-ES ADR.
7. **Arabic RTL math is half-built, doc re-proposes greenfield.** A11y: RTL math renderer, `<bdi dir="ltr">`, MathML-ARIA skeleton, visual regression tests all exist in student-web; AXIS-3/8 and feature-discovery treat it as a proposal. Scope the proposal to the delta only (MathML speech rules, Arabic math-function-name mapping, admin-dashboard coverage).
8. **Parent-engagement axis is structurally underspecified.** No parent auth role, no parent→child IDOR-enforcement helper, no student-visible consent for parent-visible data at 13+/16+. Enterprise, privacy, red-team, ethics, ministry all block AXIS-4 features on a Parent aggregate / age-band ADR.
9. **Cost ceiling at 10k students is at risk from LLM-heavy per-step features.** FinOps + SRE converge: Socratic self-explanation F1 (every step, every problem) routed to Sonnet = unbounded runaway; no cache-key. Extensive rework on caching (reuse SAI-003 pattern), daily-minute caps, and CI scanner rejecting silent-default-to-Sonnet tasks is required before any of the 8 SHIP features from feature-discovery land.
10. **Operational gaps for exam-day pose real reputation risk.** SRE flagged: no exam-day SLO, no change-freeze window in CD, no Mashov credential vault, no synthetic probe + staleness badge for external integrations, no DLQ/SLA for cultural-review or UGC-moderation queues. These are platform primitives, not feature work.

---

## Top-40 Tasks Table (post-dedup, priority floor-raised by consensus)

| Rank | Title | Pri | Lens consensus (≥) | Source docs | Est | Assignee hint |
|---|---|---|---|---|---|---|
| 1 | Fix EXIF stripping bug — stop lying in PhotoUploadResponse | P0 | redteam | axis9:L39 | S | kimi-coder |
| 2 | ADR-0012 StudentActor split — gate pedagogy + SRL features | P0 | enterprise | axis1:L44, axis2:L43 | L | human-architect |
| 3 | Hard-delete misconception events on erasure (not read-filter) | P0 | privacy+redteam | axis9:L176 | M | kimi-coder |
| 4 | Promote contracts/llm/routing-config.yaml governance to ADR-026 + CI scanner | P0 | finops+enterprise+sre+cogsci | feature-discovery:L25 | M | human-architect |
| 5 | Block features whose evidence Dr. Rami REJECTED (FD-003, FD-008, FD-011) | P0 | redteam+educator+cogsci+privacy+sre | finding_assessment_dr_rami:L69 | S | claude-subagent-shipgate |
| 6 | Rename "Crisis Mode" + replace Bagrut countdown with progress framing | P0 | ethics+educator+cogsci+ministry | axis6:L135, axis4:L129, competitive:L126 | M | human-architect |
| 7 | IRT theta output architecturally isolated from student-visible DTOs | P0 | ministry+redteam+ethics | axis6:L169, competitive:L122 | M | claude-subagent-theta-isolation |
| 8 | Lock "recreated items only" policy in exam-simulation code path | P0 | enterprise+ministry+educator | axis6:L73 | M | claude-subagent-bagrut-fidelity |
| 9 | Parent→child claims binding + enforcement helper (parent IDOR) | P0 | redteam+privacy+enterprise | axis4:L86, axis4:L69 | M | kimi-coder |
| 10 | Sandbox SymPy template evaluation in problem-variation engine | P0 | redteam+enterprise | axis8:L98, axis8:L92 | M | kimi-coder |
| 11 | Session JWT in httpOnly SameSite=Strict cookie (not localStorage) | P0 | redteam | axis10:L116 | S | kimi-coder |
| 12 | Cap Socratic self-explanation to 3 LLM calls/session + reuse SAI-003 cache | P0 | finops+sre | axis2:L81 | M | kimi-coder |
| 13 | Retire/redesign "At-Risk Student Alert" under ADR-0003 session-only | P0 | redteam+ministry+ethics+educator+cogsci | competitive:L91, competitive:L122 | S | claude-subagent-adr-authoring |
| 14 | ADR: Parent auth role + multi-institute visibility | P1 | enterprise+privacy+redteam | axis4:L69 | M | human-architect |
| 15 | Register every new misconception/PII store with RetentionWorker pre-release | P1 | redteam+privacy+sre | axis6:L137, feature-discovery:L56 | M | kimi-coder |
| 16 | Publish exam-day SLO + change-freeze in CD | P1 | sre | feature-discovery:L91, axis6:L72 | M | human-architect |
| 17 | Store Mashov credentials in secret manager + rotation runbook | P1 | privacy+sre+redteam | axis10:L163, axis10:L83 | M | kimi-coder |
| 18 | Outbound SMS sanitizer + rate-limit + quiet-hours policy (parent nudges) | P1 | redteam+ethics+sre+finops | axis4:L109, axis4:L64, axis4:L157 | M | kimi-coder |
| 19 | Ship-gate ban on Bagrut-score prediction + countdown copy (CI scanner expansion) | P1 | sre+ethics+ministry+educator | finding_assessment_dr_rami:L55, axis2:L81 | S | kimi-coder |
| 20 | Redis session-store health + eviction alerts for misconception scope | P1 | sre | feature-discovery:L112 | S | kimi-coder |
| 21 | Harden CSV bulk roster import (size cap, CSV injection, UTF-8, name normalization) | P1 | redteam+a11y+ethics | axis10:L135, axis10:L104 | M | kimi-coder |
| 22 | Ban PII in LLM prompts — lint rule + ADR | P1 | privacy+redteam | axis9:L253 | S | kimi-coder |
| 23 | Saga/process-manager pattern for cross-student flows (collaboration) | P1 | enterprise+redteam | axis7:L75 | L | claude-subagent-collab-saga |
| 24 | External-integration adapter pattern ADR | P1 | enterprise+sre | axis5:L71, axis10:L159 | M | human-architect |
| 25 | CAS-gate or teacher-moderate peer math explanations before delivery | P1 | ministry+redteam+a11y+educator | axis7:L114, axis7:L105 | M | claude-subagent-pedagogy |
| 26 | k-anonymity floor for classroom/teacher aggregates (k≥10 default) | P1 | privacy+redteam+ethics+sre+educator | axis5:L63, axis7:L158, axis4:L139 | S | kimi-coder |
| 27 | Correct FD-003 misconception-resolution figure, remove 95% claim | P1 | privacy+educator+cogsci+redteam | dr_rami:L93, dr_rami:L112 | S | claude-subagent-doc-remediation |
| 28 | Replace "Yu et al. 2026" citations in FD-008 — or retire partial-credit grading | P1 | educator+cogsci+redteam | dr_rami:L132 | S | claude-subagent-doc-remediation |
| 29 | LD/anxious-friendly hint-governor: L1 hint always available + show-solution escape | P1 | educator+ethics+a11y | axis1:L185, axis1:L133 | S | kimi-coder |
| 30 | Raise desirable-difficulty default to 75% for IL Bagrut cohort + ADR | P1 | educator+cogsci+ethics | axis1:L210, axis1:L205 | S | human-architect |
| 31 | Localize MathAriaLabels.cs for he/ar speech rules + KaTeX→MathML gap | P1 | a11y+educator | axis3:L99, axis3:L121 | M | kimi-coder |
| 32 | Ship Arabic RTL math delta (notation profile, numerals toggle, function names) | P1 | a11y+educator+ministry | axis3:L133, axis3:L143 | M | kimi-coder |
| 33 | Ministry Bagrut rubric DSL + version pinning + per-track sign-off | P1 | educator+ministry | axis5:L160, axis8:L132 | M | claude-subagent-bagrut-fidelity |
| 34 | Cultural-context community review board — ops queue with DLQ + SLA | P1 | educator+ministry+sre+ethics | axis8:L202, axis8:L86 | M | human-architect |
| 35 | Sub-processor registry + DPAs (SSO, Mashov, Google Classroom, Twilio, Anthropic) | P1 | privacy+sre | axis10:L137, axis9:L277 | M | human-architect |
| 36 | Reflective-text PII scrub before persistence + LLM (cross-axis) | P1 | privacy+redteam | axis2:L136, axis1:L124 | S | kimi-coder |
| 37 | Grade-passback policy ADR + teacher opt-in veto + whitelist | P1 | educator+ministry+ethics+sre | axis10:L104, axis10:L108 | M | human-architect |
| 38 | ADR: Right-to-be-forgotten in event-sourced Cena | P1 | enterprise+privacy | axis9:L70 | M | human-architect |
| 39 | Mashov sync circuit-breaker + synthetic probe + staleness badge | P1 | sre | feature-discovery:L131 | M | kimi-coder |
| 40 | Ship-gate banned-terms scanner coverage: all three locale files + admin/full-version | P1 | a11y+ethics+sre+ministry | axis2:L60, axis5:L59 | S | kimi-coder |

(Full queue, including P2s, is in `tasks.jsonl`.)

---

## Already-Built Retirement Map

Feature proposals in the doc corpus that the code already implements — retire the greenfield framing, keep only the delta.

| Proposed feature (doc) | Already built at | Source doc line | Flagged by |
|---|---|---|---|
| Adaptive interleaving scheduler (axis1 F1, FD-001) | `src/actors/Cena.Actors/Mastery/AdaptiveScheduler.cs`; `Tutoring/TutorActor.cs`; `src/mobile/lib/core/services/adaptive_interleaving.dart` | axis1:L40-L66 | educator, cogsci, finops |
| Self-explanation / elaborative interrogation (axis1 F4, axis2 F1, FD-009) | `src/actors/Cena.Actors/Pedagogy/ExplainItBack.cs` (RDY-074) | axis2:L1-L50 | educator, finops, cogsci |
| Scaffolding / worked-example support (axis1 F8) | `Cena.Actors/Mastery/ScaffoldingService.cs:43`; `ScaffoldingLevel.cs` | axis1:L441 | educator |
| HLR / spacing calculator (axis1 F2) | `Cena.Actors/Mastery/HlrCalculator.cs` | axis1 | cogsci |
| Hint ladder / stuck-type ontology (axis1 F5, axis7 F3 hints, FD-050) | `LearningSessionActor.HintRequest` + ADR-0036 | axis1:L267 | cogsci, educator, a11y |
| CAS 3-tier router (MathNet→SymPy→fallback) | `src/actors/Cena.Actors/Cas/CasRouterService.cs:29` | multiple | finops, cogsci, educator |
| SymPy CAS-gated problem variation engine (axis8 F2) | `Cena.Actors/Cas/CasGatedQuestionPersister.cs`, `CasConformanceSuite.cs`, `SymPySidecarClient.cs`, `MathNetVerifier.cs` | axis8 F2 | educator, ministry |
| IRT calibration + Elo + BKT infrastructure (axis6 F2, axis8 F1) | `Services/IrtCalibrationPipeline.cs`, `Mastery/EloScoring.cs`, `Mastery/BktService.cs` | axis6 | educator |
| Real-time session-scoped misconception tagging (axis6 F6) | `Services/MisconceptionDetectionService.cs`, `MisconceptionCatalog.cs`, `MlExclusionEnforcementTests.cs`, `RetentionWorker.cs` | axis6:L~200 | educator, cogsci |
| Arabic RTL math renderer (axis3 F7, axis8 F4, FD-014) | `src/student/full-version/src/composables/useLocaleInference.ts`; `tests/a11y/math-ltr-wrapper.spec.ts`; `tests/e2e/rtl-visual-regression.spec.ts` | axis3, axis8, feature-discovery | a11y, educator, cogsci |
| MathML/ARIA math labels (axis3 F6, FD-016) | `src/shared/Cena.Infrastructure/Accessibility/MathAriaLabels.cs` | axis3 | a11y |
| Prerequisite DAG / Math Concept Map substrate (cross-domain F1) | commit 11fcaed (RDY-073 F7 compression Phase 1B) | cross-domain:L~60 | cogsci, ministry |
| Parent digest + parental controls scaffold (axis4 F1/F3) | `src/actors/Cena.Actors/ParentDigest/`, `ParentalControls/` | axis4 | educator |
| Offline sync ledger (axis10 F4) | `Sessions/MartenOfflineSyncLedger.cs` | axis10 | educator |
| Cultural context service (axis8 F5 foundation) | `Cena.Actors/Services/CulturalContextService.cs` | axis8 | educator |
| Retention worker + data retention policy | `Compliance/RetentionWorker.cs`, `DataRetentionPolicy.cs` | axis9 | educator, privacy, sre |
| ML-exclusion enforcement | `Cena.Actors.Tests/Compliance/MlExclusionEnforcementTests.cs` | axis9 | educator |
| Socratic tutor infra (competitive F1) | `Tutor/ClaudeTutorLlmService.cs`, `TutorMessageService.cs`, `TutorPromptScrubber.cs` | competitive | educator, finops |
| Per-student daily LLM cost cap ($1.50/$0.70) | `contracts/llm/routing-config.yaml:285` | axis1 | finops |
| Global cost circuit breaker | `src/actors/Cena.Actors/RateLimit/RedisCostCircuitBreaker.cs` | axis1 | finops |
| L2 explanation cache (Redis, 30d TTL) | `tasks/student-ai/done/SAI-003-l2-explanation-cache.md` | axis1 | finops |
| GDPR self-service flow with e2e test | `src/student/full-version/tests/e2e/privacy-gdpr-self-service.spec.ts` | axis9 | a11y |
| TutorSafetyGuard + LaTeXSanitizer + TenantScope | `src/actors/Cena.Actors/Tutoring/TutorSafetyGuard.cs`, `src/shared/Cena.Infrastructure/Security/LaTeXSanitizer.cs`, `TenantScope.cs` | feature-discovery:L21 | redteam, privacy |
| Accommodations bounded context | `src/actors/Cena.Actors/Accommodations/` | axis3 | educator, enterprise |
| Affective signal onboarding (ADR-0037) | ADR-0037 + onboarding self-assessment | cross-domain, axis2 | cogsci |
| Hebrew gate + locale inference | `LanguagePicker.vue:21-60`, `useLocaleInference.ts` | axis3, axis4 | a11y |
| Touch-target / reduced-motion / color-contrast a11y scanners | `tests/a11y/*.spec.ts` | feature-discovery | a11y |

(Full list with detailed notes is in `retired.md`.)

---

## Systemic Issues (≥4 lenses agreed)

These are the cross-cutting themes where four or more personas independently converge, indicating a structural rather than a feature-level problem.

1. **Aggregate decomposition (ADR-0012) is a prerequisite, not a refactor.** Enterprise, privacy, cogsci, ethics, ministry all cite StudentActor growing past 500 LOC per partial class. Every new pedagogy/self-regulation feature compounds the problem.
2. **ADR-026 (3-tier routing) does not exist yet.** Finops, enterprise, SRE, redteam, cogsci all reference it; it is currently only a convention in `contracts/llm/routing-config.yaml`. Needs ADR + CI scanner + silent-default-to-Sonnet build-fail.
3. **Misconception/retention ship-cleanliness is broken.** Privacy, redteam, sre, cogsci, enterprise all note: read-filter erasure is not hard-delete; RetentionWorker must gate every new PII store; PII-in-LLM-prompts lint rule is missing; EXIF-strip is a no-op returning `true`.
4. **Effect-size inflation leaks from research docs into product copy.** Educator, cogsci, privacy, redteam, sre, ethics all flag FD-003 95% / FD-008 Yu 2026 / FD-011 d=1.16 / FD-001 d=0.5-0.8 / Hattie d=1.44 as either fabricated, cherry-picked, or misapplied. Needs citation-integrity ADR + ship-gate rule.
5. **Crisis Mode + Bagrut countdown is a recurring dark-pattern vector.** Ethics, educator, cogsci, ministry, sre all converge: rename, reframe, opt-in, remove from default flow.
6. **At-Risk Student framing violates ADR-0003 and ministry defensibility.** Redteam, ministry, ethics, educator, cogsci, sre converge on: session-only, teacher-facing-only, auto-expire, engagement-risk-only, never exam-risk.
7. **Parent engagement has no architectural foundation.** Enterprise, privacy, redteam, ethics, ministry all block AXIS-4 on missing Parent aggregate / age-band ADR / IDOR binding / consent purposes.
8. **Exam-day operations are unaddressed.** Sre + ministry + finops + redteam all flag: exam-week change-freeze, exam-day SLO per host, Mashov circuit-breaker + staleness badge, grade-passback idempotency, Bagrut-traffic capacity plan.

---

## Per-Doc Verdict Roll-Up (15 × 10)

Legend: A=adopt · V=revise · R=retire · M=merge-with

| Doc | edu | ent | priv | cog | a11y | red | fin | eth | min | sre |
|---|---|---|---|---|---|---|---|---|---|---|
| axis1_pedagogy_mechanics_cena | V | A | A | V | A | V | V | V | V | V |
| axis2_motivation_self_regulation_findings | V | V | A | V | A | V | V | V | V | V |
| axis3_accessibility_accommodations_findings | V | V | V | V | V | V | A | V | A | V |
| AXIS_4_Parent_Engagement_Cena_Research | V | V | V | A | V | V | V | V | V | V |
| cena_axis5_teacher_workflow_features | V | V | V | V | A | V | A | V | V | V |
| AXIS_6_Assessment_Feedback_Research | A | A | V | V | V | V | A | V | A | V |
| AXIS_7_Collaboration_Social_Features_Cena | V | V | V | V | V | V | V | V | V | V |
| AXIS_8_Content_Authoring_Quality_Research | A | A | A | V | V | V | V | V | A | V |
| axis9_data_privacy_trust_mechanics | A | A | V | A | A | A | A | A | A | A |
| AXIS_10_Operational_Integration_Features | A | V | V | A | A | V | A | V | V | A |
| cena_competitive_analysis | V | V | V | V | V | V | V | V | A | M |
| cena_cross_domain_feature_innovation | V | R | A | V | V | V | A | V | A | M |
| cena_dr_nadia_pedagogical_review_20_findings | A | V | A | A | A | A | A | A | A | A |
| finding_assessment_dr_rami | A | R | A | A | A | A | A | A | A | A |
| feature-discovery-2026-04-20 | A | A | A | A | V | V | V | V | A | V |

Observations:
- **axis7 (collaboration/social)** is the only doc where all 10 lenses chose `revise` — collaboration scope is universally under-specified.
- **axis9 (privacy)** has the most `adopt` votes, despite privacy's own `revise` — the doc's direction is right but implementation gaps (hard-delete, sub-processor DPAs) are new tasks.
- **finding_assessment_dr_rami** is near-unanimously adopted (Dr. Rami's REJECTs are well-founded); enterprise lens marks `retire` because their angle is "FD numbers are broken, but that's a doc fix not an enterprise fix."
- **cross-domain innovation** is `retire` from enterprise + `merge-with` from SRE — structural consensus that it should fold into feature-discovery.

---

## Outstanding ADR Gaps (blockers for ≥3 tasks each)

These decisions must land before their dependent tasks can safely enter the queue.

1. **ADR-026 — 3-tier model routing governance.** Currently a superprompt reference only. Blocks: task #4, #12, Socratic rate-limit, prompt-cache enforcement, cost-projection dashboard, ~12 LLM-adjacent tasks.
2. **ADR-0003 Decision 7 — misconception storage-of-record (Marten events vs Redis-only).** Conflict between `MisconceptionDetectionService` event stream and Feature 6 Redis-only pattern. Blocks: retention-worker coverage audit, read-filter→hard-delete migration, dashboard visibility reconciliation.
3. **ADR-0012 split schedule.** StudentActor decomposition into LearningSession / SelfRegulation / Accommodations aggregates. Blocks: every pedagogy/SRL/accommodations feature with state.
4. **ADR (new) — Parent auth role + age-band + multi-institute visibility.** Blocks: all AXIS-4 parent-engagement features, parent-share analytics, SMS/WhatsApp nudge purposes, student-opt-in at 16+.
5. **ADR (new) — Cross-student saga / process-manager pattern.** Blocks: peer explanations, collab whiteboard, cohort analytics, team challenges — i.e. most of axis7.
6. **ADR (new) — External-integration adapter pattern.** Blocks: Mashov, Google Classroom, SSO, webhook delivery, calendar — i.e. most of axis10.
7. **ADR (new) — Prediction-surface ban (RDY-080 as architecture).** Theta / at-risk / readiness must be internal-only. Blocks: F2 IRT-CAT, F4 at-risk, any "predicted Bagrut score" API, FD-024 level profile retention. Not a copy rule — an architecture test.
8. **ADR (new) — Crisis-mode / countdown boundary.** Positive framing only, opt-in, not default, no loss-aversion. Blocks: axis4 F8, axis6 F7, competitive F7, cross-domain F2.
9. **ADR (new) — BKT fixed-parameter policy + worked-example fading.** Hysteresis, cohort-specific difficulty targets. Blocks: axis1 F6, F8, F5 hint-governor.
10. **ADR (new) — Companion-bot / therapy-scope boundary.** Cena Companion, wellness bot, emotional states, safeguarding escalation. Blocks: cross-domain F5, ethics + redteam handoffs.
11. **ADR (new) — Cultural-context community-review board.** Ops queue, DLQ, SLA, sign-off artifacts. Blocks: axis8 F5, mother-tongue hints.
12. **ADR (new) — Citation-integrity / effect-size communication.** CI scanner rejecting marketing claims without meta-analytic mean. Blocks: ship of any feature whose source citation Dr. Rami REJECTED.
13. **ADR (new) — Event-sourced right-to-be-forgotten policy.** Hard-delete vs crypto-shred vs aggregate-rebuild. Blocks: axis9 F3 erasure, data portability export, misconception hard-delete.
14. **ADR (new) — Accommodation profile scope (student vs enrollment).** Blocks: axis3 F1 dyscalculia, AXIS-4 parent-visible accommodations, teacher-view accommodations.
15. **ADR (new) — Minimum-anonymity (k-anonymity floor) as platform primitive.** k≥10 default across class aggregates, teacher dashboards, DP analytics, anonymous-signal displays. Blocks: axis5, axis7 F5, cohort F4.

---

## Task counts summary

Raw task instances extracted across 150 YAMLs: **386**
Post-dedup (unique canonical tasks): **186**
Plus 4 architectural epics bundling sub-tasks: **190 total backlog entries**

### By priority

- **P0** (ship-blocker, security, ADR-gate): 13
- **P1** (pre-launch required): 51
- **P2** (polish / post-launch): 122

### By tier (post Option C+D consolidation, 2026-04-20)

- **Pre-launch (MVP)**: 63 tasks + 4 epics = 67 files
- **Post-launch**: 98 tasks
- **Descoped**: 25 tasks

### Other artifacts

- **Retired**: 38 proposals (see `retired.md`)
- **Conflicts requiring human decision**: 18 (see `conflicts.md`)
- **Manual-triage backlog**: 20 items in `audit/tight-match-orphans.md` + `tight-match-handoffs.md` partial-match bucket (tracked as prr-186)

**2026-04-20 follow-up consolidation**: coverage audit + tight-match pass identified 34 genuinely missed findings + 4 conversation-derived tasks (prr-148..151), now promoted as prr-148..185. 10 weak-dedup tensions added to conflicts.md as C-09..C-18. First-pass audit candidates (prr-200..299) superseded and archived.

Task file: `/Users/shaker/edu-apps/cena/pre-release-review/reviews/tasks.jsonl`
Retired-proposals file: `/Users/shaker/edu-apps/cena/pre-release-review/reviews/retired.md`
Conflicts file: `/Users/shaker/edu-apps/cena/pre-release-review/reviews/conflicts.md`
Descope log: `/Users/shaker/edu-apps/cena/pre-release-review/reviews/descoped-log.md`
