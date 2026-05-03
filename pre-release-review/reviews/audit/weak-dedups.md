# Audit: Weak Dedups (Tension Flattened into Consensus)

Total weak-dedups: **14**.

**Criteria**: task with `lens_consensus` >= 3 where the persona findings that informed it carry labels on opposite sides of a known tension axis (cache vs no-cache, parent-visible vs student-autonomy, CAS-gate vs teacher-moderate, hard-delete vs crypto-shred, ship-now vs refactor-first, cost-reduce vs quality-first). Matching finding <-> task is done by 3+ content-word overlap on finding text vs task title.

**Note**: this is mechanical; not every flagged tension is a real merge error. Some may already be resolved in `conflicts.md` under a different framing — cross-check before splitting.

---

### W-001: ADR-0012 StudentActor split — gate pedagogy + SRL features (prr-002)
- **Source findings**:
  - `persona-cogsci` in `axis2_motivation_self_regulation_findings.yaml:L53` (P1) [ship-now]: "F3 (Wise Feedback) frames Yeager's mistrust-bridging research as a core mechanism for Cena. The Yeager 2014 work is on *written essay feedback in a US racial context*; the transfer to Israeli Hebrew/Arabic math feedback "
  - `persona-cogsci` in `axis2_motivation_self_regulation_findings.yaml:L67` (P1) [ship-now]: "F7 (SRL Micro-Strategy Prompts) claims math-specific SRL intervention d=1.00 from Dignath 2008 (axis2:271-276). The 2008 meta-analysis found d=1.00 for *primary-school math SRL training programs* delivered over multiple "
  - `persona-cogsci` in `axis2_motivation_self_regulation_findings.yaml:L79` (P1) [cache]: "F1 (Socratic Self-Explanation Prompts) proposes LLM-validation of response "conceptual coherence" (axis2:52-54). Per ADR-0003 and ADR-0037, the student's free-text reasoning must not be allowed to feed a persistent maste"
  - `persona-cogsci` in `axis2_motivation_self_regulation_findings.yaml:L93` (P2) [ship-now]: "F4 (Mastery Path Progress) frames the goal-gradient effect (Hull 1932) as a warrant while *also* explicitly banning streaks and loss-mechanics (axis2:148). The cogsci tension: goal-gradient research shows effort increase"
  - `persona-enterprise` in `axis1_pedagogy_mechanics_cena.yaml:L42` (P0) [refactor-first]: "Doc treats "scheduler" as a new service, but StudentActor is already 764 + 1036 + 353 + 363 + 453 = 2969 LOC of partial classes (god-aggregate pattern). ADR-0012 accepts this for pilot but mandates split before productio"
  - `persona-enterprise` in `axis1_pedagogy_mechanics_cena.yaml:L49` (P1) [ship-now]: "Feature 5 (hint governor) and Feature 8 (worked-example fading) both emit new events (HintGovernorDecided_V1-ish, FadingLevelChanged_V1-ish) but doc doesn't specify upcaster strategy. The existing HintRequested_V1 in Ped"
- **Tension**: `cache` vs `no-cache`
- **Recommended split**: split `prr-002` into two tasks, one per side of the tension, and route the decision to `conflicts.md` if not already covered there.

