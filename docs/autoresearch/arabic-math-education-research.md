# Arabic Language Specifics for Mathematical Education Content Generation

> **Status:** Research findings
> **Date:** 2026-03-27
> **Purpose:** Detailed analysis of Arabic-language challenges and opportunities for Cena's math content generation pipeline, focused on practical implications for serving Israeli Arab-sector students.
> **Applies to:** Content Authoring Context, LLM Routing Strategy, MOB-010 (i18n), prompt-templates.py

---

## 1. Arabic Mathematical Terminology (MSA)

### 1.1 Terminology Landscape in Israeli Arab-Sector Schools

Israeli Arab-sector schools use Modern Standard Arabic (MSA / fusha) for all mathematics instruction. The Ministry of Education mandates that the math curriculum is **identical** to the Hebrew-sector curriculum, with all materials translated from Hebrew to Arabic. This means the Arabic mathematical terminology used in Israeli schools is primarily **calqued from Hebrew terminology**, which itself often borrows from European mathematical traditions.

**Current glossary status in Cena:** The `ARABIC_MATH_GLOSSARY` in `contracts/llm/prompt-templates.py` contains ~30 MSA terms. The glossary note states: "Aligns with Palestinian/Jordanian curriculum conventions." This is directionally correct but needs nuance (see below).

### 1.2 Regional Terminology Variations

