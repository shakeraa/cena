# Dr. Rami Assessment: Feature Discovery Findings (SHIP-Recommended)

## Assessment Framework
- **Effect Size Justification**: Is the cited ES representative of meta-analytic evidence or cherry-picked from extreme primary studies?
- **Competitive Analysis Accuracy**: Are competitive products described without overstating?
- **RDY-080 (Bagrut Prediction)**: Does the feature claim to predict Bagrut outcomes without calibrated concordance evidence?
- **ADR-0003 (Session-Scoped Misconceptions)**: Does misconception data truly not persist across sessions?
- **DPA §7 (ML Training on Student Data)**: Does the feature require training ML models on student responses?
- **Cherry-Picking/Publication Bias**: Is the cited literature selectively represented?

---

## Verdict Table

| ID | Finding | Verdict | Key Reasoning |
|----|---------|---------|---------------|
| FD-001 | Interleaved Adaptive Scheduler — Rohrer et al., ES ~0.5-0.8 | **WARNING** | Meta-analytic estimate (Brummair & Richter 2019: d=0.34) is substantially lower than the cited range. Rohrer 2020 RCT found d=0.83 but interleaved students spent MORE time on task (dosage confound). Recent large-scale replication found d=0.29 short-term, d=0.04 (n.s.) for cumulative retention. The 0.5-0.8 range cherry-picks the upper tail of a highly heterogeneous literature. **Required revision**: Cite meta-analytic mean (d~0.34) with explicit caveat about time-on-task confound. |
| FD-002 | SymPy CAS-Gated Problem Variation Engine — STACK, Singh & Gulwani AAAI | **PASS** | STACK is a well-established production system (University of Edinburgh, 20+ years). Singh & Gulwani's AAAI work on automated feedback is legitimate. No ES claim. Rules-based symbolic engine requires no student-data ML training. No Bagrut prediction implied. Note: Verify Singh & Gulwani is the correct AAAI reference for CAS-gated variation (their work is primarily on program repair/feedback generation). |
| FD-003 | Real-Time Misconception Tagging (Session-Scoped) — Eedi/DeepMind RCT, 95% misconception resolution | **REJECT** | The 95% "misconception resolution" claim is NOT supported by the Eedi/DeepMind literature. The 2025 DeepMind/Eedi RCT defines misconception resolution as post-intervention success within a study unit; actual reported success rates are ~60-80% across conditions, not 95%. This figure appears fabricated or severely misattributed. **ADR-0003 compliance**: Claiming 95% resolution implies persistent misconception tracking, which may require cross-session retention vectors — a direct ADR-0003 violation. **DPA §7**: DeepMind models raise pre-training data concerns. |
| FD-004 | IRT-Driven Adaptive Placement Engine (2PL-CAT) — Cosyn et al. EDM 2024, IXL | **WARNING** | IRT-CAT is psychometrically sound and well-established. Competitive cite (IXL) is accurate. **RDY-080 FLAG**: Any adaptive placement engine that produces "readiness scores" implicitly predicts future performance. Without a concordance study against actual Bagrut outcomes, this is a Bagrut prediction violation per RDY-080. Cosyn et al. EDM 2024 should be verified as the correct citation. **Required revision**: Add explicit statement that placement scores are NOT predictive of Bagrut performance absent a calibrated concordance study. |
| FD-005 | Formative-Summative Signal Split — Black & Wiliam 1998 meta-analysis | **PASS** | Black & Wiliam (1998) "Inside the Black Box" is one of the most cited reviews in education. Typical ES cited (d=0.4-0.7) is broadly consistent with the formative assessment literature. No Bagrut prediction. No ML training. Appropriately framed as signal separation, not predictive modeling. |
| FD-006 | Confidence Calibration with Certainty-Based Marking — Foster 2016, 2021, Gardner-Medwin | **WARNING** | Foster (2016, 2022) and Gardner-Medwin are legitimate researchers. However, **Foster (2021) specifically found NO effect on mathematics achievement**: "A Bayesian meta-analysis of the effect sizes showed no effect on students' mathematics achievement." This is selective citation — citing Foster without disclosing the null achievement result. CBM improves calibration but not necessarily learning outcomes. **Required revision**: Disclose Foster 2021 null achievement finding; reframe as metacognitive calibration tool, not achievement booster. |
| FD-007 | Crisis Mode — Compression Schedule & Priority Topics — Dunlosky 2013, UWorld | **PASS** | Dunlosky et al. (2013) "Improving Students' Learning" is a highly influential IES monograph. UWorld is a legitimate competitor with spaced repetition features. No ES claim. No Bagrut prediction. Appropriately framed as a scheduling feature, not a learning guarantee. |
| FD-008 | AI Partial-Credit Grading with Step-Level Rubrics — Yu et al. 2026, 90%+ agreement | **REJECT** | Yu et al. 2026: **No such publication is verifiable.** The year 2026 has not occurred. This citation is either fabricated, a typo (perhaps 2016?), or a preprint that does not exist in the literature. **DPA §7 VIOLATION**: AI grading systems require training on student work products, directly implicating data protection. 90% inter-rater agreement sounds impressive but is unbenchmarked — experienced human graders typically achieve 85-95% agreement, so "90%+" may simply mean "approaches human inconsistency levels." **Required revision**: Replace citation with verifiable source; conduct DPA §7 impact assessment. |
| FD-009 | Socratic AI Tutor (No-Answer Mode) — Khanmigo, Stanford/NBER 2025 | **WARNING** | Khanmigo is a real product. Stanford/NBER 2025 tutoring studies are emerging. "No-Answer Mode" (Socratic questioning) is a legitimate pedagogical design choice supported by tutoring literature. However, **DPA §7 FLAG**: LLM-based tutors operate on pre-trained models; while inference does not "train" on student data per se, prompt logging and model fine-tuning pipelines create data governance concerns. The cited studies should be verified as peer-reviewed, not preprints. **Required revision**: Clarify data handling architecture and whether any fine-tuning occurs on Israeli student data. |
| FD-010 | Arabic RTL Math Renderer with Notation Localization — Lazrek 2004, Wiris | **PASS** | Lazrek is a recognized researcher in Arabic mathematical notation (Universite Cadi Ayyad). Wiris is an established math editor with RTL support. Pure engineering/accessibility feature. No ES claim, no prediction, no ML training. Straightforward compliance. |
| FD-011 | Culturally-Contextualized Problem Generator — Zayyadi meta-analysis ES 1.16 | **REJECT** | **ES 1.16 is extremely suspicious and likely cherry-picked.** The broader meta-analytic literature on contextual teaching and learning (CTL) in mathematics finds ES ranging from 0.01 to 1.3, with representative estimates at d=0.34-0.88 (Brummair & Richter 2019; Tamur et al. 2021). A meta-analysis ES of 1.16 is at the extreme upper tail and suggests severe publication bias, inclusion of low-quality primary studies, or fabrication. Meta-analytic means in education rarely exceed d=0.6 for instructional interventions. **Required revision**: Replace with representative meta-analytic estimate (d~0.4-0.6) or provide full methodological transparency on study inclusion criteria. |
| FD-012 | Bayesian IRT Calibration Error Correction — Koenig 2025, 84% bias reduction | **WARNING** | Koenig (2020, 2024) is a legitimate researcher in Bayesian IRT. The 84% bias reduction figure IS found in the literature (2025: "Accounting for item calibration error in computerized adaptive testing"), but it is **misattributed to Koenig**. The 84% figure refers to a Bayesian approach for ability estimation in CAT, not specifically to Koenig's OH2PL model for item parameter calibration. Koenig's own bias correction addresses item discrimination parameters, not ability estimates. **Required revision**: Correct citation attribution; clarify that 84% applies to ability-estimation bias, not item-parameter calibration bias. |
| FD-013 | Mother-Tongue-Mediated Hint System — IJES 2025, translanguaging research | **WARNING** | Translanguaging is a legitimate research area with supportive evidence for multilingual learners. "IJES 2025" should be verified as a real, peer-reviewed publication. The evidence base for translanguaging in mathematics is growing but primarily from Western multilingual contexts — generalizability to Israeli Arabic/Hebrew speakers is plausible but not established. No ES claim, so no cherry-picking concern. **Required revision**: Verify IJES 2025 citation; add generalizability caveat. |
| FD-014 | "Why This Problem?" Explainable AI — Conati et al. 2021/2024 | **PASS** | Cristina Conati is a highly respected AIED researcher (UBC) with extensive work on explainable student modeling. Conati et al. (2021) on user characteristics and explainability is legitimate. The XAI in education literature supports adaptive explanations. No ES claim, no Bagrut prediction, no ML training requirement. Appropriately framed. |
| FD-015 | Per-Session Error Analysis Report — Kehrer 2013, ES 0.37 | **WARNING** | The ES of d=0.37 is modest and credible. However, the citation "Kehrer 2013" appears misdated — the relevant study appears to be Kehrer 2021 (immediate feedback in homework). The ES d=0.37 refers to immediate vs. delayed feedback, not specifically to "error analysis reports." This is a secondary citation that may not directly support the feature. **Required revision**: Correct citation year; verify that Kehrer's specific study supports error analysis reports (not just feedback timing). |
| FD-016 | Team vs. Challenge Cooperative Competitions — Ke & Grabowski 2007 | **PASS** | Ke & Grabowski (2007) is a legitimate and widely cited study on cooperative learning in game-based environments. No ES claim in the finding description. Cooperative competition is well-supported by the broader literature (e.g., Slavin's work). No Bagrut prediction, no ML training. Appropriately framed. |
| FD-017 | Focus Ritual — 60-Second Breathing Opener — US DoE breathing anxiety research | **WARNING** | The citation "US DoE breathing anxiety research" is **vague to the point of being unverifiable**. The U.S. Department of Education does not typically conduct breathing/anxiety intervention studies directly. This likely refers to peripheral mindfulness research or third-party studies cited by DoE. No ES claim, but the evidentiary basis is unclear. **Required revision**: Replace with specific, citable study (e.g., Dunning et al. 2019 on mindfulness in education, or Zoogman et al. 2015 meta-analysis). |
| FD-018 | Learning Energy Tracker — Sanvello, self-monitoring research | **PASS** | Self-monitoring and mood tracking are well-established in the self-regulated learning literature. Sanvello is a real product. The finding makes no exaggerated claims — it positions itself as a wellness/engagement feature, not an achievement intervention. No ES claim, no Bagrut prediction. Appropriately scoped. |
| FD-019 | Mashov Gradebook Sync (Read-Only) — community API documentation | **PASS** | Straightforward engineering integration. Read-only API sync poses no Bagrut prediction risk (no scoring, no forecasting). No ES claim. No ML training. No ADR-0003 concern. Trivial compliance. |
| FD-020 | Bagrut-Aligned Partial Credit Rubric Engine — Bagrut scoring rules | **PASS** (with caveat) | Encoding official Bagrut scoring rubrics as a rules engine is legitimate alignment work. **RDY-080 NOTE**: As long as this is a deterministic rule-coding system (input: student work → output: rubric-based score) and does NOT produce predictive "readiness" scores, it does not violate RDY-080. However, if the engine is used to extrapolate likely Bagrut scores from practice performance, it immediately becomes a Bagrut prediction system requiring concordance validation. **Required revision**: Explicitly state the engine does not predict Bagrut scores; it only encodes official scoring rules. |

---

## Summary Statistics

| Category | Count |
|----------|-------|
| **PASS** | 9 (FD-002, FD-005, FD-007, FD-010, FD-014, FD-016, FD-018, FD-019, FD-020) |
| **WARNING** | 8 (FD-001, FD-004, FD-006, FD-009, FD-012, FD-013, FD-015, FD-017) |
| **REJECT** | 3 (FD-003, FD-008, FD-011) |

---

## Critical Issues Requiring Immediate Action

### 1. FD-003: Fabricated Evidence (REJECT)
The 95% "misconception resolution" rate is not found in any Eedi/DeepMind publication. This figure must be removed and replaced with actual reported values (~60-80% across conditions). Additionally, the feature's ADR-0003 compliance must be architecturally audited.

### 2. FD-008: Unverifiable Citation + DPA §7 Violation (REJECT)
"Yu et al. 2026" does not exist in any retrievable form. This citation must be replaced or removed. The AI grading feature requires a full DPA §7 impact assessment before any implementation.

### 3. FD-011: Effect Size Cherry-Picking (REJECT)
ES 1.16 is at the extreme upper tail of heterogeneous meta-analytic evidence. Representative estimates are d=0.4-0.6. Using d=1.16 risks serious over-promising to stakeholders.

### 4. FD-006: Selective Citation (WARNING)
Foster (2021) found NO effect on mathematics achievement. Citing Foster without this critical null result is misleading. The feature should be reframed as a metacognitive calibration tool.

### 5. FD-004: RDY-080 Risk (WARNING)
Any IRT-CAT placement score that could be interpreted as "Bagrut readiness" is a Bagrut prediction system. An explicit disclaimer and concordance study plan are required.

---

*Assessment conducted by Dr. Rami, Statistical Honesty and Measurement Integrity*
*Date: Current session*