### W-002: Promote contracts/llm/routing-config.yaml governance to ADR-026 + CI scanner (prr-004)
- **Source findings**:
  - `persona-cogsci` in `feature-discovery-2026-04-20.yaml:L74` (P1) [ship-now]: "FD-018 (Learning Energy Tracker) in this doc is framed as SHIP with per-session mood emoji. ADR-0037 (post-doc, 2026-04-19) now says affective self-signal is tie-breaker only, never cross-student/cohort use at per-studen"
  - `persona-cogsci` in `feature-discovery-2026-04-20.yaml:L85` (P1) [ship-now]: "The "3 REJECTED" list names FD-003, FD-008, FD-011 (L7). Correct. But FD-003 carries forward as SHIP in the body text of this mega-doc (L123-L144) with a "60-80% success" honest framing. That is actually fine — the REJEC"
  - `persona-cogsci` in `feature-discovery-2026-04-20.yaml:L96` (P2) [teacher-moderate]: "Doc at L15 correctly names the biggest findings: cultural localization (FD-011 with honest d=0.4-0.6), teacher-workflow integration, crisis mode, session-scoped misconception resolution. This prioritisation matches cogsc"
  - `persona-enterprise` in `feature-discovery-2026-04-20.yaml:L33` (P0) [refactor-first,ship-now]: "18 SHIP findings + 22 SHORTLIST = up to 40 potential feature tracks. Enterprise concern: without a triage gate requiring each feature to map to an existing bounded context (and name the aggregate it touches), the Student"
  - `persona-enterprise` in `feature-discovery-2026-04-20.yaml:L48` (P1) [ship-now]: "BORDERLINE tensions (FD-003 misconception tagging, etc.) are flagged but no enforcement checklist exists. Every BORDERLINE must have a "compliance design" attached before promotion to SHIP in the queue, or it becomes a s"
  - `persona-enterprise` in `feature-discovery-2026-04-20.yaml:L55` (P1) [ship-now]: "Doc uses "RDY-0XX" placeholder task IDs, indicating these aren't queue-linked yet. Enterprise coordination needs one authoritative queue entry per SHIP finding — parallel research doc task IDs drift quickly."
- **Tension**: `ship-now` vs `refactor-first`
- **Recommended split**: split `prr-004` into two tasks, one per side of the tension, and route the decision to `conflicts.md` if not already covered there.

### W-003: Rename 'Crisis Mode' + replace Bagrut countdown with positive progress framing (prr-006)
- **Source findings**:
  - `persona-cogsci` in `AXIS_4_Parent_Engagement_Cena_Research.yaml:L68` (P2) [ship-now]: "F1 ("Why This Topic" Explainability Card) and the companion axis9 feature (explainable AI) both operate on the diagnostic-result level. Cogsci view: transparency aids *trust* (meta-analytic trust literature) but the lear"
  - `persona-cogsci` in `AXIS_4_Parent_Engagement_Cena_Research.yaml:L78` (P2) [ship-now]: "F8 (Bagrut Countdown Dashboard) relies on deadline-driven urgency. Cena's non-negotiable 3 bans loss-aversion, not urgency per se, but a 180-day countdown is psychologically close enough to deserve explicit ship-gate rev"
  - `persona-cogsci` in `AXIS_6_Assessment_Feedback_Research.yaml:L42` (P0) [ship-now]: "F3 and F4 lean on Yu et al. 2026 (arXiv:2603.00895) as the primary effect-size warrant. Dr. Rami's assessment (finding_assessment_dr_rami.md:24) flags this citation as unverifiable — the year 2026 has not occurred in a w"
  - `persona-cogsci` in `AXIS_6_Assessment_Feedback_Research.yaml:L57` (P0) [ship-now]: "F6 (Real-Time Misconception Tagging) cites 95.4% misconception resolution from DeepMind/Eedi 2025. Dr. Rami (FD-003) flags this as not supported by the Eedi/DeepMind literature (actual rates ~60-80% across conditions). T"
  - `persona-cogsci` in `AXIS_6_Assessment_Feedback_Research.yaml:L81` (P1) [ship-now]: "F1 (Formative-Summative Split) is the right cogsci framing — the "expertise reversal" risk of relying on hint-heavy formative signals is real. The dashboard should frame the "gap" between signals as a hypothesis (you may"
  - `persona-cogsci` in `AXIS_6_Assessment_Feedback_Research.yaml:L90` (P1) [ship-now]: "F7 (Crisis Mode) proposes "explicit de-prioritization of deep conceptual exploration in favor of exam-relevant procedural fluency" (AXIS_6:262). Cogsci honesty: this is a pragmatic trade-off that *will* reduce far- trans"
- **Tension**: `parent-visible` vs `student-autonomy`
- **Recommended split**: split `prr-006` into two tasks, one per side of the tension, and route the decision to `conflicts.md` if not already covered there.

