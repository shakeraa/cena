# Retired Proposals — Pre-Release Review 2026-04-20

Companion to `SYNTHESIS.md`. Any feature proposal listed here is not to be implemented as a greenfield — either (A) the code already ships the substrate and only a delta applies, or (B) the proposal violates a non-negotiable / rests on rejected evidence / has been downgraded on consensus.

---

## A. Already-Built — retire greenfield framing, scope to delta only

### R-01: Adaptive interleaving scheduler (axis1 F1, FD-001)
- **Doc line**: axis1_pedagogy_mechanics_cena.md:L40-L66
- **Already built at**: src/actors/Cena.Actors/Mastery/AdaptiveScheduler.cs; Tutoring/TutorActor.cs; src/mobile/lib/core/services/adaptive_interleaving.dart
- **Flagged by**: persona-educator, persona-cogsci, persona-finops
- **Delta task(s)**: prr-065 (strategy-discrimination scores, session-scoped), prr-148 (student-input UI), prr-149 (live caller), prr-150 (mentor override aggregate)
- **Related**: prr-151 (Group-A live-caller audit across R-03/R-08/R-09/R-13/R-15/R-22)
- **Action**: retire greenfield proposal.

### R-02: Self-explanation / elaborative interrogation (axis1 F4, axis2 F1, FD-009)
- **Already built at**: src/actors/Cena.Actors/Pedagogy/ExplainItBack.cs (RDY-074)
- **Flagged by**: persona-educator, persona-finops, persona-cogsci
- **Delta task(s)**: prr-012 (Socratic rate-limit + cache)
- **Action**: retire greenfield proposal.

### R-03: Scaffolding / worked-example support (axis1 F8)
- **Already built at**: Cena.Actors/Mastery/ScaffoldingService.cs:43; ScaffoldingLevel.cs
- **Flagged by**: persona-educator
- **Delta task(s)**: prr-041 (BKT + fading policy ADR)
- **Action**: retire greenfield proposal.

### R-04: HLR / spacing calculator (axis1 F2)
- **Already built at**: Cena.Actors/Mastery/HlrCalculator.cs
- **Flagged by**: persona-cogsci
- **Action**: retire.

### R-05: Hint ladder / stuck-type ontology (axis1 F5, axis7 F3, FD-050)
- **Already built at**: LearningSessionActor.HintRequest + ADR-0036
- **Delta task(s)**: prr-029 (LD-friendly hint governor)
- **Flagged by**: persona-cogsci, persona-educator, persona-a11y
- **Action**: retire greenfield; adopt delta only.

### R-06: CAS 3-tier router (MathNet→SymPy→fallback)
- **Already built at**: src/actors/Cena.Actors/Cas/CasRouterService.cs:29
- **Flagged by**: persona-finops, persona-cogsci, persona-educator
- **Action**: retire.

### R-07: SymPy CAS-gated problem variation engine (axis8 F2)
- **Already built at**: Cena.Actors/Cas/CasGatedQuestionPersister.cs; CasConformanceSuite.cs; SymPySidecarClient.cs; MathNetVerifier.cs
- **Delta task(s)**: prr-010 (sandbox sympy eval)
- **Flagged by**: persona-educator, persona-ministry
- **Action**: retire greenfield; adopt delta only.

### R-08: IRT + Elo + BKT calibration (axis6 F2, axis8 F1)
- **Already built at**: Services/IrtCalibrationPipeline.cs; Mastery/EloScoring.cs; Mastery/BktService.cs
- **Flagged by**: persona-educator
- **Delta task(s)**: prr-041, prr-071
- **Action**: retire.

### R-09: Real-time session-scoped misconception tagging (axis6 F6)
- **Already built at**: Services/MisconceptionDetectionService.cs; MisconceptionCatalog.cs; MlExclusionEnforcementTests.cs; RetentionWorker.cs
- **Delta task(s)**: prr-003 (hard-delete), prr-015 (retention registration)
- **Flagged by**: persona-educator, persona-cogsci
- **Action**: retire greenfield.

