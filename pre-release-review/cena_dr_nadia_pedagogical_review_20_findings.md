# Dr. Nadia's Pedagogical Review: 20 SHIP-Recommended Findings for Cena
## Strict Cross-Examination | Date: 2026-04-20

---

## VERDICT SUMMARY TABLE

| # | Finding | Verdict | Key Concern |
|---|---------|---------|-------------|
| FD-001 | Interleaved Adaptive Scheduler | **PASS** | None significant |
| FD-002 | SymPy CAS-Gated Problem Variation Engine | **PASS** | Template quality control |
| FD-003 | Real-Time Misconception Tagging (Session-Scoped) | **PASS** | ADR-0003 compliance is non-negotiable |
| FD-004 | IRT-Driven Adaptive Placement Engine (2PL-CAT) | **PASS** | XL effort; ensure Bayesian small-sample correction paired |
| FD-005 | Formative-Summative Signal Split | **PASS** | None significant |
| FD-006 | Confidence Calibration with Certainty-Based Marking | **PASS** | Frame as calibration, not negative marking |
| FD-007 | Crisis Mode — Compression Schedule | **PASS** | Avoid implying Bagrut score prediction |
| FD-008 | AI Partial-Credit Grading with Step-Level Rubrics | **PASS** | Human-in-the-loop validation required initially |
| FD-009 | Socratic AI Tutor (No-Answer Mode) | **WARNING** | Friction for time-pressured Bagrut students; needs escape hatch |
| FD-010 | Arabic RTL Math Renderer | **PASS** | Core requirement, not optional |
| FD-011 | Culturally-Contextualized Problem Generator | **PASS** | Stereotyping risk; require community review board |
| FD-012 | Bayesian IRT Calibration Error Correction | **PASS** | Technical complexity high |
| FD-013 | Mother-Tongue-Mediated Hint System | **PASS** | Do not track language proficiency profiles |
| FD-014 | "Why This Problem?" Explainable AI | **WARNING** | Thin research base; potential UI clutter |
| FD-015 | Per-Session Error Analysis Report | **PASS** | Auto-deletion architecture must be audited |
| FD-016 | Team vs. Challenge Cooperative Competitions | **WARNING** | XL effort; complex moderation requirements |
| FD-017 | Focus Ritual — 60-Second Breathing Opener | **PASS** | Never frame as therapy |
| FD-018 | Learning Energy Tracker | **PASS** | Frame as study optimization, not mental health |
| FD-019 | Mashov Gradebook Sync | **PASS** | API is unofficial; build defensively |
| FD-020 | Bagrut-Aligned Partial Credit Rubric Engine | **PASS** | None significant |

**Totals: 17 PASS | 3 WARNING | 0 REJECT**

---

## DETAILED REVIEWS

---

### FD-001: Interleaved Adaptive Scheduler — PASS

**Pedagogical Soundness:** EXCELLENT. The research base is among the strongest in the entire set. Rohrer et al. (2014) classroom RCT (n=140, d=1.05 on delayed tests) and Brunmair & Richter (2019) meta-analysis (d=0.34, larger for bottom of distribution) provide robust peer-reviewed support. The adaptive weighting (more interleaving for students who struggle with strategy discrimination) is a sophisticated application beyond random mixing.

**Cena-Specific Fit:** EXCELLENT. Bagrut explicitly requires students to identify which technique applies without being told which unit it belongs to. Interleaving directly targets this exam skill. The 30-day delay effects (Rohrer et al. 2015, d=0.79) align with Bagrut retention needs.

**Arabic-Cohort Equity:** SUPPORTIVE. Reduces reliance on rote procedural matching — a particular risk for students who may have had less exposure to varied problem phrasing in Hebrew. Strategy discrimination is universally valuable.

**Motivation Design:** SUPPORTS intrinsic motivation. Builds competence through adaptive challenge. No loss aversion, no comparative shame. Session-local strategy discrimination scores respect privacy. The "Which technique did you use?" self-report prompt supports autonomy and metacognitive awareness.

**Implementation Risk:** LOW. Well-understood algorithm. Threshold-based heuristic (not ML). Compliant with all guardrails. The ~10-problem mix packet approach is proven (Khan Academy uses similar interleaving).

**Required Guardrails:** Strategy discrimination scores must remain session-local or anonymized aggregate only (ADR-0003). Problem presentation must not label which unit/skill the problem belongs to — this is critical for forcing strategy discrimination.

---

### FD-002: SymPy CAS-Gated Problem Variation Engine — PASS

**Pedagogical Soundness:** STRONG. STACK system's use of Maxima CAS for dynamic randomized questions is a proven competitive benchmark (University of Edinburgh, UK Open University). Singh & Gulwani (2012, AAAI) on auto-generating algebra problems provides academic grounding. The CAS-as-oracle architecture ensures mathematical correctness — a non-negotiable for trust.

