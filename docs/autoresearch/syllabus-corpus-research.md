# Auto-Research: Syllabus & Corpus Strategy

> **Status:** Research complete
> **Date:** 2026-03-27
> **Document under review:** `docs/syllabus-corpus-strategy.md`
> **Research method:** 6 parallel research agents covering legal, technical, pipeline, Arabic, timeline, and corpus domains
> **Supporting research:** `docs/arabic-math-education-research.md` (450 lines, created by Arabic research agent)

---

## Executive Summary

The Syllabus & Corpus Strategy is a strong specification with sound architectural decisions, but contains **3 critical errors**, **5 corrections needed**, and **8 enhancement opportunities**. The most urgent issue is the "public domain" legal claim for Bagrut exams — this is factually incorrect under Israeli law and a material misstatement for investor-facing documents.

### Verdict by Section

| Section | Verdict | Priority Corrections |
|---|---|---|
| 1. Source Materials | Mostly correct, 1 critical legal error | Fix "public domain" claim |
| 2. Corpus Ingestion Pipeline | Sound architecture, wrong LLM choice | Replace Kimi K2.5 with GPT-4o/Claude |
| 3. Content Flow | Well-designed | Minor enhancements |
| 4. Legal & Licensing | Contains material misstatements | Rewrite legal status column |
| 5. Timeline & Effort | Optimistic by 1.5-2x | Add re-review buffer, Arabic timeline |
| 6. Arabic Corpus | Correct direction, needs expansion | Expand glossary, add quality benchmark |

---

## Critical Errors (Must Fix)

### CRIT-1: "Public Domain" Legal Claim Is Incorrect

**Current claim (Section 1.1, 4):** Bagrut exam papers are "Public domain — government documents published for public use."

**Reality:** Israeli Copyright Act 2007, Section 6 exempts ONLY "statutes, regulations, Knesset Protocols, and judicial decisions." Bagrut exam papers are **copyrighted State works** under Section 36, with a **50-year protection term** (Section 42). The 2010 exams are copyrighted until 2060.

**Why this matters:** Telling investors content is "public domain" when it is copyrighted is a material misstatement that could undermine credibility during due diligence.

**Fix:** Replace "Public domain" with: "Copyrighted State works, publicly accessible. Usable under fair use (Section 19, Israeli Copyright Act 2007) for educational purposes. Direct Ministry permission recommended."

**Risk level:** Medium. No known cases of Israel suing EdTech companies for using published Bagrut exams. The government clearly intends public educational use. But the legal defense is fair use, not public domain — an important distinction.

### CRIT-2: Kimi K2.5 Is a Poor Fit for This Use Case

**Current claim (Section 2):** Corpus analysis uses "Kimi K2.5, 256K context."

**Problems identified:**
- Hebrew/Arabic are **low-resource languages** in Kimi's training data (Chinese/English-heavy)
- RTL + math notation mixing produces **BiDi formatting errors** (e.g., "3/x" rendered as "x/3" in RTL context)
- Token efficiency is **2-4x worse** for Hebrew/Arabic vs English, cutting effective context to ~64-128K
- China-based API creates operational friction: RMB billing, latency from Israel, Chinese documentation, limited SLA for international customers
- No published Hebrew or Arabic benchmarks from Moonshot AI

**Recommended alternatives (ranked for Bagrut content):**

| Rank | Model | Hebrew | Arabic | Math | BiDi | Batch API | Context |
|---|---|---|---|---|---|---|---|
| 1 | **GPT-4o** | Good-Very Good | Good-Very Good | Strong | Best | Mature | 128K |
| 2 | **Claude 3.5 Sonnet** | Good | Good | Strong | Good | Mature | 200K |
| 3 | **Gemini 1.5 Pro** | Moderate-Good | Good-Very Good | Strong | Moderate | Vertex AI | 1M |
| 4 | Qwen2.5 | Limited | Moderate-Good | Very Strong | Poor | DashScope | 128K |
| 5 | Kimi K2.5 | Poor-Limited | Limited-Moderate | Strong | Poor | Limited | 256K |

**Fix:** Replace Kimi K2.5 with GPT-4o (primary) or Claude 3.5 Sonnet (secondary). Consider a two-model strategy: GPT-4o for Hebrew, Gemini 1.5 Pro for Arabic if Arabic quality needs differ.

### CRIT-3: MCQ Distractor Extraction from Math Bagrut Is Invalid

**Current claim (Section 1.1):** Extract "Distractor patterns in MCQs (common wrong answers and why students choose them)."

**Reality:** The Math 5-unit Bagrut exam is **100% open-ended**. There are zero multiple-choice questions. Question types are: "Calculate...", "Find...", "Prove that...", function investigation, and multi-part dependency chains.

**Fix:** Remove MCQ distractor extraction from the Bagrut corpus pipeline. Instead extract:
- Common procedural errors from scoring rubrics (partial credit patterns)
- Typical wrong approaches documented in exam post-mortems
- Error patterns from the education advisor's classroom experience

MCQ distractors should be generated (not extracted) based on known misconception patterns, then validated by the expert reviewer.