### W-004: Lock 'recreated items only' policy in exam-simulation code path (prr-008)
- **Source findings**:
  - `persona-educator` in `AXIS_6_Assessment_Feedback_Research.yaml:L105` (P2) [ship-now]: "Feature 2 (IRT 2PL-CAT Placement) — the "Bayesian EAP ability estimator" requires a well-calibrated item bank. Doc XL effort estimate is realistic. But the sub-requirement the doc implies but doesn't state: items need on"
  - `persona-enterprise` in `AXIS_6_Assessment_Feedback_Research.yaml:L33` (P0) [cas-gate,ship-now]: "Exam-simulation scoring proposals risk becoming a side-channel for raw Bagrut items. Non-negotiable 4 is strict: student-facing items are AI-recreated + CAS-gated. Doc must explicitly state "simulation uses recreated ite"
  - `persona-enterprise` in `AXIS_6_Assessment_Feedback_Research.yaml:L53` (P2) [refactor-first]: "Confidence calibration stores a student self-report per attempt. If stored in StudentProfileSnapshot it grows the god-aggregate (ADR-0012). Belongs on LearningSession attempt record."
  - `persona-ministry` in `axis_6_assessment_feedback_research.yaml:L47` (P0) [cache]: "Feature 6 "Real-Time Misconception Tagging (Session-Scoped)" is ADR-0003 adjacent. Dr. Rami''s FD-003 REJECT flagged the "95% misconception resolution" claim as fabricated; doc claims 60-80%. From a Ministry lens: any mi"
- **Tension**: `ship-now` vs `refactor-first`
- **Recommended split**: split `prr-008` into two tasks, one per side of the tension, and route the decision to `conflicts.md` if not already covered there.

### W-005: Parent→child claims binding + IDOR enforcement helper (prr-009)
- **Source findings**:
  - `persona-enterprise` in `AXIS_4_Parent_Engagement_Cena_Research.yaml:L30` (P0) [parent-visible]: "Parent-child multi-institute principal is an unsolved tenancy problem. ADR-0001 defines TenantScope.GetInstituteFilter for mentors/students but not for a parent whose child is enrolled in institutes the parent doesn't be"
  - `persona-enterprise` in `AXIS_4_Parent_Engagement_Cena_Research.yaml:L45` (P1) [cache,cost-reduce,quality-first]: "Auto-translation touches every outbound parent communication. This is a classic tier-routing (ADR-026) concern — every translation is an LLM call. Doc doesn't specify Tier 1 (no-op/cached) vs Tier 2 (Haiku) vs Tier 3 (So"
  - `persona-enterprise` in `AXIS_4_Parent_Engagement_Cena_Research.yaml:L52` (P2) [crypto-shred,hard-delete]: "Celebration sharing and consent mgmt both imply new events into the stream. Needs explicit event-schema versioning and retention policy (consent withdrawal is a hard delete or a superseding event? Default is superseding."
  - `persona-privacy` in `AXIS_4_Parent_Engagement_Cena_Research.yaml:L41` (P0) [student-autonomy]: "Every parent-visible feature (1, 2, 5, 6, 7, 8) assumes unilateral parent-access by virtue of parent account. For 16+ students, Israeli PPA draft opinion (Feb 2025) + GDPR-K practice emphasize student dignity — parent-sh"
  - `persona-privacy` in `AXIS_4_Parent_Engagement_Cena_Research.yaml:L55` (P0) [no-cache]: "Feature 6 (Encouragement SMS nudges) sends messages about a child's study behavior to parent phone. Twilio sub-processor (RDY-069 in-flight). Message content like "Yael hasn't studied for 3 days" is per-student behaviora"
  - `persona-privacy` in `AXIS_4_Parent_Engagement_Cena_Research.yaml:L68` (P1) [ship-now]: "Feature 4 (Privacy-Preserving Cohort Context) is the only feature explicitly addressing k-anonymity for peer-comparison. Doc gives it SHORTLIST but all other features (1, 5, 7, 8) implicitly expose cohort signals. Featur"
- **Tension**: `cache` vs `no-cache`
- **Recommended split**: split `prr-009` into two tasks, one per side of the tension, and route the decision to `conflicts.md` if not already covered there.