**Cena-Specific Fit:** CRITICAL INFRASTRUCTURE. This is not a feature; it is the content pipeline. Unlimited validated problem generation is essential for scaling to Bagrut-level content across 3/4/5-unit tracks.

**Arabic-Cohort Equity:** CRITICAL. Research shows Arab students need 22% more Meitzav improvement time in math (Blass 2020). Manual item authoring cannot meet this demand. CAS-gated generation eliminates the content bottleneck while ensuring zero errors that would undermine trust in Arabic-medium instruction.

**Motivation Design:** SUPPORTS competence. Validated problems mean students never encounter mathematically incorrect questions (a trust-destroying experience common on lower-quality platforms). Infinite variety prevents repetition fatigue.

**Implementation Risk:** LOW-MEDIUM. Template DSL design requires careful authoring interface design. Distractor generation from algebraic misconception rules (not student data) is the correct approach per ADR-0003. Quality control pipeline needed: template -> SymPy validates -> human review for cultural context -> deploy.

---

### FD-003: Real-Time Misconception Tagging (Session-Scoped) — PASS

**Pedagogical Soundness:** EXCELLENT. Eedi's research with 4M+ student responses across 36,000 distractor-misconception mappings (Barton/Woodhead 2022) provides unprecedented scale. The DeepMind/Eedi RCT (2025) finding 95.4% misconception resolution and 93.0% retry success vs. 65.4% for static hints is extraordinary — statistically equivalent to human tutor effectiveness.

**Cena-Specific Fit:** EXCELLENT. Bagrut students often repeat the same errors across problem types. Catching misconceptions in the moment, before they crystallize, is dramatically more effective than retrospective correction. This is the highest-leverage pedagogical feature in the set.

**Arabic-Cohort Equity:** STRONGLY SUPPORTIVE. Session-scoped tagging means no persistent deficit labels — critical for students who may already face lowered expectations. Real-time resolution in Arabic prevents error patterns from compounding.

**Motivation Design:** SUPPORTS competence and reduces shame. The framing is "It looks like you might be..." not "You have misconception X." The follow-up question for immediate practice supports mastery orientation. All data session-scoped = no accumulated "error record."

**Implementation Risk:** MEDIUM. ADR-0003 compliance is the highest-stakes architectural constraint. Must use Redis-only session storage with auto-purge. No SQL records linking student_id to misconception_tag_id. Static taxonomy (pre-curated), not learned from student data. Requires explicit code review for compliance.

**Required Guardrails:** Redis-only with TTL; no cross-session knowledge tracing; class overview shows only real-time session-active tags; the student starts each session with a clean slate.

---

### FD-004: IRT-Driven Adaptive Placement Engine (2PL-CAT) — PASS (with note)

**Pedagogical Soundness:** EXCELLENT. IRT-CAT is among the most validated assessment technologies in psychometrics. Cosyn et al. (2024, EDM) demonstrate AUROC >0.85 with at most 16 questions. IXL's LevelUp Diagnostic achieves classification consistency >0.85. The GRE and GMAT use IRT-CAT for high-stakes decisions.

**Cena-Specific Fit:** STRONG. Accurate placement into 3/4/5-unit Bagrut tracks is essential. Students switching between tracks or returning after gaps need precise gap analysis. Reduces testing time by 50-70% vs. fixed-form tests.

**Arabic-Cohort Equity:** REQUIRES FD-012 PAIRING. Standard IRT requires 200-500 examinees per item for stable estimates. Without Bayesian small-sample correction, Arab-cohort items would suffer from higher parameter error, leading to mistargeted difficulty and student frustration. FD-012 (Bayesian IRT Calibration Error Correction) must ship alongside or before this feature.

**Motivation Design:** SUPPORTS competence through appropriate challenge level. Students who are placed correctly experience early success rather than frustration from misaligned content. The "knowledge map" visualization (mastered vs. gap topics) supports autonomy through transparent self-knowledge.

**Implementation Risk:** HIGH (XL effort). Requires calibrated item bank of 500+ items with IRT parameter estimation, adaptive item selection algorithm, Bayesian ability estimation engine. This is the highest-engineering-complexity feature in the set. Must be paired with FD-012 for Arabic-cohort equity.

---

### FD-005: Formative-Summative Signal Split — PASS

**Pedagogical Soundness:** EXCELLENT. Black & Wiliam (1998) meta-review of 600+ studies found formative assessment effectively doubles learning speed. Wiliam (2011) defines the formative-summative distinction clearly. Bloom's (1968) mastery learning framework provides the theoretical foundation.