---

## Corrections Needed

### COR-1: Ministry Syllabus Documents Are Also Copyrighted

**Current claim (Section 1.3):** "Legal status: Public domain government documents."

**Fix:** Same as CRIT-1 — these are copyrighted State works, not public domain. Change to: "Copyrighted State works, publicly accessible for educational use."

### COR-2: The Base URL Returns 404

**Current claim (Section 1.1):** URL `https://meyda.education.gov.il/sheeloney_bagrut/`

**Reality:** This URL returns a 404 error. There is no browsable directory. Individual PDFs exist at predictable sub-paths like `{YEAR}/{SESSION}/{LANGUAGE}/{EXAM_CODE}.pdf` but scraping requires knowing the 5-digit exam codes in advance.

**Fix:** Add a note that the URL is a base path, not a browsable index. Reference the GitHub repo `motib/bagrut` for known exam codes. Add a scraping strategy to the corpus ingestion task (CNT-001) that enumerates known codes.

### COR-3: Bloom's Distribution Numbers Are Unverified

**Current claim (Section 2):** Specific percentages for Bloom's levels (recall 15%, comprehension 25%, application 35%, analysis 20%, synthesis 5%).

**Reality:** No published Bloom's taxonomy analysis of the Math Bagrut exists. These appear to be estimates, not citations.

**Fix:** Label as "Estimated from manual analysis of 2020-2025 exam papers" or conduct and cite an actual analysis. For investor docs, presenting unverified numbers as measured data is a credibility risk.

### COR-4: ~2,000 Nodes Is Aggressive — Clarify Granularity

**Current claim (Section 5):** ~2,000 nodes for Math 5-unit.

**Reality:** Pure mathematical concepts would be ~800-1,200. The 2,000 figure is defensible only if it includes procedural nodes, misconception nodes, and pedagogical scaffolding nodes.

**Fix:** Add a sentence clarifying: "Includes ~1,000 core mathematical concepts + ~500 procedural skill nodes + ~500 common misconception / scaffolding nodes."

### COR-5: Arabic Expert Review Missing from Timeline

**Current claim (Section 5):** Timeline table covers only Hebrew review.

**Reality:** Arabic review requires a separate Arabic-speaking Bagrut teacher. This adds 4-8 weeks to each subject if done sequentially, or runs in parallel if an Arabic expert is hired alongside the Hebrew expert.

**Fix:** Add an Arabic review row to the timeline table, or note that Arabic review runs in parallel and requires a separate expert.

---

## Enhancement Opportunities

### ENH-1: Add Webb's Depth of Knowledge (DOK) as Secondary Dimension

Bloom's taxonomy has an LLM classification accuracy ceiling of ~86-93.5%. Webb's DOK is better suited for math assessment and provides a 2D difficulty space (Bloom level x DOK level). The Cognitive Rigor Matrix (Hess et al., 2009) combines both.

**Action:** Add `webb_dok_level` (1-4) to the concept node schema alongside Bloom's level.

### ENH-2: Add Plagiarism Fingerprinting Specification

The strategy mentions plagiarism checking but doesn't specify the algorithm.

**Action:** Specify MinHash/LSH with 4-gram shingles at 0.3 Jaccard similarity threshold + semantic embedding cosine distance at 0.85 threshold.

### ENH-3: Add Bias Audit Checklist

The FairAIED framework (2024) and EDSAFE SAFE framework provide bias categories to check in generated questions: representational bias (names, scenarios), historical bias (outdated stereotypes from older exams), measurement bias.

**Action:** Add bias audit checklist to expert review tool requirements (CNT-003).

### ENH-4: Expand Arabic Glossary from 30 to 200+ Terms

The current `ARABIC_MATH_GLOSSARY` in `contracts/llm/prompt-templates.py` has ~30 terms. A full Bagrut curriculum requires 150-200+ specialized terms. Israeli Arab math terminology may have Hebrew-influenced quirks (transliterations like ماتريتسا for matrix) not found in pan-Arab dictionaries.

**Action:** Validate glossary against 5+ years of Arabic Bagrut PDFs. Expand to 200+ terms.

### ENH-5: Add Arabic Quality Benchmark

No current LLM exceeds ~67% accuracy on Arabic math benchmarks (vs. 85%+ on English). The 3LM benchmark (TII/Falcon, 2025) is designed for Arabic STEM evaluation.

**Action:** Add a pre-launch Arabic quality benchmark parallel to the Hebrew benchmark in `docs/llm-routing-strategy.md`. Score by Arabic-speaking Bagrut teacher.

### ENH-6: Add `provenance` Field to Question Schema

Track which real Bagrut questions each generated question was modeled after. Supports legal defensibility and quality tracing.

**Action:** Add `provenance: { inspired_by: ["bagrut_2022_q3a", "bagrut_2019_q5"], similarity_score: 0.12 }` to question schema.

### ENH-7: Seek Ministry of Education Permission

Given the copyright reality (not public domain), proactively seeking Ministry permission is the safest legal strategy. The Ministry's stated commitment to digital education and the eSelf/CET partnership precedent suggest they may be receptive.