### W-006: ADR: Parent auth role + age-band + multi-institute visibility (prr-014)
- **Source findings**:
  - `persona-enterprise` in `AXIS_4_Parent_Engagement_Cena_Research.yaml:L30` (P0) [parent-visible]: "Parent-child multi-institute principal is an unsolved tenancy problem. ADR-0001 defines TenantScope.GetInstituteFilter for mentors/students but not for a parent whose child is enrolled in institutes the parent doesn't be"
  - `persona-enterprise` in `AXIS_4_Parent_Engagement_Cena_Research.yaml:L45` (P1) [cache,cost-reduce,quality-first]: "Auto-translation touches every outbound parent communication. This is a classic tier-routing (ADR-026) concern — every translation is an LLM call. Doc doesn't specify Tier 1 (no-op/cached) vs Tier 2 (Haiku) vs Tier 3 (So"
  - `persona-enterprise` in `AXIS_4_Parent_Engagement_Cena_Research.yaml:L52` (P2) [crypto-shred,hard-delete]: "Celebration sharing and consent mgmt both imply new events into the stream. Needs explicit event-schema versioning and retention policy (consent withdrawal is a hard delete or a superseding event? Default is superseding."
  - `persona-privacy` in `AXIS_4_Parent_Engagement_Cena_Research.yaml:L41` (P0) [student-autonomy]: "Every parent-visible feature (1, 2, 5, 6, 7, 8) assumes unilateral parent-access by virtue of parent account. For 16+ students, Israeli PPA draft opinion (Feb 2025) + GDPR-K practice emphasize student dignity — parent-sh"
  - `persona-privacy` in `AXIS_4_Parent_Engagement_Cena_Research.yaml:L55` (P0) [no-cache]: "Feature 6 (Encouragement SMS nudges) sends messages about a child's study behavior to parent phone. Twilio sub-processor (RDY-069 in-flight). Message content like "Yael hasn't studied for 3 days" is per-student behaviora"
  - `persona-privacy` in `AXIS_4_Parent_Engagement_Cena_Research.yaml:L68` (P1) [ship-now]: "Feature 4 (Privacy-Preserving Cohort Context) is the only feature explicitly addressing k-anonymity for peer-comparison. Doc gives it SHORTLIST but all other features (1, 5, 7, 8) implicitly expose cohort signals. Featur"
- **Tension**: `cache` vs `no-cache`
- **Recommended split**: split `prr-014` into two tasks, one per side of the tension, and route the decision to `conflicts.md` if not already covered there.

### W-007: Register every new misconception/PII store with RetentionWorker pre-release (prr-015)
- **Source findings**:
  - `persona-privacy` in `AXIS_6_Assessment_Feedback_Research.yaml:L44` (P0) [no-cache]: "Features 3 (AI Partial-Credit Grading) and 4 (Free-Response CAS-Validated AI Grading) send student free-text work to an LLM. This is the LARGEST LLM PII surface in the proposal set. Required before build: (a) ZDR contrac"
  - `persona-privacy` in `AXIS_6_Assessment_Feedback_Research.yaml:L58` (P0) [cache]: "Feature 6 (Real-Time Misconception Tagging) is the most ADR-0003-sensitive feature in the whole corpus. Doc proposes Redis-only with TTL + no SQL persistence — correct design. BUT the existing MisconceptionEvents.cs emit"
  - `persona-privacy` in `AXIS_6_Assessment_Feedback_Research.yaml:L105` (P2) [cache,no-cache]: "Feature 5 per-session error analysis report — by design session-scoped so privacy-clean. Just ensure Feature 5 errors don't leak into Feature 6 Redis cache in a way that extends retention."
  - `persona-privacy` in `feature-discovery-2026-04-20.yaml:L56` (P0) [ship-now]: "Quick-wins list (L19) includes Google SSO (FD-042), Auto-Translation (FD-034), Focus Ritual (FD-017) — all have undisclosed sub-processors. Before any "quick win" ships, the sub-processor inventory + DPA + privacy-page u"
  - `persona-privacy` in `feature-discovery-2026-04-20.yaml:L66` (P1) [ship-now]: "Doc lists 8 BORDERLINE features "where the tension with GD-004/ADR-0003/RDY-080 deserves explicit discussion, not silent dropping" — privacy lens agrees explicit discussion is required. Each BORDERLINE should get a decis"
  - `persona-redteam` in `AXIS_6_Assessment_Feedback_Research.yaml:L42` (P0) [ship-now]: "F3 AI Partial-Credit Grading emits a numeric score per step based on LLM rubric judgment. Without a per-step CAS verifier, the LLM is the sole correctness oracle for a score that feeds grade reports — direct ADR-0002 vio"