**Cena-Specific Fit:** CRITICAL. The gap between practice performance (with hints, retries, untimed conditions) and exam performance (timed, no supports, high-stakes) is the single biggest readiness blindspot for Bagrut students. Khan Academy's explicit mastery demotion mechanics demonstrate the value of cross-signal validation.

**Arabic-Cohort Equity:** SUPPORTIVE. Transparent signal separation helps all students understand their true readiness. Prevents overconfident students (common across all cohorts) from skipping necessary review.

**Motivation Design:** SUPPORTS autonomy through self-knowledge. The dual progress bars (Practice Strength vs. Exam Readiness) are informational, not comparative. The gap indicator is framed constructively, not shaming.

**Implementation Risk:** LOW. Dual scoring pipeline is well-understood architecture. Formative data is session-scoped per ADR-0003; only summative mock-exam scores retained long-term.

---

### FD-006: Confidence Calibration with Certainty-Based Marking — PASS

**Pedagogical Soundness:** STRONG. Foster (2016, 2021, 2025) provides a sustained research program specifically on confidence assessment in mathematics classrooms. Gardner-Medwin (2006, 2019) on Certainty-Based Marking demonstrates deeper learning stimulation. Foster's 2021 study found students "overwhelmingly positive" about confidence assessment, calling it "low-hanging fruit."

**Cena-Specific Fit:** STRONG. Overconfident Bagrut students skip topics they think they know; underconfident students waste time on mastered material. Calibration addresses both. The scoring formula (sum confidence for correct minus sum for incorrect) incentivizes truthful calibration.

**Arabic-Cohort Equity:** SUPPORTIVE. Foster (2025) confirmed gender differences in calibration exist but the process itself is equitable — it benefits both over- and under-confident students. No cohort-specific disadvantage.

**Motivation Design:** SUPPORTS metacognitive competence and autonomy. Calibration is a self-knowledge tool, not a comparative metric. The session-end calibration chart (confidence vs. accuracy) is personal insight only. Key design: frame underconfidence positively ("You're learning more than you think!") and overconfidence constructively ("Great chance to strengthen this area").

**Implementation Risk:** LOW. Very low implementation effort (S). The negative-marking scheme is transparently pedagogical, not gamified loss-aversion. Foster's research confirms students experience it as engaging and useful, not punitive.

---

### FD-007: Crisis Mode — Compression Schedule — PASS

**Pedagogical Soundness:** STRONG. Dunlosky et al. (2013) identified practice testing and distributed practice as the two highest-utility learning techniques with broad generalizability. The compressed spacing schedule (Day 1, Day 3, Week 1, Week 2) is evidence-based for short timelines. UWorld's 90% pass rate with adaptive study planners provides competitive validation.

**Cena-Specific Fit:** EXCELLENT. Crisis prep is a core use case for Bagrut. The priority scoring algorithm (exam frequency x point weight x gap severity) maximizes points per study hour — exactly what students need. The progressive shift from broad review to self-testing to weak-area focus to timed simulation matches established exam prep models.

**Arabic-Cohort Equity:** SUPPORTIVE. Helps students who start late, are retaking, or have limited study time. The rapid CAT diagnostic (10-12 items) for gap identification is efficient and non-stigmatizing.

**Motivation Design:** SUPPORTS autonomy through structured prioritization. Reduces overwhelm by making the study plan concrete and actionable. The "topics secured" framing is accumulation-based (no loss aversion). Daily micro-assessments provide frequent competence feedback.

**Implementation Risk:** MEDIUM. Must avoid implying Bagrut score prediction (RDY-080). The explicit de-prioritization of deep conceptual exploration is a necessary and honest trade-off for crisis timelines — this should be transparent to students.

---

### FD-008: AI Partial-Credit Grading with Step-Level Rubrics — PASS

**Pedagogical Soundness:** STRONG. Yu et al. (2026) demonstrated >90% score agreement with human TAs on 3,851 free-response calculus samples. The rubric-guided LLM prompting approach achieves 79.79% fully correct feedback. Competitive products (Notie AI, GradeWithAI) confirm viability.

**Cena-Specific Fit:** CRITICAL ALIGNMENT. Partial credit IS the Bagrut scoring methodology. Method marks constitute 50-70% of available credit. Students can earn significant credit for correct methodology even with computational errors — this is exactly what the rubric engine replicates.

**Arabic-Cohort Equity:** STRONGLY SUPPORTIVE. Fair, consistent grading regardless of handwriting quality or teacher bias. Arab students whose Hebrew handwriting may be less practiced benefit from algorithmic consistency.

**Motivation Design:** SUPPORTS competence through actionable feedback. Per-step score breakdown shows exactly where reasoning breaks down — far more informative than binary correct/incorrect. The "Why did I lose points?" explanation with reference to specific Bagrut rules builds procedural fluency with exam mechanics.