| Term (English) | Israel/Palestine | Egypt | Jordan | Gulf States | Notes |
|---|---|---|---|---|---|
| Function | دالة (dalla) | دالة (dalla) | دالة (dalla) | دالة (dalla) | Consistent across MSA |
| Equation | معادلة (mu'adala) | معادلة | معادلة | معادلة | Consistent |
| Derivative | مشتقة (mushtaqqa) | مشتقة | مشتقة | مشتقة | Consistent |
| Integral | تكامل (takamul) | تكامل | تكامل | تكامل | Consistent |
| Logarithm | لوغاريتم (lugharitm) | لوغاريتم | لوغاريتم | لوغاريتم | Loanword, consistent |
| Factoring | تحليل إلى عوامل | تحليل | تحليل إلى عوامل | تحليل | Slight phrasing variation |
| Matrix | مصفوفة (masfufa) | مصفوفة | مصفوفة | مصفوفة | Consistent |
| Proof | برهان (burhan) | برهان | برهان | إثبات (ithbat) in some contexts | Minor variation |

**Key finding:** Core mathematical terminology in MSA is remarkably consistent across the Arab world for secondary/high-school level mathematics. The 1987 Amman Convention (organized by the Jordan Academy of Arabic) established guidelines for scientific symbols in Arabic, and the UNESCO Mathematics Project for the Arab States (UMPAS, launched 1969) developed standardized secondary-level curricula. These efforts have produced genuine terminological convergence at the Bagrut-equivalent level.

**Where variation exists:** Colloquial explanations (not formal terminology) diverge significantly. An Egyptian teacher might explain a concept using Egyptian Arabic phrasing that differs from Levantine Arabic phrasing used in Israel. Since Cena generates formal MSA content for exam prep, this variation is less relevant for question generation but important for Socratic dialogue (where the tone should feel natural to Israeli Arab students, who speak Palestinian/Levantine Arabic colloquially).

### 1.3 Standardization Efforts

- **ALECSO** (Arab League Educational, Cultural and Scientific Organization): Maintains standardized vocabularies for scientific and technical fields, including mathematics.
- **Jordan Academy of Arabic**: The 1987 Amman Convention remains the most influential standardization effort for mathematical symbols and notation in Arabic.
- **National language academies** in each Arab country (Egypt's Academy of the Arabic Language, etc.): Contribute to terminology standardization but with limited cross-border enforcement.

### 1.4 Practical Implications for Cena

1. **The existing 30-term glossary is a good start but insufficient.** A full Bagrut math curriculum requires approximately 150-200 specialized terms (covering calculus, trigonometry, linear algebra, probability, statistics, analytical geometry). Recommendation: expand to 200+ terms, cross-referencing against actual Arabic Bagrut exam papers.
2. **Loanwords dominate advanced math.** Terms like لوغاريتم (logarithm), أسيمبتوت (asymptote), بارابولا (parabola), ماتريتسا (matrix -- note: Israeli Arabic Bagrut may use the Hebrew-influenced transliteration rather than the pan-Arab مصفوفة). This needs validation against actual Arabic Bagrut PDFs.
3. **Israeli Arab math terminology may have Hebrew-influenced quirks** not found in Jordanian or Egyptian curricula, because the curriculum is translated from Hebrew. Validate the glossary against 5+ years of Arabic Bagrut exam papers, not against pan-Arab dictionaries.

---

## 2. RTL + Math Notation Challenges

### 2.1 The Core Problem

Arabic text flows right-to-left (RTL). Mathematical notation, even in Arabic-speaking countries, predominantly flows **left-to-right (LTR)**. This creates bidirectional (bidi) text environments where a single line of content may contain:

```
[Arabic RTL text] [LTR math expression] [Arabic RTL text]
```

Example: `احسب قيمة x عندما 2x + 3 = 7 ثم تحقق من الحل`
(Calculate the value of x when 2x + 3 = 7, then verify the solution)

The Arabic text runs RTL, but `2x + 3 = 7` must render LTR. The Unicode Bidirectional Algorithm (UBA) must correctly handle this interleaving.

### 2.2 Mathematical Directionality: The Two Traditions

There are **two distinct traditions** for mathematical directionality in the Arabic-speaking world:

| Tradition | Where Used | Math Direction | Example |
|---|---|---|---|
| **LTR math** (dominant) | Egypt, Levant (including Israel), most of the Arab world | Left-to-right | `f(x) = 2x + 3` reads left to right |
| **RTL math** (Maghreb tradition) | Morocco, parts of North Africa | Right-to-left | Expressions are mirrored; `3 + x2 = (x)f` |

**For Israeli Arab students, math is LTR.** This is confirmed by the fact that Bagrut exams use standard LTR mathematical notation identical to the Hebrew-sector exams. Cena does NOT need to support RTL math (Maghreb convention).

### 2.3 Unicode Bidirectional Algorithm (UBA) Implications

The UBA (UAX #9) classifies characters into directional types:

| Character Type | Direction | Examples |
|---|---|---|
| Arabic letters | Strong RTL | ا ب ت ث ج |
| Latin letters | Strong LTR | a b c x y z |
| European digits | Weak LTR (European Number) | 0 1 2 3 4 5 |
| Math operators | Weak/Neutral | + - = × ÷ |
| Parentheses | Mirrored characters | ( ) [ ] { } |

**Critical issues for Cena:**

1. **Operator and delimiter mirroring.** In an RTL context, parentheses are automatically mirrored by Unicode: `(` becomes `)` visually. This is correct for Arabic text but can cause confusion in math expressions. For example, `f(x)` in an RTL paragraph might render as `(f(x` if not properly isolated.

2. **Inline math in RTL paragraphs.** When a math expression like `x² + 3x - 4 = 0` appears inline within Arabic text, the UBA must correctly identify the math as an LTR island. Without explicit bidi marks or isolation, the algorithm may reorder parts of the expression.

3. **Arabic-Indic vs. Western Arabic numerals.** Arabic-speaking countries use two numeral systems:
   - Western Arabic: 0 1 2 3 4 5 6 7 8 9 (used in Israel, Lebanon, most of the Levant)
   - Eastern Arabic-Indic: ٠ ١ ٢ ٣ ٤ ٥ ٦ ٧ ٨ ٩ (used in Egypt, Gulf states)
   Israeli Arab students use **Western Arabic numerals** (same as Hebrew-sector students). The MOB-010 task correctly specifies: "Number rendering: Arabic-Indic numerals optional (default: Western Arabic)."

### 2.4 Technical Solutions

**W3C Arabic Mathematical Notation specification** (w3.org/TR/arabic-math): Defines how to handle mathematical directionality in MathML. Key features:
- The `dir` attribute on the `<math>` element sets overall directionality (`ltr` or `rtl`).
- For Israeli Arab students, `dir="ltr"` is correct for math content.
- MathML Core's CSS stylesheet defaults to `direction: ltr` for math elements.

**Edraak arabic-mathjax** (github.com/Edraak/arabic-mathjax): An open-source MathJax extension created by the Queen Rania Foundation's Edraak platform (Jordan). Features:
- Flips equations for RTL rendering when needed (Maghreb convention).
- Translates common identifiers and function names to Arabic.
- MIT-licensed, can be used as reference or integrated.
- **Relevance to Cena:** Useful as a reference for how another MENA EdTech platform solved the same problem. However, Edraak supports the Maghreb RTL math convention which Cena does NOT need for Israeli students.

**Practical rendering approach for Cena:**

1. **Math expressions should always be rendered LTR**, even when embedded in RTL Arabic text.
2. Use Unicode bidi isolation (`U+2066` LRI / `U+2069` PDI, or HTML `<bdi>` / CSS `unicode-bidi: isolate`) to wrap inline math expressions.
3. For displayed (block-level) math, use MathML with `dir="ltr"` or KaTeX/MathJax in LTR mode.
4. The Flutter `Directionality` widget in MOB-010 handles text direction, but math rendering components (MathQuill, KaTeX) must be explicitly set to LTR regardless of the app's locale.
5. SVG diagrams generated by the diagram pipeline: ensure `direction: ltr` on coordinate axes and labels, while descriptive Arabic text labels use RTL.

### 2.5 Known Platform Approaches

| Platform | How They Handle Arabic Math |
|---|---|
| **Edraak** (Jordan) | MathJax extension with optional RTL math; Arabic labels on graphs |
| **Khan Academy Arabic** | LTR math in RTL text; uses KaTeX |
| **Wikipedia Arabic** | Long-standing open bug (Phabricator T32630) for math alignment in RTL wikis; LaTeX renders LTR but alignment with surrounding text is inconsistent |
| **Wolfram Alpha** | No Arabic interface |
| **GeoGebra** | Arabic UI available; math rendering is LTR; some localization issues with axis labels |

---

## 3. Translation vs. Original Generation

### 3.1 The Claim Under Examination

From `docs/syllabus-corpus-strategy.md`:
> "Machine translation of math questions introduces subtle errors (e.g., pronoun gender in Arabic affects equation references). Original generation in Arabic is more reliable."

### 3.2 Validation: The Claim Is Substantially Correct

**Evidence supporting the claim:**

**A. Arabic grammatical gender creates translation hazards in math word problems.**

Arabic has a pervasive gender system that affects verbs, pronouns, adjectives, and even number agreement. In mathematical word problems, this creates specific risks:

1. **Verb-subject agreement.** Arabic verbs conjugate for gender and number (18 conjugation forms: 3 persons x 2 genders x 3 numbers). In Standard Arabic, agreement is contingent on word order -- in SVO order, the verb agrees in all phi-features with the subject; in VSO order, partial agreement (person + gender only). A translation engine that gets the word order wrong will produce incorrect gender agreement.

2. **Number-noun agreement.** Arabic has a counter-intuitive "polarity" rule where numbers 3-10 take the **opposite** gender of the counted noun (e.g., "three boys" uses the feminine form of "three"). This affects math word problems: "Ahmad has 5 apples" requires the correct gender form of "5" based on the gender of "apples" (تفاحات, feminine). Machine translation from Hebrew, where number-noun gender agreement is simpler, frequently gets this wrong.

3. **Pronoun reference in multi-step problems.** In a Bagrut-style multi-part problem:
   > "Given the function f(x) = x^2 + 3x. Find **its** derivative. Then determine **its** zeros."

   In Arabic, "its" (referring to the function or the derivative) must agree in gender with the referent. دالة (function) is feminine, so the pronoun is هَا (ha). مشتقة (derivative) is also feminine. But if the problem refers to a "graph" (بيان, masculine), the pronoun switches to هُ (hu). Hebrew uses similar gendered pronouns, but machine translation can confuse which pronoun refers to which antecedent, especially in chains.

4. **Dual number.** Arabic has a dual form (in addition to singular and plural) that Hebrew does not have in the same way. "Two equations" in Arabic requires the dual form (معادلتان/معادلتين) which translation systems can miss, producing either singular or broken plural.

**B. Research on machine translation errors for Arabic.**

Published research on machine translation error analysis demonstrates that lexical, semantic, and reordering errors have the most significant impact on Arabic MT quality. Gender-bound constructs in English-to-Arabic MT have been specifically studied, with systems like Google Translate and Microsoft Bing making errors in subject-verb agreement, adjectival-noun agreement, and pronoun-antecedent agreement.

While no published study specifically examines Hebrew-to-Arabic machine translation of mathematical content, the structural similarities between the languages (both Semitic, both RTL, both with grammatical gender) actually make this a harder problem -- the languages are close enough that MT systems may "assume" direct correspondence where subtle differences exist.

**C. Hebrew-to-Arabic MT quality is limited by training data.**

Arabic-Hebrew is a lower-resource language pair for MT systems compared to Arabic-English or Hebrew-English. Most commercial MT systems (Google, Microsoft) handle this pair by pivoting through English internally, which introduces additional error compounding.

### 3.3 Specific Error Types in Translated Math Content

| Error Type | Example | Severity |
|---|---|---|
| Gender agreement on verbs | "احسب" (masculine) vs. "احسبي" (feminine) -- using wrong form for student address | Medium -- confusing but not mathematically wrong |
| Number-noun polarity | "5 apples" with wrong gender on "5" | Low -- stylistically wrong, mathematically clear |
| Pronoun reference in chains | "its derivative" using masculine pronoun for feminine noun | High -- can cause ambiguity about what "it" refers to |
| Word order changes | Arabic VSO vs. SVO changes verb agreement rules | Medium -- can produce grammatically incorrect sentences |
| Dual number omission | "Two equations" rendered as plural instead of dual | Low -- understood but sounds unnatural |
| Terminology inconsistency | Hebrew term translated to pan-Arab Arabic rather than Israeli Bagrut convention | High -- student encounters unfamiliar terminology |
| Missing diacritics | Ambiguous consonantal skeleton without vowel marks | Medium -- can change meaning of mathematical terms |

### 3.4 Verdict

**The claim is valid.** Original generation in Arabic is more reliable than translation from Hebrew for mathematical content, for three reasons:

1. **Grammatical gender hazards** are real and specific to math word problems, where pronoun chains and number agreement directly affect comprehension.
2. **Terminology consistency** is better controlled in original generation (using the Arabic Bagrut corpus as the style guide) than in translation (which may produce pan-Arab rather than Israeli-specific terminology).
3. **Style matching** is more natural -- Arabic Bagrut exams have their own phrasing patterns ("أثبت أن..." / "احسب..." / "أوجد...") that are better captured by learning from Arabic exam papers directly than by translating Hebrew patterns.

**However, the claim should be refined.** The strategy document implies that translation is categorically unreliable. A more accurate statement: translation is adequate for **simple factual content** (definitions, formula references) but unreliable for **word problems, multi-step instructions, and Socratic dialogue** where gender agreement, pronoun reference, and natural phrasing matter.

### 3.5 Recommendation

Maintain the original-generation strategy for Arabic content. Enhance it with:
1. A Hebrew-to-Arabic **terminology mapping** (not free translation) for ensuring concept alignment.
2. An Arabic Bagrut **exam corpus** as the primary style guide (not translated Hebrew patterns).
3. Arabic-specific **grammar validation** (see Section 4) to catch gender agreement errors in generated content.

---

## 4. Arabic NLP for Education

### 4.1 Available Tools Relevant to Cena

| Tool | Developer | Capabilities | Relevance to Cena |
|---|---|---|---|
| **CAMeL Tools** | NYU Abu Dhabi (CAMeL Lab) | Morphological analysis, POS tagging, diacritization, NER, sentiment analysis, dialect identification | HIGH -- can validate Arabic grammar in generated content |
| **Camel Morph MSA** (2024) | NYU Abu Dhabi | Largest open-source MSA morphological analyzer; 100K+ lemmas, 1.45 billion analyses, 535 million diacritizations | HIGH -- can check generated Arabic text for morphological correctness |
| **AraBERT** | AUB | BERT pre-trained on Arabic; SOTA on Arabic NLU tasks | MEDIUM -- could power quality classification of generated Arabic content |
| **Stanford Arabic Parser** | Stanford NLP | Penn Arabic Treebank trained parser, word segmentation | MEDIUM -- useful for structural validation |
| **SAMA 3.1** (Standard Arabic Morphological Analyzer) | LDC | Morphological analysis considering all prefix-stem-suffix segmentations | MEDIUM -- superseded by Camel Morph for most uses |
| **Edraak arabic-mathjax** | Queen Rania Foundation | MathJax extension for Arabic math rendering | HIGH -- reference implementation for math rendering in Arabic |

### 4.2 State of Arabic Math-Specific NLP

**There are no publicly available Arabic math tokenizers, parsers, or quality checkers specifically designed for mathematical education content.** This is a gap in the ecosystem.

What exists:
- **General Arabic NLP** is mature (CAMeL Tools, AraBERT, Farasa, MADAMIRA) and can handle morphological analysis, POS tagging, and diacritization.
- **Arabic math benchmarks** exist (GATmath, Arabic-GSM8K, 3LM) but are for evaluating LLM performance, not for validating generated content.
- **Arabic readability assessment** is being researched (BAREC Shared Task 2025) but is not math-specific.

What does NOT exist:
- No Arabic equivalent of SymPy for symbolic math with Arabic notation support.
- No Arabic math word problem parser that extracts equations from Arabic text.
- No Arabic math terminology validator that checks whether generated content uses correct Bagrut-level terms.

### 4.3 Practical Approach for Cena

Since specialized Arabic math NLP tools do not exist, Cena should build a lightweight validation pipeline using available components:

```
Generated Arabic content
    │
    ├─► CAMeL Tools morphological analysis → check gender agreement on verbs/pronouns
    ├─► Terminology check → verify terms match Arabic Bagrut glossary (200+ terms)
    ├─► SymPy validation → same as Hebrew pipeline (math is language-independent)
    ├─► Diacritization check → ensure critical mathematical terms have correct diacritics
    └─► Arabic expert review → human validation (Arabic-speaking Bagrut teacher)
```

The morphological analysis step can catch the most common translation/generation errors (gender agreement, number agreement) automatically. The terminology check is a simple dictionary lookup. SymPy validation works identically to the Hebrew pipeline since mathematical expressions are language-neutral.

### 4.4 Conference and Research Landscape

- **ArabicNLP 2025** (co-located with EMNLP 2025, November 2025): The premier venue for Arabic NLP research. Relevant shared tasks include BAREC (readability assessment) and educational quality data filtering.
- **AbjadNLP 2026**: Second workshop on NLP for languages using Arabic script, expanding coverage beyond Arabic to Urdu, Farsi, etc.
- **ArabicWeb-Edu dataset**: Research on evaluating web pages for educational value in Arabic -- potentially relevant for Arabic content quality assessment.

---

## 5. CET Arabic Materials

### 5.1 What CET Offers

CET (Center for Educational Technology / מט"ח) is Israel's largest educational publisher. Their Arabic-language offerings include:

| Resource | Description | Quality Assessment |
|---|---|---|
| **Ofek Digital Backpack** | Digital content platform with Arabic interface | Platform is available in Arabic; unclear how comprehensive Arabic math content is |
| **Ivritna** | Online Hebrew tutoring for Arabic speakers (3,500+ students) | Not math-focused; teaches Hebrew conversational skills to Arab students; Ministry-adopted |
| **Translated textbooks** | Hebrew textbooks translated to Arabic | Ministry-mandated translations; quality varies; some complaints about stilted Arabic |
| **Arabic website** | cet.ac.il available in Arabic | Navigation and institutional content in Arabic |
| **BaGroup** | WhatsApp study groups (44,000 students) | Unclear if Arabic study groups exist separately |

### 5.2 Quality Assessment

**CET's Arabic math materials are translations of Hebrew materials, not originally authored in Arabic.** This is consistent with the Ministry of Education's approach where "curricula for mathematics and science and technology are written in Hebrew and translated into Arabic."

This translation-based approach has known limitations:
1. Terminology may not always match what Arabic-speaking teachers use colloquially in classrooms.
2. Phrasing patterns may feel "translated" rather than natural Arabic.
3. Word problems set in Hebrew cultural contexts may not resonate with Arab students (though the Ministry has made efforts to adapt contexts).

**CET does NOT appear to have an Arabic-first math content generation capability.** Their Arabic content is derivative of their Hebrew content.

### 5.3 Competitive Implication for Cena

This is a significant opportunity. If Cena generates Arabic math content **natively** (from Arabic Bagrut exam corpus, not translated from Hebrew), it would offer:
1. More natural Arabic phrasing.
2. Terminology validated against actual Arabic Bagrut exams.
3. Word problems that feel culturally appropriate.
4. No "translated feel."

This positions Arabic-native content as a genuine differentiator vs. CET and other Hebrew-first platforms.

---

## 6. Arab-Sector Bagrut Performance Gaps

### 6.1 The Data

| Metric | Hebrew Sector | Arab Sector | Gap | Source |
|---|---|---|---|---|
| **PISA 2018 Math** | 506 | 362 | **144 points** | OECD/Taub Center |
| **PISA 2022 Math (Israel avg)** | ~490 (est.) | ~379 (est.) | **~111 points** | OECD (national avg 458) |
| **Bagrut eligibility rate (2021)** | 77% | 75% | **2pp** (narrowed significantly) | Taub Center |
| **"Full" Bagrut (4-5 units math+English, 2016)** | 47% | 23% | **24pp** | Taub Center |
| **Meitzav Grade 5 math improvement (2008-2017)** | ~13% gain | ~22% gain | Arab sector improving faster | Taub Center |
| **TIMSS 2019: Math teachers (increase since 2013)** | Moderate | **+171%** | Major investment in Arab sector | TIMSS Israel report |

### 6.2 Key Observations

1. **The overall Bagrut eligibility gap has nearly closed** (77% vs. 75% in 2021), but this masks a critical quality gap: the **5-unit mathematics Bagrut**, which is necessary for competitive university admission in STEM fields, remains dramatically less common in the Arab sector.

2. **PISA gaps are enormous** -- 111-144 points represents roughly 3-4 years of schooling equivalent. Hebrew-speaking students score above the OECD average while Arabic-speaking students score far below it.

3. **Socioeconomic factors explain much of the gap.** The Taub Center notes that "disparities in achievement between Jewish students and Arab Israeli students can be explained to a great extent by their socioeconomic backgrounds." When comparing students of similar SES, the gap shrinks substantially.

4. **The Arab sector is improving faster** on many metrics, suggesting that investment (teacher training, budgets, infrastructure) is having an effect. The 171% increase in math teachers in the Arab sector since 2013 is particularly notable.

5. **5-unit math is the critical barrier.** For university STEM admission, students need 5-unit math Bagrut. The 24pp gap in "full Bagrut" rates (which requires 4-5 units math) represents the real access barrier.

### 6.3 Content and Pedagogical Implications for Cena

| Gap Factor | Cena's Potential Intervention |
|---|---|
| **Less access to quality tutoring** | AI tutor accessible 24/7 in Arabic; bridges the private-tutoring gap |
| **Translated rather than native Arabic content** | Arabic-native content generation from Arabic Bagrut corpus |
| **Lower 5-unit math enrollment** | Adaptive system that builds confidence from 3-unit level toward 5-unit capability |
| **Teacher quality variance** | Consistent, high-quality Socratic tutoring regardless of school quality |
| **SES-driven resource gaps** | Mobile-first, offline-capable platform accessible to lower-SES families |
| **Culturally alienating content** | Word problems and contexts relevant to Arab students' lives |
| **Lower parental academic support** | Parent dashboard with Arabic interface to increase home involvement |

### 6.4 Market Sizing Implication

Arab-sector students represent approximately **28-30%** of Israel's school-age population. With ~75,000 students per age cohort in Israel, approximately **20,000-22,000** Arab students per year sit for Bagrut. If Cena captures even 10% of this market at launch, that is 2,000+ paying students -- a significant market that is **underserved by existing Hebrew-first EdTech**.

---

## 7. LLM Arabic Math Generation Quality

### 7.1 Benchmark Data (2025-2026)

| Model | Arabic Math Benchmark | Score | Notes |
|---|---|---|---|
| **GPT-4** | Qiyas (Arabic GAT) | 64% avg | Moderate; better than GPT-3.5 (49%) but far from ceiling |
| **GPT-4** | Arabic MMLU | ~72.5% | Better on general knowledge than specific math reasoning |
| **Best overall model** | GATmath | 66.9% | "Considerable challenge" -- no model exceeds 67% |
| **Best overall model** | GATLc | 64.3% | Even lower on language comprehension + math |
| **Qwen3 14B** | Dialectal Arabic benchmarks | +27.4pp vs Qwen2.5 | Strong improvement in Arabic multilingual capabilities |
| **Jais 70B** | Tabular reasoning | "Lags badly" | Arabic-centric training insufficient for math reasoning |
| **Falcon Arabic** | Arabic NLP | Competitive | Better on language tasks than reasoning tasks |

### 7.2 Key Findings

1. **No current LLM achieves above ~67% accuracy on Arabic math benchmarks.** This is substantially lower than English math benchmarks (where GPT-4 scores 85%+ on GSM8K). The gap reflects both the models' weaker Arabic training and the inherent difficulty of Arabic mathematical text processing.

2. **Arabic-specific models (Jais, Falcon Arabic) do NOT outperform general multilingual models (GPT-4, Qwen) on math reasoning.** Being Arabic-centric in training does not compensate for insufficient mathematical reasoning capability. For math content generation, general reasoning ability matters more than Arabic language specialization.

3. **The ArabicNumBench study (February 2026)** specifically found that "numerical correctness is insufficient for characterizing production-ready Arabic language models" -- models may produce mathematically correct answers but with poor Arabic linguistic quality.

4. **Kimi K2.5 (Cena's batch generation model) has no published Arabic math benchmarks.** Moonshot AI's primary market is China; Arabic capability is incidental. However, Kimi's structured output support and long context window (256K) are useful regardless of language -- the key question is whether its Arabic generation quality is adequate for Bagrut-level content.

5. **Claude Sonnet 4.6 (Cena's tutoring model) performs well on multilingual tasks** but specific Arabic math generation quality has not been independently benchmarked as of March 2026.

### 7.3 Quality Risks Specific to Arabic Math Generation

| Risk | Description | Mitigation |
|---|---|---|
| **Gender agreement errors** | LLMs may produce masculine verb forms when addressing female students, or use wrong gender pronouns for mathematical objects | CAMeL Tools morphological validation; prompt engineering with explicit gender instructions |
| **Diacritization errors** | Missing or incorrect diacritics can change the meaning of Arabic words (e.g., عَدَد "number" vs. عُدَد "equipment") | Post-generation diacritization check using Camel Morph MSA |
| **Terminology drift** | LLM may use pan-Arab or Egyptian Arabic terms instead of Israeli Bagrut convention | Glossary-constrained generation; terminology validation against Bagrut corpus |
| **Code-switching** | LLM may mix Arabic and English/Hebrew within a single response | Content filtering for language consistency |
| **Stilted MSA** | Generated MSA may be overly formal or "robotic" compared to natural classroom Arabic | Few-shot examples from real Arabic Bagrut exams in prompts |
| **Numeral system confusion** | LLM may output Eastern Arabic-Indic numerals (٠١٢) instead of Western Arabic (012) | Post-processing normalization; explicit prompt instruction |

### 7.4 Recommended Arabic Quality Benchmark for Cena

Extend the Hebrew quality benchmark from `docs/llm-routing-strategy.md` Section 6.3 to Arabic:

**Arabic Math Content Quality Benchmark (pre-launch blocker):**

1. **Test set**: Same 10 math concepts as Hebrew benchmark.
2. **Generation tasks per concept**:
   - Generate 3 Socratic dialogue turns in Arabic using Claude Sonnet 4.6
   - Generate 1 Feynman explanation in Arabic
   - Evaluate 5 student free-text answers in Arabic (including gender-varied student personas)
   - Generate 5 MCQ questions with distractors in Arabic
3. **Quality rubric** (scored by Arabic-speaking Bagrut teacher, 1-5):
   - Arabic math terminology accuracy (uses Israeli Bagrut conventions)
   - Gender agreement correctness (verbs, pronouns, number-noun polarity)
   - Mathematical correctness (same as Hebrew benchmark)
   - Pedagogical appropriateness (Bagrut level, not university)
   - Arabic fluency (natural MSA, not "translated feel")
   - Diacritization accuracy on key terms
4. **Pass threshold**: Average score >= 3.5 across all criteria. Any single criterion < 2.0 is a blocker.
5. **Additional Kimi K2.5 benchmark**: Generate 20 Arabic MCQ questions in batch mode. Score on terminology accuracy and gender agreement. If error rate > 15%, Kimi is not suitable for Arabic generation and Claude Sonnet should be used instead (at higher cost).

### 7.5 Model Recommendation for Arabic Content

| Task | Recommended Model | Rationale |
|---|---|---|
| Arabic Socratic tutoring | Claude Sonnet 4.6 | Best multilingual reasoning; Kimi's Arabic quality is unproven |
| Arabic MCQ generation (batch) | **Needs benchmarking** -- try Kimi K2.5 first, fall back to Sonnet | Cost matters for batch; but quality must be validated |
| Arabic error classification | Kimi K2.5 | Classification is language-light; structured output sufficient |
| Arabic content safety | Kimi K2 Turbo | Binary classification; language-minimal |
| Arabic diagram generation | Kimi K2.5 | Labels only; validation against Arabic glossary |
| Arabic expert review queue | N/A | Human Arabic-speaking Bagrut teacher (separate hire) |

---

## 8. Cross-Cutting Recommendations

### 8.1 Immediate Actions (Before Development)

1. **Download and analyze 5+ years of Arabic Bagrut math exams** from the Ministry of Education website. Extract terminology, phrasing patterns, and question structures to build the Arabic corpus style guide. This is the single most important preparatory step.

2. **Expand the Arabic math glossary** from 30 to 200+ terms, validated against the Arabic Bagrut exam corpus. Pay special attention to potential Hebrew-influenced terms unique to Israeli Arabic math education.

3. **Run the Arabic quality benchmark** described in Section 7.4 with Claude Sonnet and Kimi K2.5. Results determine whether Kimi is usable for Arabic batch generation or whether all Arabic content must go through Claude (with 6-11x higher cost).

4. **Hire or contract an Arabic-speaking Bagrut math teacher** for expert review. This person must be a licensed Israeli Bagrut teacher who teaches in Arabic, not a general Arabic language expert.

### 8.2 Architecture Decisions

5. **Arabic content is generated natively, not translated.** This is confirmed as the correct approach. The prompt templates should have Arabic-specific system prompts (not just glossary-swapped Hebrew prompts). The Socratic prompt currently says "Respond ONLY in Hebrew" -- an Arabic variant must be created.

6. **Math rendering is always LTR.** Enforce `direction: ltr` on all math components regardless of locale. Use Unicode bidi isolation for inline math in Arabic text. Reference Edraak's arabic-mathjax for implementation patterns.

7. **Integrate CAMeL Tools** (or Camel Morph MSA) into the content QA pipeline for Arabic morphological validation. This catches gender agreement errors that neither SymPy nor simple spell-check would detect.

8. **Create Arabic-specific prompt templates** for each LLM task (Socratic, answer evaluation, error classification, methodology switching). These should inject the Arabic glossary, use Arabic few-shot examples from real Bagrut exams, and include explicit instructions about gender agreement and diacritization.

### 8.3 Content Pipeline Additions

9. **Arabic corpus ingestion** should be a parallel workstream to Hebrew corpus ingestion, not a downstream translation step. Arabic Bagrut PDFs are publicly available from the same Ministry of Education source.

10. **Arabic question patterns** ("أثبت أن..." / "احسب..." / "أوجد...") should be extracted from the Arabic exam corpus, not assumed to be direct translations of Hebrew patterns. While mathematically identical, the phrasing conventions may differ subtly.

11. **Gender-aware generation.** Arabic prompts should include instructions for generating gender-neutral math content where possible (using passive voice or plural forms), and when addressing the student directly, the system should use the gender matching the student's profile setting.

### 8.4 Market and Strategic Implications

12. **Arabic-native content is a genuine market differentiator.** CET and most Israeli EdTech serve Arab students with translated Hebrew content. Cena's approach of generating from Arabic corpus data is novel in the Israeli market.

13. **The 5-unit math gap is the highest-impact target.** Arab-sector students who achieve 5-unit math Bagrut gain access to competitive STEM university programs. Cena should position its Arabic offering explicitly around enabling 5-unit math achievement.

14. **Future MENA expansion is linguistically feasible** because the core MSA math terminology is standardized across the Arab world. However, Bagrut-specific content (exam structure, scoring, topic ordering) would need adaptation for Jordanian Tawjihi, Egyptian Thanaweya Amma, etc.

---

## Sources

### Academic and Technical
- [W3C Arabic Mathematical Notation](https://www.w3.org/TR/arabic-math/arabic.xhtml) -- W3C Technical Note on Arabic math rendering
- [Issues of Rendering Arabic Mathematical Notation in Computer Software](https://cs.uwaterloo.ca/~smwatt/home/students/theses/MAlsheri2014-msc-project.pdf) -- Al-Sheri, 2014, University of Waterloo
- [Unicode Bidirectional Algorithm Basics](https://www.w3.org/International/articles/inline-bidi-markup/uba-basics) -- W3C International
- [MathML dir attribute](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Global_attributes/dir) -- MDN Web Docs
- [Statistical Error Analysis of Machine Translation: The Case of Arabic](https://www.scielo.org.mx/scielo.php?script=sci_arttext&pid=S1405-55462020000301053) -- Scielo, 2020
- [Errors and Non-errors in English-Arabic Machine Translation of Gender-bound Constructs](https://nchr.elsevierpure.com/en/publications/errors-and-non-errors-in-english-arabic-machine-translation-of-ge/) -- Elsevier
- [Evaluating Arabic Large Language Models: A Survey of Benchmarks, Methods, and Gaps](https://arxiv.org/html/2510.13430v1) -- arXiv, 2025
- [GATmath and GATLc: Comprehensive Benchmarks for Evaluating Arabic LLMs](https://journals.plos.org/plosone/article?id=10.1371/journal.pone.0329129) -- PLOS ONE, 2025
- [Mathematical Problem Solving in Arabic: Assessing Large Language Models](https://www.sciencedirect.com/science/article/pii/S187705092402982X) -- ScienceDirect
- [ArabicNumBench: Evaluating Arabic Number Reading in LLMs](https://arxiv.org/pdf/2602.18776) -- arXiv, 2026
- [Evaluating Various Tokenizers for Arabic Text Classification](https://www.researchgate.net/publication/362782617_Evaluating_Various_Tokenizers_for_Arabic_Text_Classification) -- ResearchGate
- [A Comprehensive Analysis of Various Tokenizers for Arabic LLMs](https://www.mdpi.com/2076-3417/14/13/5696) -- Applied Sciences, 2024
- [Camel Morph MSA: A Large-Scale Open-Source Morphological Analyzer for MSA](https://aclanthology.org/2024.lrec-main.240/) -- ACL Anthology, 2024
- [AraBERT: Transformer-based Model for Arabic Language Understanding](https://arxiv.org/abs/2003.00104) -- arXiv
- [Educational Quality Data for Arabic LLM Training](https://aclanthology.org/2025.arabicnlp-main.36.pdf) -- ArabicNLP 2025
- [3LM: A Benchmark for Arabic LLMs in STEM and Code](https://huggingface.co/blog/tiiuae/3lm-benchmark) -- Hugging Face / TII

### Israeli Education Data
- [Taub Center: Achievements and Gaps in the Education System](https://www.taubcenter.org.il/en/research/achievements-and-gaps-the-education-system-in-israel-a-status-report/) -- Taub Center, 2020
- [Taub Center: The Arab Education System in Israel -- Are the Gaps Closing?](https://www.taubcenter.org.il/en/research/the-arab-education-system-in-israel-are-the-gaps-closing/) -- Taub Center
- [Taub Center: The Education System in Israel 2020-2024](https://www.taubcenter.org.il/en/research/education-system-2024/) -- Taub Center, 2024
- [Taub Center: Raising the Bar -- Advanced Math and English](https://www.taubcenter.org.il/en/research/raising-the-bar-are-enough-israeli-students-taking-advanced-math-and-english/) -- Taub Center
- [OECD PISA 2022 Israel Profile](https://gpseducation.oecd.org/CountryProfile?primaryCountry=ISR&treshold=10&topic=PI) -- OECD
- [OECD Finds Record Educational Gulf](https://www.timesofisrael.com/global-test-widest-educational-gap-between-hebrew-arabic-speaking-israeli-kids/) -- Times of Israel
- [Israel TIMSS 2023 Encyclopedia](https://timss2023.org/wp-content/uploads/2024/10/Israel.pdf) -- TIMSS
- [K-12 Mathematics Education in Israel](https://www.worldscientific.com/worldscibooks/10.1142/10741) -- World Scientific
- [Education and Employment Among Young Arab Israelis](https://www.acitaskforce.org/wp-content/uploads/2024/02/resource-1560-1.pdf) -- Taub/Brookdale
- [The Correlation between Budgets and Matriculation Exams](https://www.mdpi.com/2227-7102/12/8/545) -- MDPI Education Sciences

### Tools and Platforms
- [CAMeL Tools](https://github.com/CAMeL-Lab/camel_tools) -- NYU Abu Dhabi, open-source Arabic NLP toolkit
- [NNLP-IL Arabic Resources](https://github.com/NNLP-IL/Arabic-Resources) -- Israeli NLP community Arabic resource list
- [Edraak arabic-mathjax](https://github.com/Edraak/arabic-mathjax) -- Open-source Arabic MathJax extension (MIT license)
- [Arabic LLM Benchmarks Repository](https://github.com/tiiuae/Arabic-LLM-Benchmarks) -- TII, list of Arabic benchmarks
- [Intel Hebrew Math Tutor](https://huggingface.co/Intel/hebrew-math-tutor-v1) -- Hugging Face, Hebrew math model (reference for translation approach limitations)
- [Modern Arabic Mathematical Notation](https://grokipedia.com/page/Modern_Arabic_mathematical_notation) -- Grokipedia overview

### Arabic Linguistic References
- [Arabic Grammatical Gender System](https://kalimah-center.com/gender-in-arabic/) -- Kalimah Center
- [Verb-Subject Agreement in Standard Arabic](https://www.tandfonline.com/doi/full/10.1080/23311983.2023.2268920) -- Taylor & Francis, 2023
- [The System of Arabic Terminology](https://westerneuropeanstudies.com/index.php/2/article/download/2502/1732) -- Western European Studies