- **Tension**: `cache` vs `no-cache`
- **Recommended split**: split `prr-015` into two tasks, one per side of the tension, and route the decision to `conflicts.md` if not already covered there.

### W-008: Store Mashov credentials in secret manager + rotation runbook (prr-017)
- **Source findings**:
  - `persona-privacy` in `AXIS_10_Operational_Integration_Features.yaml:L114` (P2) [ship-now]: "Feature 8 (portability export) is already shipped in MeGdprEndpoints.cs / Student DataExporter.cs. Doc mis-positions as "new feature". Retire the proposal; only Hebrew translation of the export format docs remains."
  - `persona-redteam` in `AXIS_10_Operational_Integration_Features.yaml:L65` (P1) [cache]: "F4 Offline-First Practice Mode caches student PII + problem answers on device. On shared devices (typical in Israeli family settings with one tablet), the cache leaks across users. Requirements: (a) per-user encrypted ca"
  - `persona-sre` in `axis_10_operational_integration_features.yaml:L46` (P1) [no-cache]: "Feature 5 (Multi-Device sync) event stream contains "detailed interaction data" and explicitly says "Must NOT use this for ML training". Need a log-sink scrubber + prompt-injection guard before events hit long-term stora"
  - `persona-sre` in `axis_10_operational_integration_features.yaml:L58` (P1) [no-cache]: "Feature 6 (School IT admin dashboard) surfaces "error logs" to non-technical admins. Any unredacted raw error could leak other tenants or student PII. Must route through ExceptionScrubber + a tenant-isolation filter."
- **Tension**: `cache` vs `no-cache`
- **Recommended split**: split `prr-017` into two tasks, one per side of the tension, and route the decision to `conflicts.md` if not already covered there.

### W-009: k-anonymity floor (k≥10) for classroom/teacher aggregates (prr-026)
- **Source findings**:
  - `persona-educator` in `AXIS_4_Parent_Engagement_Cena_Research.yaml:L50` (P0) [ship-now]: "Feature 8 (Crisis Mode Bagrut Countdown Dashboard) is a countdown timer showing days-until-exam. Countdown timers are a textbook loss-aversion pattern — every day that passes is a "loss" from the student's remaining prep"
  - `persona-educator` in `AXIS_4_Parent_Engagement_Cena_Research.yaml:L110` (P2) [ship-now]: "Feature 2 (Bilingual Parent Dashboard) — "Hebrew, Arabic, Russian, Amharic." Amharic is listed as future — per memory feedback_language_strategy.md, the stack is English primary, Hebrew /Arabic secondary. Russian is an I"
  - `persona-educator` in `AXIS_7_Collaboration_Social_Features_Cena.yaml:L77` (P1) [no-cache]: "Feature 2 (Teacher-Mediated Micro Groups) — teacher-moderation queue for every message is realistic for a 3-4-student group only if the teacher is already highly engaged with Cena. In the field, teachers barely have time"
  - `persona-educator` in `AXIS_7_Collaboration_Social_Features_Cena.yaml:L100` (P2) [student-autonomy]: "Feature 6 (Reciprocal Peer Tutoring Matcher) matches students by complementary strengths. In practice, matching requires student- mastery profile visibility — which is sensitive data. Keep matching fully teacher-driven ("
  - `persona-educator` in `cena_axis5_teacher_workflow_features.yaml:L108` (P2) [cas-gate]: "Feature 6 (Student Conference Prep with strength-first talking points) — "auto-generated talking points" can easily slip into deterministic praise that sounds hollow ("Noam shows great persistence!") if the strength pool"
  - `persona-ethics` in `axis_4_parent_engagement_cena_research.yaml:L38` (P1) [ship-now,student-autonomy]: "F8 "Crisis Mode — Bagrut Countdown Dashboard" (L309-345) is the ethics hotspot of this axis. Multiple concerns: (a) "Crisis" branding itself is loss-aversion copy — ship-gate definition: "FOMO urgency." The label should "