**Implementation Risk:** MEDIUM. Requires human-in-the-loop review workflow initially. LLM hallucination risk is mitigated by SymPy CAS validation — the LLM evaluates reasoning quality but cannot override CAS on mathematical truth. This CAS-first architecture is correct.

---

### FD-009: Socratic AI Tutor (No-Answer Mode) — WARNING (Required Revision)

**Pedagogical Soundness:** THEORETICALLY STRONG. Socratic method has deep pedagogical roots. Bisra et al. (2018) meta-analysis found self-explanation g=0.55 across 64 studies. Rittle-Johnson et al. (2017) found g=0.27 for math-specific self-explanation. Chi et al. (1989) on self-explanations is foundational.

**Cena-Specific Fit:** QUESTIONABLE. This is the primary concern. Bagrut prep is often crisis-mode, time-pressured, and efficiency-focused. Students with <6 months to exam may find a no-answer Socratic tutor frustrating when they need rapid, actionable help. The evidence for Socratic tutoring is strongest in conceptual understanding contexts, not high-stakes exam prep.

**Arabic-Cohort Equity:** MIXED. Socratic questioning in Arabic could leverage rich questioning traditions. But the linguistic complexity of Socratic dialogue in a student's L2 (Hebrew) is cognitively demanding. Arabic-speaking students may struggle more with open-ended Socratic prompts than structured hints.

**Motivation Design:** RISK OF UNDERMINING AUTONOMY. A tutor that never gives direct answers can feel controlling rather than supportive, especially when students are genuinely stuck. Self-Determination Theory's competence need is not met if the system consistently withholds help.

**Implementation Risk:** HIGH. LLM-based Socratic dialogue requires sophisticated reasoning chain management. The "escape hatch" to full worked solutions must be always available and prominently displayed. Without it, this feature will drive student abandonment.

**Required Revision:** (1) Make Socratic mode OPT-IN per session, not default. (2) Always provide "Show me the solution" escape hatch after 2-3 Socratic exchanges. (3) Use structured sentence starters (in Arabic and Hebrew) rather than open-ended dialogue. (4) Limit to conceptual "why" questions, not procedural "how" questions where students need direct help. (5) A/B test with Israeli students before full rollout.

---

### FD-010: Arabic RTL Math Renderer — PASS

**Pedagogical Soundness:** N/A (Infrastructure feature, not instructional intervention). The standards base is strong: MathML 4.0 (2026) formally defines RTL behavior, Alsheri (2014) documented the rendering problem academically.

**Cena-Specific Fit:** NON-NEGOTIABLE REQUIREMENT. This is not a feature to evaluate; it is a prerequisite for serving Arabic-speaking students. The KaTeX RTL rendering bug documented in OpenAI's community forum (2025) shows that math expressions inheriting surrounding RTL directionality produce misaligned, backwards equations. For Cena's Arabic-speaking student population, this is a core requirement.

**Arabic-Cohort Equity:** THIS IS THE EQUITY FEATURE. Arabic students in Israel learn math in Arabic-medium schools with Arabic mathematical notation. Switching to Western notation creates cognitive friction — students must mentally translate notation while learning math concepts. A notation-native renderer removes this barrier entirely.

**Motivation Design:** SUPPORTS competence by removing extraneous cognitive load. Math feels native rather than translated. Supports relatedness by making Cena feel like an extension of the Arabic math classroom, not a foreign product.

**Implementation Risk:** LOW-MEDIUM. MathML 4.0 provides clear guidance. Edraak's Arabic MathJax extension proves technical feasibility. Wiris MathType Arabic Support demonstrates commercial viability. Requires careful direction isolation CSS and font selection (Amiri or Noto Naskh Arabic).

---

### FD-011: Culturally-Contextualized Problem Generator — PASS (with required guardrail)

**Pedagogical Soundness:** EXTRAORDINARY. Zayyadi et al. (2024) meta-analysis found effect size of 1.16 for culturally based math instruction — the highest in the entire 20-feature set. Blass (2021) found that Bedouin ethnomathematics contexts enhanced self-efficacy and learning outcomes. Students reported "authentic and meaningful learning" vs. conventional lessons.

**Cena-Specific Fit:** EXCELLENT. Israel's student population is extraordinarily diverse (Jewish, Arab, Ethiopian, Russian, Druze, Bedouin). Culturally authentic scenarios (tatreez geometry, olive harvest ratios, buna coffee ceremony, Soviet olympiad style) make math feel relevant across all cohorts.

**Arabic-Cohort Equity:** THE HIGHEST-IMPACT EQUITY FEATURE. ES=1.16 is unprecedented. Palestinian embroidery for symmetry, Ramadan timing for arithmetic, traditional Arab architecture for geometry — these are not decorative wrappers but authentic mathematical contexts that Arab students recognize as their own.