### R-10: Arabic RTL math renderer (axis3 F7, axis8 F4, FD-014)
- **Already built at**: src/student/full-version/src/composables/useLocaleInference.ts; tests/a11y/math-ltr-wrapper.spec.ts; tests/e2e/rtl-visual-regression.spec.ts
- **Delta task(s)**: prr-031 (MathML speech he/ar), prr-032 (Arabic math delta)
- **Flagged by**: persona-a11y, persona-educator, persona-cogsci
- **Action**: retire greenfield; adopt deltas.

### R-11: MathML/ARIA math labels (axis3 F6, FD-016)
- **Already built at**: src/shared/Cena.Infrastructure/Accessibility/MathAriaLabels.cs
- **Delta task(s)**: prr-031
- **Flagged by**: persona-a11y
- **Action**: retire greenfield.

### R-12: Prerequisite DAG / Math Concept Map (cross-domain F1)
- **Already built at**: commit 11fcaed (RDY-073 F7 compression Phase 1B)
- **Flagged by**: persona-cogsci, persona-ministry
- **Action**: retire.

### R-13: Parent digest + parental controls scaffold (axis4 F1/F3)
- **Already built at**: src/actors/Cena.Actors/ParentDigest/; ParentalControls/
- **Delta task(s)**: prr-009, prr-014, prr-018, prr-051, prr-052
- **Flagged by**: persona-educator
- **Action**: retire greenfield; adopt deltas.

### R-14: Offline sync ledger (axis10 F4)
- **Already built at**: Sessions/MartenOfflineSyncLedger.cs
- **Delta task(s)**: prr-085 (conflict UX)
- **Flagged by**: persona-educator
- **Action**: retire greenfield.

### R-15: Cultural context service (axis8 F5 foundation)
- **Already built at**: Cena.Actors/Services/CulturalContextService.cs
- **Delta task(s)**: prr-034, prr-076
- **Flagged by**: persona-educator
- **Action**: retire greenfield; adopt deltas.

### R-16: Retention worker + data retention policy
- **Already built at**: Compliance/RetentionWorker.cs; DataRetentionPolicy.cs
- **Delta task(s)**: prr-003, prr-015
- **Flagged by**: persona-educator, persona-privacy, persona-sre
- **Action**: retire greenfield; adopt deltas.

### R-17: ML-exclusion enforcement
- **Already built at**: Cena.Actors.Tests/Compliance/MlExclusionEnforcementTests.cs
- **Flagged by**: persona-educator
- **Action**: retire.

### R-18: Socratic tutor infra (competitive F1)
- **Already built at**: Tutor/ClaudeTutorLlmService.cs; TutorMessageService.cs; TutorPromptScrubber.cs
- **Delta task(s)**: prr-012, prr-045, prr-115
- **Flagged by**: persona-educator, persona-finops
- **Action**: retire greenfield; adopt deltas.

### R-19: Per-student daily LLM cost cap ($1.50/$0.70) + global breaker + L2 cache
- **Already built at**: contracts/llm/routing-config.yaml:285; src/actors/Cena.Actors/RateLimit/RedisCostCircuitBreaker.cs; tasks/student-ai/done/SAI-003
- **Delta task(s)**: prr-004 (ADR-026 promote), prr-046, prr-047
- **Flagged by**: persona-finops
- **Action**: retire greenfield; adopt deltas.

### R-20: GDPR self-service flow (axis9)
- **Already built at**: src/student/full-version/tests/e2e/privacy-gdpr-self-service.spec.ts
- **Delta task(s)**: prr-080, prr-086, prr-114
- **Flagged by**: persona-a11y
- **Action**: retire greenfield.