- **Tension**: `cache` vs `no-cache`
- **Recommended split**: split `prr-026` into two tasks, one per side of the tension, and route the decision to `conflicts.md` if not already covered there.

### W-010: Cultural-context community review board — ops queue with DLQ + SLA (prr-034)
- **Source findings**:
  - `persona-educator` in `AXIS_8_Content_Authoring_Quality_Research.yaml:L60` (P1) [ship-now,teacher-moderate]: "Feature 5 (Culturally-Contextualized Problem Generator) — Dr Rami correctly flagged ES=1.16 as likely cherry-picked. In my pedagogue experience, culturally-relevant problems move engagement and some conceptual understand"
  - `persona-educator` in `AXIS_8_Content_Authoring_Quality_Research.yaml:L92` (P1) [ship-now]: "Feature 3 (Bagrut-Aligned Partial Credit Rubric Engine) — this is the feature that Bagrut teachers have been asking for since the first Cena demo. Already covered by axis5/axis6 extract tasks; deduplicate. The Ministry s"
  - `persona-educator` in `AXIS_8_Content_Authoring_Quality_Research.yaml:L116` (P2) [cas-gate]: "Feature 2 (SymPy CAS-Gated Variation Engine) is already substantially built (CasGatedQuestionPersister, CasConformanceSuite, CasRouter exist). What's missing per doc: template-DSL for authors + automated distractor gener"
  - `persona-ethics` in `axis_8_content_authoring_quality_research.yaml:L57` (P1) [teacher-moderate]: "F5 Culturally-Contextualized Problem Generator (L191-232) has extraordinary effect-size claims (cited ES=1.16, Rami flagged as cherry-picked). The ethics concern is separate from effect size: the feature creates per-coho"
  - `persona-ethics` in `axis_8_content_authoring_quality_research.yaml:L82` (P2) [ship-now]: "F6 Mother-Tongue-Mediated Hint System (L235-276) is sound. One ethics note: "Store only language preference, not interaction analytics. No cross-session language-profile tracking" (L272) is the right policy. Add explicit"
  - `persona-ethics` in `axis_8_content_authoring_quality_research.yaml:L96` (P2) [ship-now]: "F3 Bagrut-Aligned Partial Credit Rubric Engine (L104-143): proposes (L132) "Student sees step-by-step scoring on review; 'Why did I lose points?' breakdown." The "lost points" framing is loss-aversion copy. Recommend: "H"
- **Tension**: `cas-gate` vs `teacher-moderate`
- **Recommended split**: split `prr-034` into two tasks, one per side of the tension, and route the decision to `conflicts.md` if not already covered there.

### W-011: Grade-passback policy ADR + teacher opt-in veto + whitelist (prr-037)
- **Source findings**:
  - `persona-educator` in `AXIS_10_Operational_Integration_Features.yaml:L66` (P2) [cache]: "Feature 4 (Offline-First Practice Mode) — valuable for periphery students (Galilee, Negev, West Bank settlements). Pedagogue concern: offline practice should *not* include timed mock-Bagrut exams, because score-integrity"
  - `persona-ethics` in `axis_10_operational_integration_features.yaml:L91` (P2) [student-autonomy]: "F8 Student Data Portability Export (L~490+) is ethics-positive — right-to-access implementation. Small note: the parent/admin role-based access path (L497) must require student-side visibility when parent triggers export"
  - `persona-ministry` in `axis_10_operational_integration_features.yaml:L44` (P0) [cas-gate]: "Feature 7 "Google Classroom Assignment & Grade Passback" — if this passes back a Cena-invented "readiness", "predicted Bagrut", or "mastery %" as a grade, we have published a Ministry-observable prediction to a gradebook"
  - `persona-ministry` in `axis_10_operational_integration_features.yaml:L83` (P2) [cache,cas-gate]: "Feature 4 "Offline-First Practice Mode" must still respect CAS gating on displayed items. Local cache of CAS-gated items is fine (Verified binding cached alongside); locally-generated items (e.g., a parametric template i"
  - `persona-sre` in `axis_10_operational_integration_features.yaml:L46` (P1) [no-cache]: "Feature 5 (Multi-Device sync) event stream contains "detailed interaction data" and explicitly says "Must NOT use this for ML training". Need a log-sink scrubber + prompt-injection guard before events hit long-term stora"
  - `persona-sre` in `axis_10_operational_integration_features.yaml:L58` (P1) [no-cache]: "Feature 6 (School IT admin dashboard) surfaces "error logs" to non-technical admins. Any unredacted raw error could leak other tenants or student PII. Must route through ExceptionScrubber + a tenant-isolation filter."