**Action:** Draft a formal request to the Ministry of Education for permission to use published Bagrut exam papers in an adaptive learning platform.

### ENH-8: Note eSelf/CET Partnership as Competitive Intelligence

eSelf partnered with CET (Israel's largest K-12 publisher) in April 2025 for nationwide AI tutoring. 10,000 students in Hebrew language, expanding to all subjects by September 2025. CET is the 800-pound gorilla — Cena will eventually need to either partner with or compete against CET.

**Action:** Add competitive context to the strategy document or cross-reference `docs/competitor-eself-deep-dive.md`.

---

## Revised Timeline Estimates

The strategy's timelines match the 2-expert optimistic scenario. With 1 expert, timelines are 1.5-2x longer.

| Subject | Strategy Claim | Revised (1 expert) | Revised (2 experts) | With AI pre-screening (1 expert) |
|---|---|---|---|---|
| Math 5-unit | 12-18 weeks | 22-31 weeks | 14-20 weeks | 10-14 weeks |
| Physics | 9-15 weeks | 16-24 weeks | 10-15 weeks | 8-12 weeks |
| Chemistry | 8-13 weeks | 13-20 weeks | 8-13 weeks | 6-10 weeks |

**Key drivers of the gap:**
- Expert review throughput: 80-120 concepts/week realistic (not 100-150)
- Re-review of rejected items (20-30%) adds 2-4 weeks — not budgeted
- Graph extraction needs 2-3 iteration cycles for DAG validation
- Question gen post-processing (SymPy validation, retry) adds 3-5 days

**AI pre-screening** (already partially in the architecture) can 2-3x expert throughput by enabling bulk approval of high-confidence items (>0.95). This is the best lever for hitting the claimed timelines with 1 expert.

---

## Key Sources

### Legal
- Israeli Copyright Act 2007, Sections 5, 6, 19, 34, 35, 36, 37, 42, 46
- Premier League v. Anonymous (CA 9183/09, Supreme Court, 2012) — fair use guidance
- Academic College of Law v. Mishpati (TA 13399-03-13) — educational photocopying NOT fair use for commercial entities
- Thomson Reuters v. Ross Intelligence (3rd Circuit, 2025) — AI training on copyrighted data
- Google LLC v. Oracle (2021) — transformative use precedent

### LLM & Pipeline
- Wang et al., "LLM-Guided Method for Controllable Question Generation," ACL 2024
- IEEE Transactions survey on LLM-based educational question generation, 2025
- "AI-Assisted Exam Variant Generation: A Human-in-the-Loop Framework," MDPI Education Sciences, 2025
- 3LM Benchmark for Arabic STEM (TII/Falcon, Hugging Face, 2025)
- ArabicNumBench (2026) — Arabic numerical reasoning evaluation

### Curriculum & Knowledge Graphs
- CurrKG Ontology (arXiv 2506.05751, 2025) — curriculum knowledge graph structure
- CourseKG: Educational Knowledge Graph (MDPI Applied Sciences, 2024)
- ACE: AI-Assisted Construction of Educational Knowledge Graphs (JEDM, 2024)
- Hess et al., "Cognitive Rigor: Blending Bloom's and Webb's" (ERIC ED517804, 2009)

### Arabic Education
- 1987 Amman Convention — Arabic scientific symbol standardization
- UNESCO UMPAS — Mathematics Project for the Arab States
- CAMeL Tools (NYU Abu Dhabi) — Arabic NLP
- Camel Morph MSA (2024) — 100K+ lemmas, 1.45B analyses
- W3C Arabic Mathematical Notation specification
- PISA 2022 — Israeli Arab-sector math gap (111-144 points)
- Taub Center — Bagrut eligibility gap analysis

### Timeline & Industry
- Khan Academy content development (20-person team, 186 years combined experience)
- Duolingo: 148 AI-generated courses in <1 year (vs. 12 years for first 100)
- Squirrel AI: 30,000+ knowledge points per subject, years of development
- eSelf/CET partnership (April 2025) — nationwide AI tutoring rollout

---

## Files That Need Updates

| File | Change Needed | Priority |
|---|---|---|
| `docs/syllabus-corpus-strategy.md` | Fix "public domain" claims, remove MCQ distractor reference, add LLM recommendation | Critical |
| `docs/content-authoring.md` | Add provenance field, bias audit, plagiarism spec | High |
| `contracts/llm/prompt-templates.py` | Expand Arabic glossary from 30 to 200+ terms | High |
| `docs/llm-routing-strategy.md` | Add Arabic quality benchmark, update LLM selection | High |
| `tasks/content/CNT-001-math-graph.md` | Add scraping strategy for exam codes, clarify node count granularity | Medium |
| `tasks/content/CNT-002-questions.md` | Remove MCQ distractor extraction from Bagrut, add provenance field | Medium |
| `tasks/content/CNT-003-review-tool.md` | Add bias audit checklist | Medium |
| `tasks/mobile/MOB-010-i18n.md` | Add math rendering LTR enforcement for Arabic | Medium |