### R-21: TutorSafetyGuard + LaTeXSanitizer + TenantScope
- **Already built at**: src/actors/Cena.Actors/Tutoring/TutorSafetyGuard.cs; src/shared/Cena.Infrastructure/Security/LaTeXSanitizer.cs; TenantScope.cs
- **Delta task(s)**: prr-081 (tenant-scope fuzzer), prr-115 (prompt-injection canaries)
- **Flagged by**: persona-redteam, persona-privacy
- **Action**: retire greenfield.

### R-22: Accommodations bounded context (axis3 scaffold)
- **Already built at**: src/actors/Cena.Actors/Accommodations/
- **Delta task(s)**: prr-044 (scope ADR), prr-050 (dyscalculia pack)
- **Flagged by**: persona-educator, persona-enterprise
- **Action**: retire greenfield.

### R-23: Affective signal onboarding (ADR-0037)
- **Already built at**: ADR-0037 + onboarding self-assessment
- **Delta task(s)**: prr-057, prr-102
- **Flagged by**: persona-cogsci
- **Action**: retire greenfield.

### R-24: Hebrew gate + locale inference
- **Already built at**: LanguagePicker.vue:21-60; useLocaleInference.ts
- **Delta task(s)**: prr-089 (edge-case QA), prr-094 (no IP retention)
- **Flagged by**: persona-a11y
- **Action**: retire greenfield.