- **Tension**: `cache` vs `no-cache`
- **Recommended split**: split `prr-037` into two tasks, one per side of the tension, and route the decision to `conflicts.md` if not already covered there.

### W-012: Ship-gate banned-terms scanner: all three locales + admin/full-version (prr-040)
- **Source findings**:
  - `persona-a11y` in `axis2_motivation_self_regulation_findings.yaml:L44` (P2) [ship-now]: "Feature 6 process-praise response system — any pre-authored message library must pass ship-gate banned-terms scanner in all three locales (streaks, 'don't break your chain' etc. must not sneak in via translation)."
  - `persona-a11y` in `cena_axis5_teacher_workflow_features.yaml:L35` (P1) [ship-now]: "Teacher UI must render math (student work previews, rubrics) with <bdi dir=\"ltr\"> in Hebrew/Arabic just like student-web. Doc doesn't specify whether admin/full-version has the same math-LTR scanner coverage as student"
  - `persona-ethics` in `axis2_motivation_self_regulation_findings.yaml:L57` (P1) [ship-now,teacher-moderate]: "Feature 3 "Wise Feedback Engine" (L102-138) proposes persona-targeted feedback templates and optional "collective-achievement lens" for Arabic-speaking students ("Your preparation matters for your future and your communi"
  - `persona-ethics` in `axis2_motivation_self_regulation_findings.yaml:L105` (P2) [student-autonomy]: "Feature 8 "Reflective Study Plan Generator" (L305-339) proposes parent-visible weekly plan sharing (L308). Parent visibility is opt-in (L336 mitigation) but the default state is not stated. Ethics lens requires: default "
  - `persona-ethics` in `cena_axis5_teacher_workflow_features.yaml:L35` (P1) [ship-now]: "F4 Exit Ticket Auto-Generator (L128-161) proposes (L131) teachers can share results with students as "You mastered X / You might want to review Y." Author notes "framed as encouragement, not shame" but BORDERLINE flag (L"
  - `persona-ethics` in `cena_axis5_teacher_workflow_features.yaml:L51` (P1) [student-autonomy]: "F6 Student Conference Prep (L199-230) auto-generates talking points about each student for parent-teacher conferences. Author flags (L227) that briefs are teacher-facing only. Ethics lens requires stronger: the student m"
- **Tension**: `cas-gate` vs `teacher-moderate`
- **Recommended split**: split `prr-040` into two tasks, one per side of the tension, and route the decision to `conflicts.md` if not already covered there.

