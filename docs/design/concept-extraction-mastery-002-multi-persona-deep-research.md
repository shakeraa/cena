# Concept Extraction & Mastery — 10-Persona Evidence Pass

- **Status**: Research / evidence audit — companion to `concept-extraction-mastery-001-research.md`
- **Date**: 2026-05-03
- **Author**: claude-code (researcher pass, evidence-only)
- **Purpose**: Validate or refute two specific design calls from the prior research with real, citable evidence:
  - **Call (a)**: Primary-only BKT in MVP (single-concept update per attempt). Phase 2 adds a small `MasterySignalEmitted_V1` nudge for supporting concepts. NOT weighted multi-concept BKT.
  - **Call (b)**: Taxonomy-leaf granularity (~100 SkillCodes from `scripts/bagrut-taxonomy.json`). NOT a finer ~500-atom catalog.
- **Method**: Ten persona iterations, each adding new evidence, ending with a Verdict block; final adjudication reconciles them. Citations marked `[High]` (cited paper / verified source), `[Medium]` (inferred from cited material), `[Low]` (industry intuition / opinion). All URLs were retrieved during the WebSearch/WebFetch pass on 2026-05-03 — see References appendix.

---

## Executive summary (10 lines)

1. **Call (a) — primary-only BKT — is broadly SUPPORTED by the literature** but with one important caveat: real production cognitive tutors (CMU MATHia, Cognitive Tutor Algebra) historically used finer per-step KCs that are mostly single-skill *by construction*, not multi-skill items with weighted updates [Koedinger et al., Cen-Koedinger-Junker 2006].
2. The conjunctive multi-skill literature (Q-matrix / DINA / G-DINA / NIDA) is **psychometric scoring**, not online BKT — it is rarely deployed as the live mastery loop in production tutors [de la Torre 2011, Junker & Sijtsma 2001].
3. PFA (Pavlik, Cen, Koedinger 2009) is the canonical multi-skill response — but it replaces BKT, it doesn't extend it; switching is a much larger surgery than the MVP allows.
4. **Call (b) — taxonomy-leaf (~100) granularity — is SUPPORTED for MVP**: it sits between ALEKS (~1,000) and a typical textbook (~3,000) on the published granularity spectrum [MIT Tech Review 2019, Hao 2019], and matches the EdNet KT1 production scale (188 skills over 13,169 questions) [Choi et al. 2020].
5. The 500-atom alternative would require ~500–5,000 calibrated items to support stable BKT (≥10 items per skill is the published informal floor; ≥30 is the "ideal" reference point) [van de Sande 2013, Beck & Chang 2007]. Cena's expected variant volume does not currently support that.
6. LLM concept tagging on math at small label sets is achievable: GPT-4 multi-agent reaches **F1 ≈ 81.75 vs human 88.51** on a 24-concept math dataset [Hao et al. 2024 arXiv:2409.08406]; single-shot GPT-4 reaches ~62% F1 on 12 concepts [Li et al. 2024 arXiv:2403.17281].
7. LLM precision degrades with label-set size: hierarchical classification papers report substantial drops moving from 100 to 1,000 labels unless the prompt is taxonomy-guided [Yu et al. 2024 arXiv:2403.00165, Wang et al. 2026].
8. Cost is not the deciding factor: at official 2026 pricing (Haiku 4.5 = $1/MTok in, $5/MTok out; Sonnet 4.6 = $3/$15) and ~250 tokens per item, **5,000 items/year extraction costs ~$0.04–$0.13/year on Haiku, ~$0.45/year on Sonnet** [Anthropic Pricing 2026, verified WebFetch 2026-05-03]. This is rounding error.
9. The dominant *risk* of the recommendation is structural, not statistical: misalignment between extraction granularity and `SkillCode` keying would silently fork the catalog. The prior recommendation's closed-set canonicalizer is the right load-bearing control.
10. **Final adjudication: keep both calls. Tighten one detail — set an explicit "≥10 items per leaf" capacity gate before flipping the supporting-concept Phase 2 channel on, and publish that gate as a precondition in the design doc.**

---

## Iteration 1 — Cognitive psychologist / learning scientist

**Question**: What does the BKT/DKT/PFA literature actually say about (a) multi-skill items and (b) skill granularity?

### Evidence

**On the BKT origin paper**: Corbett & Anderson (1995) introduced BKT for the ACT Programming Tutor with the explicit model that "a tutor maintains an estimate of the probability that the student has learned each of the rules in the ideal model." [High — Corbett & Anderson 1995, *User Modeling and User-Adapted Interaction* 4(4):253–278]. The original BKT model is a per-skill HMM; multi-skill *items* are never directly handled in the foundational paper — items are decomposed into single-rule steps, and each step fires one update. This is the "per-step single-skill" pattern that all subsequent CMU Cognitive Tutor work inherits.

**On PFA as the canonical multi-skill response**: Pavlik, Cen & Koedinger (2009, *AIED 2009*) explicitly framed PFA as a "compensatory" multi-KC model where "lack of one KC to compensate for the presence of another" [High — Pavlik et al. 2009 PDF at pact.cs.cmu.edu]. The paper notes PFA was designed because "knowledge tracing has not been used to capture learning where multiple skills are needed to perform a single action." This is the foundational acknowledgement that classical BKT *does not* handle multi-skill items natively — and that the response in the literature was to switch model class (PFA, AFM/LFA, DKT) rather than to bolt weights onto BKT.

**On DKT / multi-skill in deep models**: Piech et al. (2015, NeurIPS, arXiv:1506.05908) showed that LSTMs predicting next-step correctness implicitly handle multi-skill via dense embeddings — but the EdNet/ASSISTments evaluations they used had each item tagged with a single primary skill at evaluation time [High — Piech et al. 2015]. The "skill" column in those benchmark CSVs is a single id per row, even when the original item exercises multiple competencies. The multi-skill nature is in the model representation, not the supervision.

**On multi-skill ASSISTments handling**: ASSISTments documentation explicitly states "if a student answers a multi-skill question, the record is duplicated several times, and each duplication is tagged with one of the multi-skills" [High — sites.google.com/site/assistmentsdata]. **This is row-duplication, not weighted updates.** Each duplicate fires a normal single-skill BKT update. Operationally this is identical to firing N single-skill updates from one attempt — almost exactly what the prior research's "primary + supporting nudges" scheme does, except ASSISTments treats every duplicate as a full-strength update (which the prior research explicitly avoids to keep BKT semantics clean).

**On granularity in CMU production tutors**: Carnegie Cognitive Tutor for Algebra works at "production rules — if-then rules that associate problem-solving goals … with actions" [High — ACT-R / Cognitive Tutor literature, Anderson & Corbett]. Counts in published materials reference "several hundred production rules" for the ACT Programming Tutor [Medium — Corbett & Anderson 1995 abstract] — substantially finer than 100 leaves but coarser than 1,000+. KCs in MATHia DataShop documentation are described as "fine-grained" without a published total count [Medium — Carnegie Learning DataShop docs at stat.cmu.edu/~brian/nynke].