**Motivation Design:** POWERFULLY SUPPORTS relatedness and intrinsic motivation. Culturally relevant problems signal "your culture is valued here" — a profound belonging message for marginalized students. The effect size of 1.16 suggests this is not just engagement but deep conceptual understanding.

**Implementation Risk:** MEDIUM. Requires ongoing content authoring effort, not just engineering. **CRITICAL GUARDRAIL:** Community review board with cohort representatives must approve all cultural contexts before deployment. Stereotyping risk is real — scenarios must be authentic, not tokenizing. Context equivalence validator needed to ensure all wrappers for the same mathematical skeleton have identical difficulty.

---

### FD-012: Bayesian IRT Calibration Error Correction — PASS

**Pedagogical Soundness:** STRONG. Koenig et al. (2025) found 84% bias reduction vs. standard CAT with small calibration samples. Marsman et al. (2025) on redefining IRT for small samples provides complementary theoretical framing.

**Cena-Specific Fit:** CRITICAL FOUNDATION. Without this, FD-004 (IRT-CAT) will misplace Arabic-cohort students due to insufficient calibration data. Arab Israeli students represent a smaller population — standard IRT's 200-500 examinee minimum per item is hard to achieve for Arabic-medium items before deployment.

**Arabic-Cohort Equity:** DISGUISED EQUITY FEATURE. Accurate calibration ensures Arabic items adapt correctly from day one. Without it, Arab-cohort items would have higher parameter error, leading to mistargeted difficulty and student frustration — compounding existing achievement gaps.

**Motivation Design:** INDIRECT. Accurate difficulty targeting means appropriate challenge → flow state → sustained engagement. Mis calibrated items (too hard or too easy) undermine competence and motivation.

**Implementation Risk:** HIGH (L effort). Requires PyMC or Stan integration, Hamiltonian Monte Carlo sampling, posterior distribution storage per item. This is advanced psychometric infrastructure. Must be implemented before or alongside FD-004.

---

### FD-013: Mother-Tongue-Mediated Hint System — PASS

**Pedagogical Soundness:** STRONG. IJES (2025) study of 45 Arab students in Israeli higher education found "students who had a better command of their mother tongue did understand better and had more confidence." Recommendations include "supporting mother-tongue instruction, encouraging translanguaging practices, and developing culturally responsive pedagogical materials." ERIC ED601568 confirms code-switching as a major challenge theme.

**Cena-Specific Fit:** EXCELLENT. This operationalizes translanguaging pedagogy without requiring full Arabic curriculum translation — a pragmatic approach. Mathematical terms preserved in Hebrew (the language of Bagrut) while conceptual explanations delivered in Arabic (the cognitive language).

**Arabic-Cohort Equity:** DIRECTLY SERVES. The hint system provides targeted cognitive scaffolding in the student's strongest language. Clickable mathematical terms show Hebrew equivalents, building the Arabic-Hebrew math vocabulary bridge needed for Bagrut success.

**Motivation Design:** SUPPORTS competence by reducing language anxiety. Students can think in their cognitive language while learning Hebrew math terminology. The flashcard feature for mother-tongue <-> Hebrew math vocabulary supports autonomous learning.

**Implementation Risk:** LOW-MEDIUM. Requires multilingual hint database with code-switching rules. Key guardrail: store only language preference, not interaction analytics. No cross-session language-proficiency tracking.

---

### FD-014: "Why This Problem?" Explainable AI — WARNING (Required Revision)

**Pedagogical Soundness:** WEAK. Explainable AI (XAI) in education is an emerging field with limited rigorous evidence. No meta-analyses or large-scale RCTs demonstrate learning gains from recommendation explanations. The theoretical argument (transparency supports trust and metacognition) is plausible but unvalidated at scale.

**Cena-Specific Fit:** MODERATE. Understanding why a problem was recommended could build trust, especially for students who may be skeptical of algorithmic systems. But the benefit is speculative. Bagrut students may not care about recommendation rationale — they care about whether the problem helps them prepare.

**Arabic-Cohort Equity:** POTENTIALLY SUPPORTIVE. Transparency about algorithmic decisions could reduce mistrust of technology-mediated instruction among students who may have had negative experiences with "black box" systems.