### W-013: Live caller for AdaptiveScheduler at session start (prr-149)
- **Source findings**:
  - `persona-educator` in `axis1_pedagogy_mechanics_cena.yaml:L99` (P2) [cas-gate]: "Feature 4 (Elaborative Interrogation) suggests menu-based "why" prompts after worked-example step 2 of 4. Menu options narrow the explanation space and can short-circuit real elaboration. For the top-leverage concepts (q"
  - `persona-enterprise` in `axis1_pedagogy_mechanics_cena.yaml:L42` (P0) [refactor-first]: "Doc treats "scheduler" as a new service, but StudentActor is already 764 + 1036 + 353 + 363 + 453 = 2969 LOC of partial classes (god-aggregate pattern). ADR-0012 accepts this for pilot but mandates split before productio"
  - `persona-enterprise` in `axis1_pedagogy_mechanics_cena.yaml:L49` (P1) [ship-now]: "Feature 5 (hint governor) and Feature 8 (worked-example fading) both emit new events (HintGovernorDecided_V1-ish, FadingLevelChanged_V1-ish) but doc doesn't specify upcaster strategy. The existing HintRequested_V1 in Ped"
  - `persona-finops` in `axis1_pedagogy_mechanics_cena.yaml:L35` (P0) [quality-first]: "F4 (Elaborative Interrogation) and F8 (Worked-Example Fading w/ self-explanation) propose LLM grading of free-text student explanations on every step. At 10k concurrent students with ~20 explanations/hour, this is ~200k "
  - `persona-finops` in `axis1_pedagogy_mechanics_cena.yaml:L46` (P0) [cache,cas-gate]: "No self-explanation caching strategy proposed. Unlike SAI-003 explanation cache (keyed on question+ErrorType), self-explanation grading input is the student's free text — unhashable as-is. Need semantic/embedding-based c"
  - `persona-finops` in `axis1_pedagogy_mechanics_cena.yaml:L54` (P1) [cost-reduce]: "F5 Hint Governor — if LLM-scored stuck detection replaces heuristic path, costs jump. Current Haiku-based StuckClassifier has PerCallCostUsd=0.001 (StuckClassifierOptions.cs:82) and HeuristicSkipLlmThreshold=0.7 to skip "
- **Tension**: `ship-now` vs `refactor-first`
- **Recommended split**: split `prr-149` into two tasks, one per side of the tension, and route the decision to `conflicts.md` if not already covered there.

### W-014: Mentor/tutor override aggregate for schedule (prr-150)
- **Source findings**:
  - `persona-educator` in `axis1_pedagogy_mechanics_cena.yaml:L99` (P2) [cas-gate]: "Feature 4 (Elaborative Interrogation) suggests menu-based "why" prompts after worked-example step 2 of 4. Menu options narrow the explanation space and can short-circuit real elaboration. For the top-leverage concepts (q"
  - `persona-educator` in `cena_axis5_teacher_workflow_features.yaml:L108` (P2) [cas-gate]: "Feature 6 (Student Conference Prep with strength-first talking points) — "auto-generated talking points" can easily slip into deterministic praise that sounds hollow ("Noam shows great persistence!") if the strength pool"
  - `persona-enterprise` in `axis1_pedagogy_mechanics_cena.yaml:L42` (P0) [refactor-first]: "Doc treats "scheduler" as a new service, but StudentActor is already 764 + 1036 + 353 + 363 + 453 = 2969 LOC of partial classes (god-aggregate pattern). ADR-0012 accepts this for pilot but mandates split before productio"
  - `persona-enterprise` in `axis1_pedagogy_mechanics_cena.yaml:L49` (P1) [ship-now]: "Feature 5 (hint governor) and Feature 8 (worked-example fading) both emit new events (HintGovernorDecided_V1-ish, FadingLevelChanged_V1-ish) but doc doesn't specify upcaster strategy. The existing HintRequested_V1 in Ped"
  - `persona-enterprise` in `cena_axis5_teacher_workflow_features.yaml:L37` (P1) [ship-now]: "Teacher features rely on Firebase custom claims for mentor/instructor roles. ADR-0001 notes these claims don't exist yet and are Phase 3 work. Axis-5 is shipping Phase 3 features before the auth surface lands."
  - `persona-ministry` in `cena_axis5_teacher_workflow_features.yaml:L70` (P1) [ship-now]: "Feature 8 implied "Bagrut prep teacher" persona (L289) uses "Bagrut curriculum map" as a data source. This map must be the canonical scripts/bagrut-taxonomy.json + docs/curriculum/bagrut-taxonomy- review.md artefact; doc"
- **Tension**: `ship-now` vs `refactor-first`
- **Recommended split**: split `prr-150` into two tasks, one per side of the tension, and route the decision to `conflicts.md` if not already covered there.