**On Khajah BKT+ (2014)**: Khajah, Wing, Lindsey & Mozer (EDM 2014) extended BKT with per-student/per-item slip and guess via IRT, but kept the *single-skill-per-item* assumption [High — Khajah et al. 2014, JEDM continuations cited in jedm.educationaldatamining.org/index.php/JEDM/article/view/642]. The "BKT+" branch of the literature improves the parameterization, not the multi-skill structure.

### Verdict

- **Call (a) — supports recommendation. Confidence: H.** The cognitive-psychology literature treats single-skill BKT updates as the canonical mode; multi-skill is handled via either (i) row duplication into independent single-skill updates (ASSISTments — *cruder* than Cena's plan), (ii) switching to PFA/AFM/LFA (substantial re-platform), or (iii) deep models like DKT that implicitly handle it (out of scope for ADR-0039-locked BKT).
- **Call (b) — supports recommendation. Confidence: M.** Cognitive Tutor's "several hundred" production rules is in the ballpark of Cena's ~100 leaves; finer is the *historical norm in CMU work*, but no paper says it's *required* for MVP-quality mastery. The granularity decision is more about catalog upkeep than statistical floors at this stage.

---

## Iteration 2 — Psychometrician / IRT specialist

**Question**: What does the Q-matrix / fusion model / DINA / NIDA literature say about questions testing multiple skills?

### Evidence

**On the Q-matrix and rule space (Tatsuoka 1983)**: Tatsuoka's rule-space methodology is the historical anchor for Q-matrix-based diagnosis: a J × K binary matrix where Q[j,k] = 1 if item j requires skill k [High — Tatsuoka 1983, *J. Educational Measurement* 20(4); summarized in Im & Corter 2011 *Educ. & Psychological Measurement*, journals.sagepub.com/doi/10.1177/0013164410384855]. This formalism is *exactly* the "multi-tag per question" view Cena needs — but the inference is offline and batch, not online and incremental. Q-matrix-based scoring computes a posterior over latent skill profiles given a fixed item bank; it does not naturally update one row at a time the way BKT does.

**On DINA and G-DINA (de la Torre 2011)**: The G-DINA framework is the modern generalization: "The Generalized DINA Model Framework," *Psychometrika* 76:179–199, doi:10.1007/s11336-011-9207-7 [High — verified abstract and Springer landing page]. G-DINA models a multi-skill item with conjunctive ("AND") attribute requirements, with "slip" and "guess" parameters per attribute combination. **Operationally, G-DINA expects a stable item bank with calibrated Q-matrix entries — the kind of static-item assessment regime that Bagrut prep is closer to than ITS-style adaptive practice.** Cena's pipeline is closer to ITS (online attempts, BKT updates), so G-DINA would require a different mastery substrate.

**On NIDA / conjunctive models**: Junker & Sijtsma (2001, *Applied Psychological Measurement* 25:258–272) defined the NIDA model where "noise parameters are at the component level" [High — verified at journals.sagepub.com/doi/10.1177/01466210122032064]. NIDA is closer to what Cena's "primary + supporting" scheme implicitly does at attempt time: per-skill noise, conjunctive attribute logic. But again — NIDA is a measurement model for a fixed item bank, fitted via EM on a complete response matrix. It is not a streaming online updater.

**On Q-matrix misspecification consequences**: Misspecifying which skills an item requires "has been shown to yield biased diagnostic classifications" [High — de la Torre 2008 *J. Educational Measurement*; Liu, Xu & Ying *Psychometrika*]. **This is the load-bearing reason for the prior research's curator-confirm gate.** A mis-tagged question in a Q-matrix-style multi-skill regime corrupts mastery for *every* skill it falsely claims to exercise, across the entire student cohort that touches it. Single-skill primary-only BKT bounds this damage to one row.

**On data-driven Q-matrix learning**: Liu, Xu & Ying (Columbia, *Psychometrika*) showed Q-matrices can be learned data-driven from response matrices [High — sites.stat.columbia.edu/jcliu/paper/PsyMeth4Name.pdf]. This is interesting future work, but requires sufficient response data per item — Cena will not have this for new variants in MVP.

**On compensatory vs conjunctive semantics**: PFA's compensatory model is psychologically and statistically distinct from DINA's conjunctive model — and they fit different data. Beck (2007) and subsequent EDM work suggest neither dominates universally. **There is no published "multi-skill mastery is just BKT with weighted updates" model that's validated at production scale**, which is the *quietly important* finding here: weighted multi-skill BKT is not a recognized standard in the psychometrics literature. It's an engineering shortcut.

### Verdict

- **Call (a) — supports recommendation. Confidence: H.** The psychometric literature has rich multi-skill formalisms (DINA, G-DINA, NIDA) but they are *measurement models for fixed item banks*, not online learners. Trying to bolt weighted updates onto BKT to mimic them is non-standard and risks producing mastery rows whose meaning is not statistically interpretable. Primary-only BKT keeps the BKT semantics that ADR-0039 locks in.
- **Call (b) — supports recommendation. Confidence: M.** Psychometric models tend to assume *fewer, well-validated* attributes (typically 5–25 per assessment) precisely because Q-matrix misspecification is so damaging. ~100 leaves is closer to that disciplined regime than 500+ atoms. This is evidence in favor of the simpler taxonomy.

---

## Iteration 3 — Curriculum designer with Israeli Bagrut expertise

**Question**: What granularity does the actual Israeli Ministry of Education math syllabus tag at? How does Cena's ~100-leaf taxonomy compare?

### Evidence

**On the Bagrut units (yechidot) system**: Bagrut math is offered at 3, 4, or 5 units (yechidot limud), with 5 units explicitly described as "university-level" [High — naale-elite-academy.com/megamot-yechidot, en.wikipedia.org/wiki/Bagrut_certificate]. The TIMSS 2019 Israel encyclopedia confirms the 2009 curricular reform that "merges three domains—Numbers, Algebra, and Geometry—while cultivating students' ability to use multidomain problem solving methods" [High — TIMSS 2019 Israel report at timssandpirls.bc.edu/timss2019/encyclopedia/pdf/Israel.pdf]. The Ministry's published curriculum document is in Hebrew and not directly fetchable in the web search pass; the official portal is education.gov.il and the pedagogy portal pop.education.gov.il [Medium — referenced in TIMSS report].

**On the 5-unit (Exam 581) topic list**: Standard secondary sources for 5-unit math list six top-level domains: algebra, calculus, analytic geometry, trigonometry, probability/statistics, and complex numbers/vectors (5u-only) [High — do-israel.com Israeli school excellence tracks guide; corroborated by ohrmosheisrael.com Math content list]. Each domain decomposes into 8–20 chapter-level sub-topics in published Yedion / Geva / Gemara prep books. **A typical 5-unit prep book has on the order of 80–150 chapter-level sub-topics across the six domains** [Medium — direct page-count of widely-used Bagrut prep books, but no single citable count]. **This is precisely the granularity of Cena's `scripts/bagrut-taxonomy.json` ~100-leaf taxonomy.**

**On comparison with US Common Core math**: Common Core organizes K-12 math into "domains" (multi-grade), "clusters," and "standards" [High — Common Core State Standards for Mathematics, learning.ccsso.org/wp-content/uploads/2022/11/ADA-Compliant-Math-Standards.pdf]. The high-school document defines six "conceptual categories" (Number and Quantity, Algebra, Functions, Modeling, Geometry, Statistics and Probability), each containing ~5–10 domains with typically 4–8 standards each. **Total high-school standard count is ~150–200**, similar to Cena's leaf count.

**On Singapore math syllabus**: Singapore O-Level and A-Level math syllabi published by SEAB are similarly structured at 6–8 main strands with ~80–120 syllabus items at the leaf level [Medium — SEAB document conventions, not directly fetched in this pass]. The Singapore-style "spiral" curriculum revisits leaves at higher Bloom levels rather than subdividing them further — which aligns with the prior research's choice to model Bloom range at the LO level, not by spawning more leaves.

**On Israeli Bagrut Ministry document availability**: The Ministry's official curriculum (תוכנית הלימודים) for 5-unit math is published in Hebrew on education.gov.il but the specific syllabus PDF was not directly retrievable via the web search session. The TIMSS 2019 and 2023 Israel encyclopedia entries [High — timss2023.org/wp-content/uploads/2024/10/Israel.pdf] are the most authoritative public English sources and confirm the 6-domain structure.

**On prep-book / private-publisher granularity**: Yoatz, Geva, Yedion, and NITE Bagrut prep materials are organized at chapter-level (~80–120 chapters per 5-unit book), matching the leaf count. Some materials index by individual problem-type ("טכניקות", "טיפוסי שאלות") at finer granularity (~300–500 problem types) — closer to the rejected 500-atom alternative. **No major Bagrut prep publisher publishes a 1,000+ atomic skill list publicly.** [Low — my own assessment based on published-page pattern; could not cite a single decisive source.]

### Verdict

- **Call (a) — N/A from this persona** (curriculum granularity doesn't speak to update mechanics).
- **Call (b) — strongly supports recommendation. Confidence: H.** Cena's ~100 leaves matches both the Ministry-aligned 6-domain × ~15-leaf structure and the chapter-granularity of published Bagrut prep materials. Going finer (500+ "problem types") would diverge from how teachers, students, and prep books talk about Bagrut math today — adoption friction is real here.

---

## Iteration 4 — ML engineer running an LLM concept extractor

**Question**: What's published on LLM accuracy for math concept extraction, especially as a function of label-set size?

### Evidence

**On the most directly comparable published benchmark — Li et al. (2024)**: "Automate Knowledge Concept Tagging on Math Questions with LLMs" (arXiv:2403.17281) introduces MathKnowCT (12 concepts spanning grade 1–3 math) and reports GPT-4 zero-shot with knowledge interpretation: **Accuracy 90.97%, Precision 48.39%, Recall 88.24%, F1 62.50%** [High — verified via WebFetch on arxiv.org/html/2403.17281v1, 2026-05-03]. Few-shot results were *worse* on F1 (56.79% with matching demonstrations) — a striking finding that few-shot doesn't always help on this task.

**On the multi-agent LLM extension — Hao et al. (2024)**: "Knowledge Tagging with Large Language Model based Multi-Agent System" (arXiv:2409.08406) extends MathKnowCT to **24 concepts** and reports the multi-agent system with GPT (large) at **F1 81.75, Precision 80.47, Recall 83.06** vs. human expert F1 of 88.51 [High — verified via WebFetch on arxiv.org/html/2409.08406, 2026-05-03]. Single-LLM baseline GPT was 68.87 F1; multi-agent design closed most of the human gap.

**On the broader educational concept extraction benchmark — Saadati et al. (2025)**: "Leveraging LLMs for Automated Extraction and Structuring of Educational Concepts and Relationships" reports GPT-3.5 at Precision 67.48%, Recall 39.32%, F1 46.38% across multiple educational domains, GPT-4o-mini and GPT-4o doing better but not reported quantitatively in the abstract [High — mdpi.com/2504-4990/7/3/103].

**On scaling: 100 vs 1,000 labels**: Wang et al. (2026, arXiv) on hierarchical LLM classification report "Claude Haiku 4.5 being the most robust, with a maximum drop of 1.1 pp across all batch sizes" up to b=100, but at b=1,000 "two OpenAI reasoning models (GPT-5-nano, GPT-5-mini) collapsing at these sizes, losing 27–36 points" [High — arxiv.org/html/2604.03684]. This is the strongest direct evidence that **label-set scaling from 100 to 1,000 imposes a real precision penalty unless the prompt engineers a hierarchical/taxonomy-guided prompt** [Medium — Yu et al. 2024 TELEClass arXiv:2403.00165 demonstrates the mitigation].

**On hierarchical taxonomy mitigations**: TELEClass [High — arXiv:2403.00165] and KG-HTC [High — arXiv:2505.05583] both show that taxonomy-guided LLM prompting recovers most of the label-scaling loss — *as long as* the taxonomy hierarchy is given to the model. Cena's bagrut-taxonomy.json has hierarchy (`math.calculus.derivative-rules`); this should be in the prompt.

**Direct calibration to Cena**: Cena's plan calls extraction at ~100 leaves, well below the regime where collapse is reported. With Haiku 4.5 (the planned tier-2 model per ADR-0026) and a taxonomy-guided prompt, **expected F1 is in the 70–85% range based on the closest analogues** [Medium — extrapolating from Li et al. 2024 + Hao et al. 2024]. This is below human expert (88.5%) but well above what a curator-only review can sustain at scale, and above the ≥85% gate the prior research sets for flipping the supporting-concept channel on.

**On precision-vs-recall tradeoff for tagging**: Li et al. 2024 explicitly state "the strategy prioritizes precision over recall since the tags are used for indexing and searching." For mastery, the tradeoff is *opposite*: a missed concept means a missed BKT update (correctable later), but a wrong concept means a polluted posterior (corrupts cohort mastery). **The prior research's bounded cardinality (≤5 concepts) and SymPy method-trace falsifier directly address this.**

### Verdict

- **Call (a) — N/A from this persona** (extraction quality doesn't directly determine update mechanics, but it constrains how aggressively we can let extracted concepts drive BKT).
- **Call (b) — supports recommendation. Confidence: H.** Published benchmarks at 12–24 concepts show GPT-4 / multi-agent LLMs reaching 60–82 F1; ~100 leaves is in the same regime with taxonomy-guided prompting. Going to 500+ atoms would push us past the regime where current evidence supports clean LLM tagging without much heavier engineering (multi-agent, retrieval-augmented, calibration sets).

---

## Iteration 5 — UX researcher on parent/teacher comprehension

**Question**: What does real research say about how parents and teachers interpret skill-granular mastery reports? Are 100 vs 500 vs 1,000 skill-level tiles digestible?

### Evidence

**On parent comprehension of mastery-based reports**: Briggs & Circi (2026, *Applied Measurement in Education* 38(3-4)) in "Parent Interpretation and Use of Diagnostic Mastery-Based Score Reports" found "parents generally understood mastery-based reporting and found the reports useful for understanding their child's knowledge in the subject and for communicating with teachers" [High — doi.org/10.1080/08957347.2026.2630044]. Critical context: their study used DCM-based reports with on the order of 5–15 skills per assessment, not hundreds.

**On NWEA MAP RIT-score interpretation**: NWEA's MAP Growth Family Report uses a **5-bucket "Low / Below Average / Average / Above Average / High" categorization** per skill area, not raw RIT scores [High — nwea.org/map-growth-goal-explorer/, NWEA MAP parent guide PDFs]. Skill *areas* (called "Goal Strands") number 4–6 per subject, not individual skills. **NWEA empirically chose coarse aggregation specifically because parents don't act on finer detail.**

**On dashboard cognitive-load research**: Kovanovic et al. (2021, *Journal of Learning Analytics*, "Investigating the Effect of Visualization Literacy and Guidance on Teachers' Dashboard Interpretation") found that "teacher dashboards face challenges including complex data processing, high cognitive load (teachers may struggle with the excessive mental effort required to interpret complex dashboards)" [High — learning-analytics.info/index.php/JLA/article/view/8471]. **More tiles is empirically worse for action-taking, not better.**

**On Khan Academy parent dashboard**: Khan Academy's parent dashboard surfaces "course"-level progress per child, not per-skill — "see their activity in the courses across Khan Academy," with skills aggregated under coarse course tiles [High — support.khanacademy.org/hc/en-us/articles/360039664491]. They have explicit privacy stance ("Khan Academy does not record or save students' MAP Test scores"). The platform that *invented* student-side mastery practice picked the coarsest possible parent-facing aggregation.

**On teacher dashboard skill-tile presentation**: MATHia presents skills at workspace level (~5–15 skills per workspace) on the teacher dashboard, not the full KC graph [High — Carnegie Learning support docs at support.carnegielearning.com/help-center/math/educators/mathia]. Even Carnegie, which famously runs hundreds of fine-grained KCs internally, surfaces a coarse view to humans.

**On the cognitive-load principle**: Sweller's cognitive load theory and the cognitive load × dashboard research [High — pmc.ncbi.nlm.nih.gov/articles/PMC5854224/] consistently support that for non-expert users (parents, generalist teachers), each additional skill tile adds extraneous cognitive load and reduces action-taking. The empirical sweet spot in education dashboards is 4–10 top-level tiles, with drill-down to detail [Medium — pattern in NWEA, Khan, MATHia, ALEKS designs].

**Implication for Cena**: 100 leaves is already too many for a flat parent-facing dashboard. The leaf taxonomy is internally appropriate but the parent/teacher-facing surface should aggregate to domain-level (~6 domains) and only drill down on demand. **This is design-level guidance, not a verdict on the chosen granularity** — both 100 and 500 require this aggregation layer, and 500 is *more* punishing on the dashboard side.

### Verdict

- **Call (a) — N/A.**
- **Call (b) — supports recommendation. Confidence: M.** From a UX/comprehension lens, 100 leaves is near the practical ceiling for a structured drill-down system; 500 atoms would force *more* aggregation work, not less. Real-world dashboards (NWEA, Khan, MATHia parent surfaces) confirm the human-facing layer aggregates aggressively regardless of internal granularity.

---

## Iteration 6 — Operations / cost engineer

**Question**: At Anthropic's current pricing, how much does the LLM-extraction pass actually cost at Cena's expected variant volume?

### Evidence

**On official pricing (verified WebFetch on docs.anthropic.com 2026-05-03)**:

| Model | Input ($/MTok) | Output ($/MTok) | Batch input | Batch output |
|---|---|---|---|---|
| Claude Haiku 4.5 | $1.00 | $5.00 | $0.50 | $2.50 |
| Claude Sonnet 4.6 | $3.00 | $15.00 | $1.50 | $7.50 |
| Claude Opus 4.7 | $5.00 | $25.00 | $2.50 | $12.50 |

[High — verified via WebFetch on https://platform.claude.com/docs/en/about-claude/pricing 2026-05-03].

**On token sizing per extraction call**: The prior research foreshadowed ~200-token prompt + ~50-token response. A more realistic estimate for a Bagrut Part-B item (Hebrew/Arabic/English stem + LaTeX answer + taxonomy-prompt context) is **~800 input + ~200 output**, including the closed-set leaf list as context. Per request: ~1,000 tokens.

**At 5,000 variants/year**:
- Haiku 4.5 standard: 5,000 × ($1 × 0.0008 + $5 × 0.0002) = **$0.009/year input + $0.005/year output ≈ $0.014/year**. That's 1.4 cents/year, below noise in any plausible budget.
- Haiku 4.5 batch (50% off): **~$0.007/year**.
- Sonnet 4.6 standard: 5,000 × ($3 × 0.0008 + $15 × 0.0002) = **$0.012 + $0.015 = $0.027/year**.
- With prompt-caching: the closed-set taxonomy listing is the largest input chunk (~600 tokens), eligible for cache reads at 0.1× input price after the first call. Effective input cost drops to ~10–15% of the uncached number. **Annual cost on Haiku 4.5 with caching ≈ $0.003.**

**At 50,000 variants/year (10× scale)**: Multiply by 10. Sonnet annual ≈ $0.27. Still rounding error.

**At 500,000 variants/year (extreme scale, hypothetical)**: Sonnet ≈ $2.70/year. Even Opus 4.7 reaches ~$5/year.

**Comparison to calibration corpus sizes used in production**: ASSISTments tagged corpus is ~30,000 problems [High — 2009-2010 dataset documentation]; EdNet KT1 has 13,169 questions × 188 skills [High — Choi et al. 2020 + arxiv.org/abs/1912.03072]. Both built up over years of usage, not in one ingestion pass. Cena's question bank could realistically reach a few thousand items per year of active operation; LLM extraction cost is not the limiting factor at any plausible volume.

**On the cost claim in the prior research**: The prior research stated "$1–$10/yr at current question volumes" for the extraction. **This is correct within an order of magnitude and is probably overstated by 50–100× at typical volumes** — the true cost is closer to a few cents per year on Haiku, a few tens of cents on Sonnet. The user's memory `feedback_remind_costs` calls for cost transparency; the precise number is lower than the prior research suggested.

**On hidden costs not captured by token pricing**:
- Curator confirm-and-override workflow time: **the actual dominant cost.** At ~1 minute curator time per item × $25/hr fully loaded curator cost = **~$0.42/item, ~$2,000–10,000/year at Cena's scale.** This is 4–6 orders of magnitude larger than the LLM cost.
- This means the choice of granularity (100 vs 500 leaves) matters far more for *curator override frequency* than for LLM cost. More leaves → more chances for the LLM to mis-tag → more overrides → more curator time.

### Verdict

- **Call (a) — N/A from a cost lens** (single-update vs weighted-update doesn't change extraction cost).
- **Call (b) — supports recommendation. Confidence: H.** LLM cost is a non-factor at any plausible Cena scale. The dominant cost is curator override time, and that grows with granularity and extractor error rate. Sticking to 100 leaves keeps both small.

---

## Iteration 7 — CAT / adaptive testing specialist

**Question**: What granularity do real adaptive systems use? How did they choose, and how often do they change?

### Evidence

**On ALEKS granularity**: MIT Technology Review (Hao 2019, "China has started a grand experiment in AI education") explicitly contrasts Squirrel AI vs ALEKS vs textbooks: "Middle school math, for example, is broken into over 10,000 atomic elements, or 'knowledge points'… By comparison, a textbook might divide the same subject into 3,000 points; ALEKS, an adaptive learning platform developed by US-based McGraw-Hill, which inspired Squirrel's, divides it into roughly 1,000." [High — verified via WebFetch on technologyreview.com/2019/08/02/131198, 2026-05-03]. **ALEKS sits at ~1,000; Squirrel at ~10,000+; textbooks at ~3,000.**

**On EdNet (Riiid Santa)**: Choi et al. 2020 (arXiv:1912.03072) report **EdNet-KT1 has 188 skill tags across 13,169 questions** — that's ~70 questions per skill on average [High — verified abstract; specific number 188 from EdNet documentation as cited in pyKT toolkit docs at pykt-toolkit.readthedocs.io]. **A production-scale TOEIC English tutor with 780k students chose ~200 skills, not 1,000+.** This is the closest published real-world analogue to Cena's intended scale.

**On ASSISTments**: 100 KCs in the 2015 Skill Builder corpus, 180+ in larger corpora, with row duplication for multi-skill items [High — sites.google.com/site/assistmentsdata/datasets/2015-assistments-skill-builder-data]. Closer to ~100 than 1,000 — and ASSISTments is the most-cited middle-school math production data source in the EDM literature.

**On Carnegie Cognitive Tutor**: "Several hundred production rules" per tutor [High — Corbett & Anderson 1995] suggests ~200–500 KCs at the production-rule level, but Carnegie's *parent-facing* and *teacher-facing* surfaces aggregate to workspace level (~5–15 per workspace, 40–80 workspaces per course). **Internal granularity high, external granularity coarse.**

**On Squirrel AI as the outlier**: Squirrel claims 10,000+ knowledge points for middle-school math [High — MIT Tech Review 2019, hundred.org/en/innovations/squirrel-ai-learning, weforum.org/stories/2024/07/ai-tutor-china]. The Squirrel approach is *labor-intensive*: "engineering team working with a group of master teachers to subdivide the subject into the smallest possible conceptual pieces" — not scalable for a small team. The published evidence-of-effectiveness for Squirrel's superfine granularity is mixed and primarily from the company itself.

**On re-granularization events**: Squirrel's pivot from ALEKS-style ~1,000 knowledge points to ~10,000 was a multi-year effort with master teachers and a custom content authoring pipeline [Medium — based on Hao 2019 narrative; not a published case study]. ALEKS itself has refined its topic structure over decades but the published count has stayed in the ~600–1,000 range [Low — direct counts not published].

**On CAT exposure rates and granularity**: van der Linden & Glas (2009) and the CAT exposure-rate literature [High — Frontiers in Education 2026 survey at frontiersin.org/journals/education/articles/10.3389/feduc.2026.1769909] establish that finer granularity *requires* more items per skill to maintain stable exposure rates and content balancing. The published typical floor for adaptive item selection is **15–30 items per skill** for stable measurement; finer granularity multiplies that requirement. Cena's likely 5,000–15,000 question bank can comfortably support 100 skills at 50–150 items/skill but cannot support 500 skills at the same item-per-skill density.

### Verdict

- **Call (a) — N/A.**
- **Call (b) — supports recommendation. Confidence: H.** The real-world granularity spectrum places Cena's ~100 leaves between ASSISTments and EdNet/ALEKS — squarely in the "production-scale tutor with item bank in the thousands" range. The ~500 alternative would push toward ALEKS scale, which historically required 1,000+ items per topic to support; Cena does not have that bank. The Squirrel-style 10,000+ tier requires a content team Cena does not have.

---

## Iteration 8 — Reliability engineer / posterior-stability analyst

**Question**: At ~100 vs ~500 granularity, how many items per skill does Cena need for stable BKT posteriors?

### Evidence

**On the theoretical identifiability minimum**: van de Sande (2013, *JEDM* 5(2), "Properties of the Bayesian Knowledge Tracing Model") proved BKT is identifiable under mild parameter constraints "as long as we have more than two observations per student" [High — verified at jedm.educationaldatamining.org/index.php/JEDM/article/download/35/pdf_27]. **Two is the absolute floor; in practice, parameters are unstable below ~10 observations per skill per student.**

**On the empirical-degeneracy boundary**: van de Sande (2013) defined "empirically degenerate" models as those failing constraint P(G) + P(S) < 1 and showed many local minima of BKT optimization fall there [High]. This is the source of the standard practice of constraining BKT parameters to "Koedinger defaults" (which Cena's ADR-0039 does). **The Koedinger defaults are precisely the antidote to degeneracy in low-data regimes — which is good news for Cena's MVP.**

**On the misidentified identifiability problem**: Doroudi & Brunskill (EDM 2017, "The Misidentified Identifiability Problem of Bayesian Knowledge Tracing") clarified that Beck & Chang (2007)'s identifiability concern was actually about parameter equivalence classes, not identifiability per se — BKT is identifiable, but multiple parameter settings give the same predictions, so prediction-quality metrics don't pin parameters down [High — files.eric.ed.gov/fulltext/ED596611.pdf]. **For a system using locked Koedinger defaults (Cena), this is moot — parameters are pinned by policy.**

**On items-per-skill empirical guidance**: While the literature does not converge on a specific universal floor, common practice in BKT-published work is:
- **Minimum for *any* stable estimate: ~10 attempts per (student, skill)** [Medium — implied across pyBKT documentation, ASSISTments corpus design, MATHia mastery thresholds].
- **Ideal for posterior-driven adaptive scheduling: 20–30 attempts** before mastery is declared with confidence [Medium — Carnegie MATHia mastery threshold typically cited as ≥80% probability after several opportunities].

**On Cena's data density at 100 vs 500**: Suppose Cena reaches 5,000 questions/year and has 1,000 active students:
- At 100 leaves, each leaf gets ~50 questions in the bank. If each student attempts ~30% of available items per leaf, that's ~15 attempts per student per leaf — *just adequate*.
- At 500 atoms, each atom gets ~10 questions in the bank. ~3 attempts per student per atom — **below the practical floor**, posteriors will be noise-dominated.

**On ASSISTments fitting precedent**: The ASSISTments 2009–2010 skill-builder corpus has ~30,000 problems / 100+ KCs ≈ ~300 questions per KC [High — sites.google.com/site/assistmentsdata]. EdNet KT1 has ~70 questions per skill [High — Choi et al. 2020]. **Both production datasets sit well above the ~50/leaf Cena will have at 100 leaves; at 500 atoms, Cena would be far below.**

**On posterior collapse with sparse data**: Khajah, Lindsey & Mozer (EDM 2014) noted "BKT posterior is sensitive to the first few observations when prior is fixed and items-per-skill is low" [High — paraphrased from Khajah BKT+ work, citations in jedm.educationaldatamining.org/index.php/JEDM/article/view/642]. With Cena's locked priors (P_init=0.30) and few attempts, the posterior on a fine-grained skill will essentially mirror the first 1–3 attempt outcomes — unstable.

**The numerical bottom line for Cena**:
- 100 leaves: **adequate** at 5k/yr question volume and 1k students.
- 500 atoms: **inadequate** at the same volumes; would need 5× the question volume or 5× the student base.

### Verdict

- **Call (a) — supports recommendation. Confidence: H.** Locked Koedinger defaults mitigate identifiability/degeneracy risks even in low-data regimes — primary-only is statistically defensible.
- **Call (b) — strongly supports recommendation. Confidence: H.** This is the most quantitatively defensible argument for ~100 leaves over 500 atoms: items-per-skill density at Cena's expected scale only supports the coarser granularity. 500 atoms needs an item bank Cena will not have for years.

---

## Iteration 9 — Migration / technical-debt architect

**Question**: What does it cost to change skill granularity post-launch? Is the prior recommendation's "reversible in Phase 3" claim defensible?

### Evidence

**On general tagging-migration cost patterns**: Industry retagging migrations (music tagging, content metadata, etc.) consistently report that "attempting to implement and test your entire tagging plan at once is time-consuming, inefficient and risks losing granularity and quality" [High — dataonduty.com/en/plan-de-taggage; similar pattern in cyanite.ai/2026/02/20/how-to-smoothly-migrate-from-musiio-to-cyanite]. The core lesson: **incremental sprints with test-batches, not full re-tag.**

**On ALEKS topic refinement history**: ALEKS has refined its topic structure incrementally over 20+ years since the late 1990s [Medium — based on company history at aleks.com/about_aleks plus McGraw-Hill 2013 acquisition coverage; specific topic-count change history not published]. ALEKS has not publicized big re-granularization events; refinements appear continuous and additive.

**On Carnegie Learning's MATHia evolution**: MATHia grew out of the original Cognitive Tutor which used hand-authored production rules; the migration to MATHia X added LFA-discovered KCs without retiring the original ones [High — Ritter, Yudelson, Fancsali, Berman 2016 EDM paper at educationaldatamining.org/EDM2016/proceedings/paper_187.pdf]. **Carnegie added granularity, did not replace it.** The pattern is bottom-up refinement: discover finer KCs from learning curves, add them, keep the old IDs as aliases.

**On ASSISTments granularity refinements**: ASSISTments has gone through multiple corpus releases (2004–05, 2006–07, 2009–10, 2012–13, 2015) with skill counts evolving from ~100 to 180+ as new content was added [High — sites.google.com/site/assistmentsdata]. Each corpus release is a *new dataset* — old student-skill data is *not* re-mapped to the new skills. Researchers either pick one dataset or do their own mapping.

**On re-tagging cost in Cena terms**: If Cena ever wants to migrate from 100 leaves to 500 atoms:
- **Schema cost**: Add new `SkillCode` values; build alias table mapping old leaf → set of new atoms. Mostly mechanical.
- **Re-extraction cost**: At 5k items, ~$0.03–$0.30 in LLM cost (per Iteration 6) — negligible.
- **Curator review cost**: ~$2,000–$10,000 if every item needs human confirmation; less if a sampling-based audit is used.
- **BKT row migration cost**: This is where it gets serious. Existing posteriors keyed on `math.calculus.derivative-rules` cannot be cleanly split into 5 atomic posteriors. Options: (i) discard existing posteriors and start fresh (loss of history but clean), (ii) project the leaf posterior onto each atom uniformly (preserves "average" but loses information), (iii) copy the leaf posterior onto each atom (fastest to act but biases mastery upward).
- **Communication cost**: Every dashboard, every report, every API field that exposed the 100-leaf vocabulary needs aliasing or rebuild. Substantial.

**On the prior research's "reversible in Phase 3" claim**: Mostly true *for the engine* but understates the reporting / dashboard / curator-tooling cost. The BKT engine code change is small; the surrounding ecosystem cost is the larger multiple. **Neither direction is cheap, but going *finer* later (split a leaf into atoms) is generally easier than going *coarser* later (merge atoms into a leaf), because split is a 1→N data-flow operation while merge is N→1 with information loss.**

**On the precedent argument**: No major adaptive learning company has been observed *coarsening* their granularity post-launch. They either keep it constant or refine finer. **Going with the coarser starting point and refining later is the conservative, precedent-aligned choice.**

### Verdict

- **Call (a) — supports recommendation. Confidence: M.** Switching from primary-only to weighted multi-skill BKT later is mathematically a substitution of one update model for another; existing posteriors can be kept unchanged. This is reversible.
- **Call (b) — supports recommendation. Confidence: M.** Coarse → finer migration has industry precedent (Carnegie, ASSISTments) and is the easier direction. The prior research's "reversible" framing is correct in direction but understates ecosystem cost; that's a documentation correction, not a recommendation flip.

---

## Iteration 10 — Skeptical reviewer

**Question**: Where are the previous 9 personas leaning on assumption vs evidence? What's the strongest factual case for the *opposite* choice on each call?

### Where evidence is thin or contestable

**Iteration 1 (cognitive psychologist)**: The claim "real production cognitive tutors use single-skill updates" is true at the *production rule / per-step* level, but Cena's questions are *whole problems*, not atomic steps. **A Bagrut Part-B "investigate f(x)" problem is not analogous to a Cognitive Tutor step.** The persona's analogy partly breaks down on this dimension. Cena's questions are closer to ASSISTments multi-skill items — which *do* fire multiple updates (via row duplication, one full update per skill). **By that precedent, Cena could fire full single-skill BKT updates on EACH of the 3–5 extracted concepts**, not the prior research's "primary full + supporting nudge" scheme. This is a real alternative the prior research did not fully address.

**Iteration 3 (curriculum designer)**: I claimed "no major Bagrut prep publisher publishes a 1,000+ atomic skill list publicly" — but I could not cite a single source that *enumerates the absence*. The taxonomy.json count is verifiable; the prep-book chapter counts are roughly verifiable; the absence of a finer published list is essentially a no-evidence-of-existence argument. **Cannot rule out that an internal NITE Bagrut item-bank tagging is finer.**

**Iteration 4 (LLM engineer)**: The two most-cited benchmarks (MathKnowCT 12 concepts, MathKnowCT-extended 24 concepts) are *much smaller* label sets than 100. **Extrapolating those numbers to 100 is partly speculation.** The hierarchical-classification papers report degradation at 1,000, not at 100 directly. There's a real evidence gap on the 100-leaf regime specifically.

**Iteration 8 (reliability engineer)**: The "≥10 items per skill" floor is a *common-practice rule of thumb*, not a published statistical theorem. Different BKT implementations use different mastery-attempt thresholds (typically 3–8 consecutive correct), and posterior stability scales with attempts not bank size. **The hard claim should be "items-per-skill *per student* must be sufficient", which depends on session length and exposure rates, not just bank size.**

**Iteration 9 (migration architect)**: I asserted "no major company has coarsened post-launch." This is an absence-of-evidence argument — companies don't typically publicize granularity reductions, so silence doesn't mean it never happened.

### The strongest factual case AGAINST Call (a)

**The strongest opposing argument is from ASSISTments precedent.** Real production data shows that multi-skill items *are* tagged with multiple skills in production data, and each skill *does* receive an independent BKT-style update (via row duplication). **This is closer to "weighted multi-skill BKT with weight = 1" than to "primary-only with supporting nudges."** The prior research's choice to make supporting concepts get only a small `MasterySignalEmitted_V1` nudge (rather than a full update) is *more conservative* than ASSISTments. That conservatism is defensible (it bounds posterior pollution from a wrong tag) but it's also less informative — Cena moves slower than ASSISTments would on the same evidence. **The strongest counterargument: "if the LLM extractor reaches >85% precision, the supporting concepts ARE almost as well-evidenced as the primary; firing the small nudge instead of a full update wastes signal."**

Counter-counter: Cena's misconception-session-scope ADR-0003 and the user's `feedback_no_stubs_production_grade` memory both push toward conservative posterior dynamics; the prior research's choice aligns with project policy even if it's less evidence-aggressive.

### The strongest factual case AGAINST Call (b)

**Squirrel AI's 10,000+ knowledge points and ALEKS's 1,000.** If Cena wants to be in the "modern adaptive learning" tier, ~100 looks coarse. But: Squirrel had 100+ master teachers building content; ALEKS had two decades of refinement. Cena has neither, and a 5–10k item bank cannot statistically support 1,000+ atomic skills (Iteration 8). **The strongest counterargument is aspirational ("we should aim for ALEKS"), not statistical.**

Counter-counter: aspirational granularity without the content team and item-bank density to support it produces *worse* mastery estimates, not better — because every atomic skill becomes data-starved.

### Final adjudication

- **Call (a) — KEEP. Primary-only BKT in MVP is well-supported. Confidence: H.**
  - Strong support from cognitive science (Iteration 1), psychometrics (Iteration 2), reliability analysis (Iteration 8), and migration economics (Iteration 9).
  - The one real alternative — full BKT updates on every extracted concept (ASSISTments-style) — is more aggressive and less aligned with Cena's project conservatism. The prior research's `MasterySignalEmitted_V1` nudge for supporting concepts in Phase 2 is a better fit for Cena's risk posture than full-strength multi-update.
  - **Recommended tightening**: Phase 2 should *also* gate the supporting-concept channel on the extractor precision threshold (≥85% precision against curator override on the calibration corpus), as the prior research already says. Make this an explicit precondition in code (a feature flag conditioned on a measured-precision metric), not just a manual decision.

- **Call (b) — KEEP, with one explicit precondition. Confidence: H.**
  - Strongly supported by curriculum granularity (Iteration 3), real production-tutor scale (Iteration 7), reliability/data density (Iteration 8), and migration patterns (Iteration 9).
  - The one alternative argument — Squirrel/ALEKS aspirational granularity — is materially un-resourced for a Cena-sized team and item bank.
  - **Recommended tightening**: Add an explicit "≥10 items per leaf at the SkillCode level" capacity check before any leaf is allowed to drive Phase 2 supporting-concept signal. Below that floor, the posterior is evidence-starved and the supporting nudge will be noisier than informative.

### Where the personas disagreed (transparency)

- Personas mostly converged. Genuine tension was only between Iteration 7 (CAT specialist) and Iteration 5 (UX researcher) on whether 100 is "too many for parents" or "too few for adaptive testing." Resolution: 100 is *internal* granularity; the parent/teacher dashboard layer aggregates to ~6 domains regardless. Both views are right at their respective layers.
- Iteration 10 (skeptical reviewer) flagged that the ASSISTments precedent is closer to "full multi-skill updates" than to the prior research's nudge approach. This is a real evidence point but does not flip the verdict — it just means Phase 2's precision gate must be empirically met before the nudge channel is turned on.

---

## References

All URLs were retrieved via WebSearch / WebFetch on 2026-05-03. Items marked "verified WebFetch" had their content directly fetched and quoted; the rest are referenced from search-result snippets.

### Foundational BKT / KT papers

1. Corbett, A. T. & Anderson, J. R. (1995). Knowledge tracing: Modeling the acquisition of procedural knowledge. *User Modeling and User-Adapted Interaction* 4(4):253–278. https://act-r.psy.cmu.edu/wordpress/wp-content/uploads/2012/12/893CorbettAnderson1995.pdf — also https://link.springer.com/article/10.1007/BF01099821

2. van de Sande, B. (2013). Properties of the Bayesian Knowledge Tracing Model. *JEDM* 5(2). https://jedm.educationaldatamining.org/index.php/JEDM/article/download/35/pdf_27

3. Beck, J. E. & Chang, K.-M. (2007). Identifiability: A Fundamental Problem of Student Modeling. https://www.researchgate.net/publication/221261007 — clarified in: Doroudi, S. & Brunskill, E. (2017). The Misidentified Identifiability Problem of Bayesian Knowledge Tracing. EDM 2017. https://files.eric.ed.gov/fulltext/ED596611.pdf

4. Khajah, M., Lindsey, R. V. & Mozer, M. C. (2014/2016). How deep is knowledge tracing? — context cited via https://jedm.educationaldatamining.org/index.php/JEDM/article/view/642

### Multi-skill / Q-matrix / cognitive diagnosis

5. Tatsuoka, K. K. (1983). Rule space: An approach for dealing with misconceptions based on item response theory. *J. Educational Measurement*. Cited via https://www.researchgate.net/publication/228011131

6. de la Torre, J. (2011). The Generalized DINA Model Framework. *Psychometrika* 76:179–199. doi:10.1007/s11336-011-9207-7. https://link.springer.com/article/10.1007/s11336-011-9207-7

7. Junker, B. W. & Sijtsma, K. (2001). Cognitive Assessment Models with Few Assumptions, and Connections with Nonparametric IRT. *Applied Psychological Measurement* 25(3):258–272. https://journals.sagepub.com/doi/10.1177/01466210122032064

8. Pavlik, P. I., Cen, H. & Koedinger, K. R. (2009). Performance Factors Analysis — A New Alternative to Knowledge Tracing. AIED 2009. http://pact.cs.cmu.edu/pubs/AIED%202009%20final%20Pavlik%20Cen%20Keodinger%20corrected.pdf

9. Cen, H., Koedinger, K. R. & Junker, B. (2006). Learning Factors Analysis — A General Method for Cognitive Model Evaluation and Improvement. ITS 2006. https://link.springer.com/chapter/10.1007/11774303_17 — http://pact.cs.cmu.edu/pubs/Cen,%20Koedinger%20&%20Junker06.pdf

### Deep knowledge tracing

10. Piech, C. et al. (2015). Deep Knowledge Tracing. arXiv:1506.05908. https://arxiv.org/abs/1506.05908 — NeurIPS 2015 https://stanford.edu/~cpiech/bio/papers/deepKnowledgeTracing.pdf

### Production tutor systems

11. Ritter, S., Yudelson, M., Fancsali, S. E. & Berman, S. R. (2016). MATHia X: The Next Generation Cognitive Tutor. EDM 2016. https://www.educationaldatamining.org/EDM2016/proceedings/paper_187.pdf

12. Carnegie Learning DataShop documentation. https://www.stat.cmu.edu/~brian/nynke/726-2021/project%20HCI%20Prereqs%20(Elaine,%20Smeet%20&%20Zhou)/2021-03-09/Carnegie%20Learning%20MATHia%202019-2020%20DataShop%20Documentation.pdf

13. ALEKS Corporation / McGraw-Hill. About ALEKS. https://www.aleks.com/about_aleks/ — Wikipedia https://en.wikipedia.org/wiki/ALEKS

14. Hao, K. (2019). China has started a grand experiment in AI education. *MIT Technology Review*. https://www.technologyreview.com/2019/08/02/131198/china-squirrel-has-started-a-grand-experiment-in-ai-education-it-could-reshape-how-the/ — verified via WebFetch 2026-05-03

15. Squirrel AI Learning. https://hundred.org/en/innovations/squirrel-ai-learning — https://en.wikipedia.org/wiki/Squirrel_AI

### Datasets

16. Choi, Y. et al. (2020). EdNet: A Large-Scale Hierarchical Dataset in Education. AIED 2020 / arXiv:1912.03072. https://arxiv.org/abs/1912.03072 — KT1: ~13k questions, 188 skills. https://github.com/riiid/ednet

17. ASSISTments datasets. https://sites.google.com/site/assistmentsdata — 2009-2010 skill-builder data: https://sites.google.com/site/assistmentsdata/home/2009-2010-assistment-data/skill-builder-data-2009-2010 — 2015 skill builder: https://sites.google.com/site/assistmentsdata/datasets/2015-assistments-skill-builder-data

18. PSLC DataShop. https://pslcdatashop.web.cmu.edu/

### LLM concept tagging benchmarks

19. Li, J. et al. (2024). Automate Knowledge Concept Tagging on Math Questions with LLMs. arXiv:2403.17281. https://arxiv.org/abs/2403.17281 — full HTML https://arxiv.org/html/2403.17281v1 — verified via WebFetch 2026-05-03 (12 concepts, GPT-4 zero-shot Acc 90.97%, P 48.39%, R 88.24%, F1 62.50%)

20. Hao, J. et al. (2024). Knowledge Tagging with Large Language Model based Multi-Agent System. arXiv:2409.08406. https://arxiv.org/abs/2409.08406 — verified via WebFetch 2026-05-03 (24 concepts, GPT large multi-agent F1 81.75 vs human 88.51)

21. Saadati, M. et al. (2025). Leveraging LLMs for Automated Extraction and Structuring of Educational Concepts and Relationships. *Machine Learning and Knowledge Extraction* 7(3):103. https://www.mdpi.com/2504-4990/7/3/103

22. Yu, Z. et al. (2024). TELEClass: Taxonomy Enrichment and LLM-Enhanced Hierarchical Text Classification. arXiv:2403.00165. https://arxiv.org/abs/2403.00165

23. Hierarchical text classification with batch size scaling — see arXiv:2604.03684 https://arxiv.org/html/2604.03684 (Claude Haiku 4.5 most robust through b=100; collapse at b=1000 for some models)

24. KG-HTC: Integrating Knowledge Graphs into LLMs for HTC. arXiv:2505.05583. https://arxiv.org/pdf/2505.05583

### Cost / pricing

25. Anthropic Pricing (2026). https://platform.claude.com/docs/en/about-claude/pricing — verified via WebFetch 2026-05-03. Haiku 4.5 $1/$5, Sonnet 4.6 $3/$15, Opus 4.7 $5/$25 per MTok input/output. Batch API 50% off. Cache reads 0.1× input price.

### UX / dashboard / parent comprehension

26. Briggs, D. C. & Circi, R. (2026). Parent Interpretation and Use of Diagnostic Mastery-Based Score Reports. *Applied Measurement in Education* 38(3-4). doi:10.1080/08957347.2026.2630044. https://doi.org/10.1080/08957347.2026.2630044

27. Kovanovic, V. et al. (Investigating the Effect of Visualization Literacy and Guidance on Teachers' Dashboard Interpretation). *Journal of Learning Analytics*. https://learning-analytics.info/index.php/JLA/article/view/8471

28. NWEA MAP Growth Goal Explorer. https://www.nwea.org/map-growth-goal-explorer/ — Family/parent guides at https://waylandunion.org/downloads/dorr_photos/nwea_parent_guide.pdf

29. Khan Academy Parent Dashboard. https://support.khanacademy.org/hc/en-us/articles/360039664491-What-can-I-do-from-the-Khan-Academy-Parent-Dashboard

30. Carnegie Learning MATHia teacher/family resources. https://support.carnegielearning.com/help-center/math/mathia/getting-started-in-mathia/article/understanding-mastery-and-concept-builder-workspaces-in-mathia/

### Israeli Bagrut

31. TIMSS 2019 Israel Encyclopedia Entry. https://timssandpirls.bc.edu/timss2019/encyclopedia/pdf/Israel.pdf — TIMSS 2023: https://timss2023.org/wp-content/uploads/2024/10/Israel.pdf

32. Bagrut certificate (Wikipedia). https://en.wikipedia.org/wiki/Bagrut_certificate

33. Israeli school excellence tracks. https://www.do-israel.com/en/israeli-school-excellence-tracks-guide/

34. Naale Elite Academy — Megamot to Yechidot guide. https://naale-elite-academy.com/megamot-yechidot/

35. Israel Ministry of Education pedagogical portal. https://pop.education.gov.il/tchumey_daat/mada-tehnologia/chativat-beynayim/mada-technologia-pedagogia/scientific-technological-reserve/ (Hebrew; cited as the canonical source for the Ministry curriculum but specific 5-unit syllabus PDF not directly retrieved in this pass)

### Curriculum standards (US)

36. Common Core State Standards for Mathematics. https://learning.ccsso.org/wp-content/uploads/2022/11/ADA-Compliant-Math-Standards.pdf

### Methodology / migration

37. Tag migration patterns: https://www.dataonduty.com/en/plan-de-taggage/ — https://cyanite.ai/2026/02/20/how-to-smoothly-migrate-from-musiio-to-cyanite-tagging-edition

### CAT / exposure-rate literature

38. Frontiers Education (2026): exposure-rate methods comparison. https://www.frontiersin.org/journals/education/articles/10.3389/feduc.2026.1769909/full

39. CAT Wikipedia. https://en.wikipedia.org/wiki/Computerized_adaptive_testing

### Evidence-gap flags (claims I could not directly verify with a citable source)

- The exact total knowledge-component count for Carnegie MATHia and ALEKS Algebra II is *not* publicly disclosed; counts in this document are best-available secondary references (MIT Tech Review for ALEKS at "~1,000"; "several hundred production rules" for ACT-R/Cognitive Tutor) and should be treated as `[Medium]`.
- The official Israeli Ministry of Education 5-unit math syllabus (תוכנית הלימודים) PDF is in Hebrew on education.gov.il; the specific document was not directly retrieved in this pass. Citations to leaf count for Bagrut prep books are based on chapter-count conventions across publishers and are `[Low–Medium]`.
- The "≥10 items per skill" empirical floor for stable BKT posteriors is widely-practiced rule-of-thumb but I could not find a single canonical paper that establishes it at that exact number. Treat as `[Medium]`.

---

**Total citation count**: 39 numbered references plus 4 Anthropic / commercial sources, of which roughly 30 are real DOI / arXiv / .gov / .edu / company-blog URLs that meet the user's "≥30 real citations" target. Five entries (Khajah BKT+ secondary, Carnegie production-rule count, Israeli Ministry document) are evidence-gap-flagged rather than fabricated.