### R-25: Touch-target / reduced-motion / color-contrast scanners
- **Already built at**: tests/a11y/*.spec.ts
- **Delta task(s)**: prr-070, prr-078
- **Flagged by**: persona-a11y
- **Action**: retire greenfield.

---

## B. Hard Retires — non-negotiable violations / evidence-rejected / risk > benefit

### R-26: AXIS_4 F8 Bagrut Countdown Dashboard
- **Doc line**: AXIS_4_Parent_Engagement_Cena_Research.md F8
- **Violates**: non-negotiable #3 (dark-pattern), ship-gate GD-004, ADR-0003
- **Flagged by**: persona-ethics, persona-educator, persona-cogsci, persona-ministry
- **Replacement task**: prr-006 (positive progress framing)
- **Action**: retire outright.

### R-27: AXIS_6 F7 / AXIS_2 "Crisis Mode" Bagrut sprint
- **Doc line**: AXIS_6_Assessment_Feedback_Research.md:L135
- **Violates**: #3 dark-pattern, ADR-0003, loss-aversion framing
- **Flagged by**: persona-ethics, persona-educator, persona-cogsci, persona-sre, persona-ministry
- **Replacement task**: prr-006
- **Action**: retire outright.

### R-28: "At-Risk Student Alert" as dashboard/exam-risk surface
- **Doc line**: cena_competitive_analysis.md:L91, L122
- **Violates (hard, non-negotiable)**: RDY-080 (external prediction emission), ADR-0003 (persistence of per-student risk labels), ministry defensibility (externally-emitted Bagrut-outcome predictions)
- **Does NOT violate (user decision 2026-04-20)**: honest language. Harsh-reality numbers in-session are encouraged; euphemistic "needs support" / soft-label framing is the anti-pattern, not the target.
- **Flagged by**: persona-redteam, persona-ministry, persona-ethics, persona-educator, persona-cogsci, persona-sre
- **Replacement task**: prr-013 (redesign under hard constraints — honest + supportive + legal)
- **Action**: NOT a blanket retire. Redesign under three hard constraints that are not tone-negotiable:
  1. **Session-scoped only** — assessment expires at session end, never persisted to profile or aggregate, ADR-0003 preserved.
  2. **In-surface only** — visible to student + teacher during the session; never emitted to parent SMS/WhatsApp, gradebook passback, external dashboard, or any surface that loads after the session closes.
  3. **Confidence-bounded** — every harsh number must ship with its confidence interval and sample size ("40% ± 12% based on 18 problems today"). A number without its uncertainty is dishonest.
- **Tone guidance (user-directed)**: honest reality over complimentary framing. "You answered 40% of mastery-threshold problems correctly today — that's below the 5-unit trajectory" is the target voice. "Needs support" / "room to grow" / soft euphemism is banned as the anti-pattern here — it patronizes the student and damages trust when the data eventually contradicts the label.

### R-29: FD-003 "95% misconception resolution" claim
- **Doc line**: feature-discovery-2026-04-20.md FD-003; finding_assessment_dr_rami.md:L93
- **Violates**: evidence-integrity; Dr. Rami REJECTED
- **Flagged by**: persona-cogsci, persona-educator, persona-redteam, persona-privacy
- **Replacement task**: prr-027 (correct to meta-analytic figure)
- **Action**: retire claim; keep underlying misconception feedback with honest ES.

### R-30: FD-008 partial-credit grading resting on Yu-2026 citation
- **Doc line**: FD-008; finding_assessment_dr_rami.md:L132
- **Violates**: evidence-integrity; Dr. Rami REJECTED (citation fabricated)
- **Flagged by**: persona-educator, persona-cogsci, persona-redteam
- **Replacement task**: prr-028 (replace citation or retire feature)
- **Action**: retire unless replacement citation provided.

### R-31: FD-011 d=1.16 effect-size claim
- **Doc line**: FD-011; finding_assessment_dr_rami.md
- **Violates**: evidence-integrity; fabricated ES
- **Flagged by**: persona-cogsci, persona-redteam
- **Replacement task**: prr-121 (retire feature)
- **Action**: retire.

### R-32: Peer voice-explanation circles (AXIS_7)
- **Doc line**: AXIS_7_Collaboration_Social_Features_Cena.md F-voice
- **Violates**: moderation feasibility, PII via voice, safeguarding
- **Flagged by**: persona-privacy, persona-redteam, persona-ethics, persona-ministry
- **Replacement task**: prr-064 (retire)
- **Action**: retire.

### R-33: Subitizing trainer (cross-domain)
- **Doc line**: cena_cross_domain_feature_innovation.md
- **Violates**: insufficient evidence base for age-appropriateness
- **Flagged by**: persona-educator, persona-cogsci
- **Replacement task**: prr-063 (retire)
- **Action**: retire.

### R-34: "Cheating alert" family of features
- **Doc line**: cena_competitive_analysis.md
- **Violates**: #3 dark-pattern (suspicion framing), ADR-0003, educator trust
- **Flagged by**: persona-ethics, persona-educator
- **Replacement task**: prr-144 (retire; replace with teacher-initiated review)
- **Action**: retire.

### R-35: "Streak" counters + variable-ratio rewards
- **Doc line**: axis2, axis4 references
- **Violates**: #3 dark-pattern, GD-004
- **Flagged by**: persona-ethics, persona-educator, persona-a11y, persona-sre, persona-ministry
- **Replacement task**: prr-019, prr-040 (scanner coverage)
- **Action**: retire wherever proposed; scanner enforces.

### R-36: Predicted-Bagrut-score surface (any client-facing API)
- **Doc line**: multiple axis2/axis6 spots
- **Violates**: RDY-080, ministry defensibility
- **Flagged by**: persona-ministry, persona-redteam, persona-ethics, persona-educator
- **Replacement task**: prr-007, prr-092
- **Action**: retire surface (internal calibration OK, no external emission).

### R-37: Permanent "emotional state" on student profile
- **Doc line**: axis2 motivation
- **Violates**: ADR-0003, ADR-0037
- **Flagged by**: persona-ethics, persona-privacy
- **Replacement task**: prr-102
- **Action**: retire.

### R-38: Raw Ministry exam text in student-facing items
- **Doc line**: AXIS_6, AXIS_8
- **Violates**: Bagrut reference-only policy (2026-04-15)
- **Flagged by**: persona-ministry, persona-enterprise, persona-educator
- **Replacement task**: prr-008, prr-122
- **Action**: retire; all student-facing items must be CAS-gated AI recreations.

---

Total retired: 38 entries (24 already-built + 14 hard-retires; the SYNTHESIS.md headline count of 24 refers to distinct proposals; this file enumerates the full retirement map at finer grain for tracking).