**Motivation Design:** POTENTIALLY SUPPORTS autonomy (understanding the system's reasoning) but risks ADDING cognitive overhead. Every UI element competes for attention. If the explanation is verbose or appears at the wrong moment, it undermines rather than supports learning.

**Implementation Risk:** MEDIUM. Primary risk is UI clutter and distraction from core learning. Thin research base means we're building on intuition, not evidence.

**Required Revision:** (1) Do not ship as a standalone feature. Instead, embed minimal explanation (one sentence: "You're practicing this because...") into the existing problem header. (2) Run an A/B test measuring session completion with and without explanations before committing to full implementation. (3) If implemented, keep explanations to <15 words. (4) Do not invest engineering resources until stronger evidence emerges.

---

### FD-015: Per-Session Error Analysis Report — PASS

**Pedagogical Soundness:** STRONG. Kehrer et al. (2013) found immediate feedback with error analysis improved learning by 12% (ES=0.37). Yu et al. (2026) confirmed 79.79% of AI-generated step-level feedback was fully correct and actionable. ASSISTments' Common Error Diagnostics project (NSF-funded) provides sustained research validation.

**Cena-Specific Fit:** STRONG. Error-type classification (computational vs. procedural vs. conceptual) is particularly valuable for Bagrut because computational and procedural errors are recoverable with practice, while conceptual errors require re-teaching. The session-end timing makes it actionable for next session planning.

**Arabic-Cohort Equity:** STRONGLY SUPPORTIVE. Session-scoped error data means no persistent deficit profile — students start each session fresh. This is especially important for students who may face lowered expectations. The "Practice Similar Problems" button provides immediate constructive action.

**Motivation Design:** SUPPORTS competence through error awareness. The framing (error types as learning opportunities, not shame) is consistent with growth-mindset feedback design. The visual summary (pie chart + example problems) makes error patterns concrete and actionable.

**Implementation Risk:** MEDIUM. Auto-deletion architecture must be rigorously implemented and audited. Session data in Redis with 24-hour TTL. No SQL persistence of error patterns. The "Practice Similar Problems" generated exercises must also expire with the session.

---

### FD-016: Team vs. Challenge Cooperative Competitions — WARNING (Required Revision)

**Pedagogical Soundness:** STRONG. Ke & Grabowski (2007) found cooperative gameplay in math produced equal learning gains to competitive gameplay but significantly better attitudes toward math (DOI: 10.1111/j.1467-8535.2006.00593.x). DeVries & Slavin's (1978) TGT method shows intergroup competition does not damage intragroup collaboration without high-stakes prizes.

**Cena-Specific Fit:** MODERATE. Engagement driver for Israeli students, but the Bagrut context is individual, high-stakes, and time-pressured. Cooperative games are valuable for engagement and attitude but are not directly exam-relevant.

**Arabic-Cohort Equity:** SUPPORTIVE. Cooperative structures align with collectivist cultural values common in Arab educational contexts. Arabic-language problem sets within team challenges support inclusion. But mixed Jewish-Arab team dynamics require careful teacher oversight.

**Motivation Design:** SUPPORTS relatedness through teamwork. The milestone-based design (Bronze/Silver/Gold) avoids shame if implemented correctly — all tiers must be framed positively with no public display of which tier other teams reached.

**Implementation Risk:** VERY HIGH (XL effort). This is the highest-implementation-complexity feature in the set. Real-time scoring engine, WebSocket connections, team matching, pre-written chat only (no free text for privacy), teacher moderation, cross-device synchronization. The engineering investment is massive relative to the pedagogical gain.

**Required Revision:** (1) Defer to Phase 3 after all core learning features are proven. (2) Start with a much simpler implementation: teacher-facilitated group problem sets with shared progress bar, not full game mechanics. (3) If full cooperative mode is built, ensure Bronze/Silver/Gold tiers are ALL framed as achievements ("Solid Foundation!", "Rising Star!", "Master Team!") with no comparative display. (4) Pre-written encouraging phrases only in team chat — no free text. (5) Teacher must be able to reshuffle teams before challenge begins.

---

### FD-017: Focus Ritual — 60-Second Breathing Opener — PASS

**Pedagogical Soundness:** MODERATE-STRONG. Breathing exercises for math anxiety have US Department of Education support. The 4-7-8 breathing pattern has established physiological effects on the parasympathetic nervous system. Calm/Headspace provide competitive validation at scale.

**Cena-Specific Fit:** STRONG. Bagrut pressure creates significant math anxiety for many Israeli students. A 60-second pre-session ritual is a low-friction intervention. The mood check-in (4 emoji options) can optionally adjust opening difficulty — a thoughtful adaptive touch.

**Arabic-Cohort Equity:** UNIVERSAL BENEFIT. Mindfulness and breathing practices exist across all cultures including Arab traditions. The feature is equally accessible regardless of language or background.

**Motivation Design:** SUPPORTS autonomy (fully optional, skip with one tap) and competence (reduced anxiety enables better focus). The correlation between mood and session completion (visible to student only) supports self-regulated learning.

**Implementation Risk:** VERY LOW. CSS animation + breath timing logic. ~1-2 weeks engineering. **CRITICAL GUARDRAIL:** Must be explicitly framed as "focus ritual," not mental health treatment. No therapy claims. No streak tracking on breathing practice.

---

### FD-018: Learning Energy Tracker — PASS

**Pedagogical Soundness:** MODERATE. Mood tracking with correlation analytics is informed by self-monitoring research but lacks the rigorous RCT evidence of features like interleaving or misconception tagging. Sanvello provides competitive validation for mood tracking generally.

**Cena-Specific Fit:** STRONG. The insight ("I'm strongest at algebra on Sunday mornings") is genuinely useful for self-directed study optimization. The optional one-tap post-session mood rating has minimal friction. The weekly Learning Energy Report is personal and actionable.

**Arabic-Cohort Equity:** UNIVERSAL BENEFIT. Private insights with no peer comparison. All students benefit equally from self-knowledge about their own learning patterns.

**Motivation Design:** STRONGLY SUPPORTS autonomy. This is self-knowledge as empowerment — students discover their own patterns and can act on them. The student-controlled data deletion supports autonomy and trust.

**Implementation Risk:** LOW. ~3-4 weeks engineering. **GUARDRAIL:** Must be framed as "learning energy" and "study optimization," not mental health or mood tracking. COPPA-compliant: for under-13, data is session-local and not retained.

---

### FD-019: Mashov Gradebook Sync (Read-Only) — PASS

**Pedagogical Soundness:** N/A. Operational integration feature, not instructional intervention.

**Cena-Specific Fit:** ESSENTIAL. Mashov is the dominant school management system in Israel (1,550+ schools). Without Mashov integration, Cena cannot become the "single dashboard" teachers check daily. Read-only approach minimizes risk while providing unified view.

**Arabic-Cohort Equity:** UNIVERSAL BENEFIT. All Israeli schools (Jewish and Arab sectors) use Mashov. The integration is equally valuable across all cohorts.

**Motivation Design:** INDIRECT. Reduces teacher administrative burden → better teacher engagement → better student experience. Teachers who spend less time switching between systems have more energy for instruction.

**Implementation Risk:** MEDIUM. Mashov API is unofficial/community-documented. Priority Software could change endpoints without notice. **REQUIRED GUARDRAILS:** Abstract API calls behind an interface layer; implement comprehensive health checks and graceful degradation (display last-synced data with timestamp); teacher credentials encrypted (AES-256) and never logged; strictly read-only — never write back to Mashov.

---

### FD-020: Bagrut-Aligned Partial Credit Rubric Engine — PASS

**Pedagogical Soundness:** STRONG. The Bagrut scoring rules are well-documented (Scala School, 2020). May et al. (2023) peer-reviewed research on partial credit vs. dichotomous scoring provides academic grounding. The "carry error forward only if work is sound" rule is a specific, well-defined algorithmic requirement.

**Cena-Specific Fit:** CRITICAL ALIGNMENT. Internalizing the exact Bagrut rubric demystifies the exam for students. The rubric engine maps each problem's solution path into Bagrut-scorable steps with the official penalty structure. This is exam-preparation infrastructure, not just grading.

**Arabic-Cohort Equity:** STRONGLY SUPPORTIVE. The Arab-Israeli Bagrut math participation gap is significant: only 7% of Arab students took 5-unit math in 2014 vs. 16% in Hebrew education. The gap is in TAKING the exam, not performance. By building procedural fluency with the scoring rules themselves, Cena reduces test anxiety and increases the likelihood students will attempt higher-unit exams.

**Motivation Design:** SUPPORTS competence through transparency. Understanding exactly how partial credit works transforms the exam from an opaque authority into a knowable system. The "Why did I lose points?" breakdown with reference to specific Bagrut rules empowers students to strategize their exam approach.

**Implementation Risk:** LOW-MEDIUM. Rules are well-documented. Requires encoding Bagrut scoring rules as configurable rubric DSL. SymPy validates each step's mathematical correctness; rubric engine decides how to penalize based on SymPy output.

---

## CROSS-CUTTING ANALYSIS

### Effect Size Realism Check

| Finding | Cited ES | Source Type | Realistic for Cena? |
|---------|----------|-------------|-------------------|
| FD-001 Interleaving | d=1.05 (Rohrer 2014), d=0.34 meta | RCT + meta-analysis | d=0.34-0.50 realistic. d=1.05 is from single study with 7th graders; may not replicate for older Bagrut students. |
| FD-003 Misconception Tagging | 95.4% resolution | Large-scale RCT (DeepMind/Eedi) | Highly realistic — this is recent, well-powered evidence. |
| FD-004 IRT-CAT | AUROC >0.85 | EDM conference + IXL tech manual | Realistic for established CAT. New platform may need 6-12 months calibration. |
| FD-006 Confidence Calibration | "Low-hanging fruit" (Foster) | Multiple peer-reviewed studies | Foster's own characterization. Small but reliable effects expected. |
| FD-007 Crisis Mode | 90% pass rate (UWorld) | Competitive data | UWorld is test-prep specialized; Cena should not claim comparable rates without comparable data. |
| FD-008 AI Grading | 90%+ agreement | Large-scale study (Yu et al. 2026) | Realistic for calculus free-response. Bagrut-specific rubrics may need separate validation. |
| FD-011 Cultural Context | ES=1.16 | Meta-analysis (Zayyadi et al. 2024) | Exceptionally high. May reflect novelty effects. d=0.50-0.80 more conservative estimate. |
| FD-012 Bayesian IRT | 84% bias reduction | Peer-reviewed (Koenig 2025) | Realistic for small samples. Effect diminishes as N>200. |

### Intrinsic Motivation Risk Map

| Risk Level | Findings | Concern |
|------------|----------|---------|
| LOW | FD-001, 002, 003, 005, 010, 012, 013, 015, 017, 018, 019, 020 | No motivation risk identified |
| LOW-MEDIUM | FD-004, 006, 007, 008 | Minor framing considerations |
| MEDIUM | FD-009, FD-014, FD-016 | Could undermine autonomy or add cognitive overhead if poorly implemented |
| HIGH | None | No features pose serious motivation risk |

### Arabic-Cohort Equity Impact Ranking

| Rank | Finding | Equity Impact |
|------|---------|---------------|
| 1 | FD-011 Cultural Context (ES=1.16) | Transformational — highest effect size in set |
| 2 | FD-010 Arabic RTL Renderer | Non-negotiable infrastructure |
| 3 | FD-003 Real-Time Misconception Tagging | Prevents error pattern crystallization |
| 4 | FD-013 Mother-Tongue Hints | Direct cognitive scaffolding |
| 5 | FD-012 Bayesian IRT Calibration | Prevents mistargeted difficulty for Arab items |
| 6 | FD-020 Bagrut Rubric Engine | Reduces exam anxiety, increases participation |
| 7 | FD-008 AI Partial-Credit Grading | Fair, consistent grading |
| 8 | FD-002 CAS Problem Variation | Eliminates content bottleneck |

### Features Requiring Paired Implementation

| Primary Feature | Must Pair With | Reason |
|-----------------|----------------|--------|
| FD-004 IRT-CAT | FD-012 Bayesian Calibration | Arab-cohort items need small-sample correction |
| FD-007 Crisis Mode | FD-005 Formative-Summative Split | Crisis mode needs accurate readiness signals |
| FD-008 AI Grading | FD-020 Bagrut Rubric Engine | Grading must map to official Bagrut scoring |
| FD-009 Socratic Tutor | FD-013 Mother-Tongue Hints | Socratic dialogue must work in Arabic |

---

## FINAL RECOMMENDATIONS

### Tier 1: Ship Immediately (Highest Evidence + Lowest Risk)
1. **FD-010** — Arabic RTL Renderer (non-negotiable prerequisite)
2. **FD-002** — CAS Problem Variation (core infrastructure)
3. **FD-001** — Interleaved Scheduler (strongest evidence, lowest risk)
4. **FD-005** — Formative-Summative Split (foundational)
5. **FD-017** — Focus Ritual (S effort, immediate value)
6. **FD-019** — Mashov Sync (Israel market requirement)

### Tier 2: Ship After Validation (Strong Evidence, Moderate Complexity)
7. **FD-003** — Misconception Tagging (excellent evidence, ADR-0003 compliance)
8. **FD-008** — AI Partial-Credit Grading (high teacher value)
9. **FD-020** — Bagrut Rubric Engine (exam alignment)
10. **FD-011** — Cultural Context Generator (ES=1.16, but needs content investment)
11. **FD-013** — Mother-Tongue Hints (strong evidence, content effort)
12. **FD-006** — Confidence Calibration (S effort, strong evidence)
13. **FD-015** — Session Error Analysis (ADR-0003 compliant)

### Tier 3: Ship With Caution (Requires Revision or Guardrails)
14. **FD-004** — IRT-CAT (XL effort; pair with FD-012)
15. **FD-012** — Bayesian IRT Calibration (technical complexity)
16. **FD-007** — Crisis Mode (avoid score prediction claims)
18. **FD-018** — Learning Energy Tracker (low risk, moderate evidence)

### Tier 4: Revise Before Shipping
19. **FD-009** — Socratic Tutor (needs escape hatch, opt-in design, A/B test)
20. **FD-014** — Explainable AI (thin evidence; embed minimally, don't build standalone)
21. **FD-016** — Cooperative Competitions (XL effort; defer to Phase 3)

---

*Review completed by Dr. Nadia, Senior Learning Scientist*
*All verdicts based on: pedagogical evidence quality, Cena-specific fit analysis, Arabic-cohort equity assessment, motivation design evaluation, and implementation risk analysis.*
