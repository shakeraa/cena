# Automated Quality Gate for AI-Generated Educational Questions

## Research Report — Cena Adaptive Learning Platform

**Date**: 2026-03-27
**Scope**: Automated scoring pipeline for AI-generated questions before human review
**Platform**: .NET 9 / Marten event store (PostgreSQL) / Proto.Actor / Hebrew + Arabic + English

---

## Executive Summary

This report synthesizes research across ten domains critical to building an automated question quality assessment system for Cena. The platform generates questions via LLMs for Israeli K-12 students in Hebrew and Arabic, aligned to the Bagrut curriculum. Questions are event-sourced with full version history, and AI-generated questions store their prompt, model, and raw output for reproducibility.

**Key findings:**

1. **Pipeline architecture**: Production systems universally follow a funnel — fast/cheap checks first, expensive LLM evaluation only for items that pass structural gates. Cena's existing 9-stage ingestion pipeline is the right shape; the quality gate inserts between "Re-Created" and "In Review."

2. **Per-dimension gates, not just composite scores**: Any single critical failure (e.g., factual inaccuracy, cultural insensitivity) should block regardless of composite score. Use hard gates per dimension with a weighted composite for borderline routing.

3. **LLM-as-judge works but has known biases**: LLM judges align with human judgment up to 85% (higher than human-human agreement at 81%). Critical failure modes include position bias (10-30% verdict flip), verbosity bias, self-preference bias, and confidence bias. Mitigate with cross-model verification, structured rubrics, and gold reference grounding.

4. **Arabic content quality is systematically lower**: Arabic remains underrepresented in LLM post-training. Arabic-specific models (Jais, AceGPT) still lag behind GPT-4/Claude on Arabic tasks. Score languages independently; apply higher scrutiny to Arabic content.

5. **Distractor evaluation is an unsolved problem pre-deployment**: Automated distractor quality assessment has no community consensus on reliable evaluation techniques. The most reliable signal comes from post-hoc student response analysis. Pre-deployment LLM evaluation is a reasonable heuristic but not definitive.

6. **IRT calibration needs 200+ responses**: Start with Elo (already implemented), transition to 2PL IRT after sufficient data. Flag items with negative point-biserial correlation immediately.

7. **Cost optimization can achieve 80%+ savings**: Tiered model routing (40-60%), semantic caching (20-40%), batch API (50% discount), and prompt optimization (15-30%) compound to ~$13/month vs ~$75/month unoptimized.

---

## Part I: Scoring Dimensions

### 1. Factual Accuracy

**What it measures**: Is the correct answer actually correct? Are distractors definitively wrong?

**Scoring method**:
- **Math/Science**: Deterministic symbolic solver (SymPy equivalent) for numeric answers. LLM fallback for conceptual questions.
- **Cross-verification**: Generate with Model A, verify with Model B (different family). Temperature 0 for evaluation.
- **Gold reference grounding**: Always provide the expected correct answer and key-stem relationship to the judge model.

**Research finding**: A second LLM call reliably verifies factual accuracy when given structured rubrics and reference answers. Without grounding, LLMs reward confident-sounding but incorrect answers (confidence bias). Providing gold references significantly reduces hallucination blind spots (Li et al., 2024, "A Survey on LLM-as-a-Judge," arXiv:2411.15594).

**Threshold**: Auto-reject < 70 | Review 70-89 | Auto-approve >= 90

### 2. Bloom's Taxonomy Alignment

**What it measures**: Does the question actually test the claimed cognitive level?

**Scoring method**:
- LLM zero-shot classification: GPT-4o-mini achieves 0.73 accuracy; Gemini-1.5-pro 0.72; Claude-3.5-haiku 0.58 (Computers & Education: AI, 2024).
- Supervised ML (with training data): SVM + synonym augmentation achieves 0.94; Bayesian-optimized ensemble up to 0.935 (Springer Nature 2024; MDPI Electronics 2025).
- **Recommendation**: Collapse 6 Bloom's levels to 3 for reliability — Lower-Order (Remember + Understand), Middle-Order (Apply + Analyze), Higher-Order (Evaluate + Create). Map to Bagrut's 4-level "thinking levels" system.

**Known confusion patterns**: Most errors occur between adjacent levels (Remember/Understand, Apply/Analyze). Create and Evaluate are hardest to classify. Mathematical questions are particularly difficult because "Apply" in math (plug into formula) differs fundamentally from "Apply" in biology.

**Research gap**: No validated Bloom's classification exists for Hebrew or Arabic educational content.

### 3. Difficulty Calibration

**What it measures**: Is the stated difficulty consistent with actual cognitive load?

**Three-phase approach**:

| Phase | Data Available | Method | Accuracy |
|-------|---------------|--------|----------|
| Pre-deployment | Zero student data | LLM prediction + feature-based (Bloom's level, word count, prerequisite depth) | Rough estimate |
| Early calibration | 1-100 responses | Elo dual-update (already in `EloScoring.cs`) | Correlation 0.927 with IRT (Pelanek, 2016) |
| Stable calibration | 200+ responses | Transition to 2PL IRT; AutoIRT for automated calibration (arXiv:2409.08823) | High confidence |

**Key finding**: The Elo system performs comparably to the Rasch model for adaptive practice, with correlations of 0.927 between Elo difficulty estimates and IRT difficulty estimates. However, when items are selected adaptively and difficulties are updated alongside student abilities, variance artificially increases over time. Mitigate with Glicko (uncertainty tracking) or regularization.

**Calibration curve**: Plot predicted difficulty (quality gate) vs actual difficulty (Elo after 250+ responses). Systematic deviations indicate LLM prediction model needs retraining; idiosyncratic deviations indicate item presentation problems.

### 4. Distractor Quality

**What it measures**: Do wrong answers represent real misconceptions (not obviously wrong)?

**What makes a good distractor** (ACL 2025, arXiv:2501.13125; BEA Workshop 2025):
- **Plausibility**: Represents a common misconception or calculation error at the target level
- **Non-inferrability**: Cannot be eliminated through test-taking strategies alone
- **Homogeneity**: Similar length, complexity, and grammatical structure to the correct answer
- **Diagnostic value**: Selecting it reveals a specific misconception for targeted remediation

**Pre-deployment evaluation** (LLM-as-judge):
1. Verify each distractor is factually wrong
2. Verify each distractor is plausible at the target level
3. Verify no distractor is eliminable by test-taking strategy
4. Verify distractors are semantically distinct from each other (embedding check)

**Post-deployment metrics** (the reliable signal):

| Metric | Good Range | Flag Threshold |
|--------|-----------|---------------|
| Distractor selection rate | 5-30% each | < 5% (non-functional) |
| Point-biserial of distractor | Negative | Positive (attracting high-ability students) |
| Distractor-total correlation | < 0 | > 0 |

**Critical gap**: "While automated distractor generation has received considerable attention, automated evaluation of distractor quality remains a largely unexplored frontier." (BEA Workshop 2025, ACL Anthology). LLM judgment of "plausibility" does not reliably predict actual student selection patterns.

### 5. Stem Clarity

**What it measures**: Is the question unambiguous, well-formed, and free of answer-revealing cues?

**Automatable Haladyna rules** (from 31 validated guidelines, Haladyna & Downing 1989, 2002):
- Stem presents a single, clearly formulated problem
- No negative wording (or explicitly flagged if present)
- No grammatical cues that reveal the answer
- Options similar in length (max 1.5x ratio between shortest and longest)
- Correct answer position randomized
- No "all of the above" / "none of the above"
- Vocabulary appropriate for target grade level

~15 of 31 Haladyna rules are directly automatable via regex + heuristics (no LLM needed).

### 6. Cultural/Linguistic Sensitivity

**What it measures**: Appropriate for Hebrew-dominant, Arabic-dominant, and bilingual students?

**Key considerations for Israel**:
- Religious sensitivities (Jewish holidays, Islamic observances, Shabbat references)
- Military service references (mandatory for Jewish citizens, not for Arab citizens)
- Political references (Israeli-Palestinian conflict, settlements)
- Gender norms (vary across secular, religious, and Arab communities)
- Dietary examples (kosher/halal considerations)

**LLM limitation**: No LLM handles Israeli-specific cultural sensitivity natively. Requires an explicit cultural sensitivity checklist built into the evaluation prompt.

**Bilingual specifics**:
- Hebrew and Arabic versions of the same question may carry different connotations
- Mathematical expressions in RTL text require bidirectional algorithm handling
- Content generated for the "Jewish sector" may be inappropriate for the "Arab sector" and vice versa

**Threshold**: Hard gate at 50 — any item below 50 on cultural sensitivity is auto-rejected regardless of composite score.

### 7. Curriculum Alignment

**What it measures**: Does the question test the claimed concept within Bagrut scope?

**Bagrut structure** (Israel's matriculation exam):
- Subjects tested at 3, 4, or 5 "units of study" (difficulty levels)
- Students need 21+ total units, including at least one 5-unit subject
- Mandatory: Hebrew/Arabic, English, Mathematics, Bible/Quran, History, Civics
- Passing threshold: 55% on each exam
- Ministry of Education writes all compulsory exams (national standard)

**Validation approach**: Map each question to a specific Bagrut unit-level curriculum scope. A 3-unit math question testing calculus concepts is misaligned and should be flagged.

### 8. Additional Dimensions to Consider

**Translation Equivalence** (for bilingual items): When Hebrew and Arabic versions exist, verify semantic equivalence — not just syntactic translation. This is a Tier 3 (LLM) evaluation. Use cross-lingual embedding similarity as a fast pre-check.

**RTL Rendering Correctness**: Both Hebrew and Arabic are RTL. Validate proper directionality markers in math-embedded content. Structural check, no LLM needed.

**Response Time Reasonableness**: Cena's `MasteryQualityClassifier` already classifies responses into fast/slow x correct/incorrect. An item with unusually high "Careless" rate (fast + incorrect) may have a misleading stem. High "Effortful" rate (slow + correct) may indicate unnecessarily complex presentation.

---

## Part II: Pipeline Architecture

### Recommended Funnel Design

Production content moderation systems universally follow a funnel: fast/cheap filters first, expensive evaluations only for items that pass earlier stages. This matches the existing 9-stage ingestion pipeline in `IngestionPipelineService.cs`.

```
                    ┌─────────────────────────┐
                    │   LLM Question Output    │
                    └────────────┬────────────┘
                                 ▼
                    ┌─────────────────────────┐
 Stage 0 (<1ms)    │   Deduplication Check    │──── Hash match → Auto-reject
                    │   (content hash + pgvector)│
                    └────────────┬────────────┘
                                 ▼
                    ┌─────────────────────────┐
 Stage 1 (<5ms)    │  Structural Validation   │──── Missing options, bad format → Auto-reject
                    │  (Haladyna rules, regex) │
                    └────────────┬────────────┘
                                 ▼
                    ┌─────────────────────────┐
 Stage 2 (50-200ms)│  Lightweight ML/Haiku    │──── Language quality, basic pedagogy
                    │  (grammar, spelling,     │     Difficulty pre-estimation
                    │   plagiarism embedding)  │
                    └────────────┬────────────┘
                                 ▼
                    ┌─────────────────────────┐
 Stage 3 (1-5s)    │  Deep LLM Evaluation     │──── Math correctness, Bloom's alignment
                    │  (Sonnet/Opus tier,      │     Distractor quality, cultural sensitivity
                    │   cross-model verify)    │     Translation equivalence
                    └────────────┬────────────┘
                                 ▼
                    ┌─────────────────────────┐
 Gate Decision      │  Threshold Engine        │
                    │  Per-dimension gates +   │
                    │  Weighted composite      │
                    └──┬─────────┬─────────┬──┘
                       ▼         ▼         ▼
                 Auto-approve  Review   Auto-reject
                 (Published)  (InReview) (Deprecated)
```

### Stage Details

**Stage 0 — Deduplication Check** (sub-millisecond)
- Content hash against known items in PostgreSQL
- Embedding similarity against item bank via pgvector
- Threshold: similarity >= 0.92 → auto-reject; 0.85-0.92 → flag for review
- Per-concept similarity only (avoid flagging different questions with similar math notation)
- Use multilingual embedding model (`multilingual-e5-large` or `BGE-M3`) for cross-lingual dedup

**Stage 1 — Structural Validation** (rule-based, < 5ms)
- Stem non-empty, correct number of options (exactly 4 for MCQ)
- Exactly one correct answer marked
- RTL/BiDi markers present for Hebrew/Arabic math content
- Haladyna automatable rules (~15 of 31)
- Unit-level curriculum scope check
- LaTeX/math rendering validation
- Character encoding validation (Unicode normalization)

**Stage 2 — Lightweight ML** (Haiku-tier, ~$0.0002/call)
- Language quality (grammar, spelling per language)
- Plagiarism embedding similarity
- Basic pedagogical structure
- Difficulty pre-estimation
- Bloom's taxonomy initial classification (3-level)

**Stage 3 — Deep Evaluation** (Sonnet/Opus-tier, ~$0.003-0.015/call)
- Mathematical correctness verification (for complex items)
- Distractor quality analysis
- Bloom's taxonomy deep classification (verification of Stage 2)
- Cultural sensitivity (language-aware prompt with explicit Israeli checklist)
- Translation equivalence (for multi-language items)

**Only items that pass Stages 0-1 and are not cached from Stage 2 reach Stage 3.**

### Gate Decision Logic

**Per-dimension thresholds with hard gates:**

| Dimension | Auto-Reject | Needs Review | Auto-Approve |
|-----------|------------|-------------|-------------|
| Factual Accuracy | < 70 | 70-89 | >= 90 |
| Language Quality | < 65 | 65-84 | >= 85 |
| Pedagogical Quality | < 60 | 60-79 | >= 80 |
| Distractor Quality | < 55 | 55-74 | >= 75 |
| Cultural Sensitivity | < 50 | — | >= 50 |
| Embedding Uniqueness | >= 0.92 | 0.85-0.92 | < 0.85 |
| Composite Score | < 65 | 65-84 | >= 85 |

**Hard gates**: If ANY dimension falls below its auto-reject threshold, the item is rejected regardless of composite.

**Cultural sensitivity is a binary hard gate**: Below 50 = reject. No "review" zone.

**Composite formula**:
```
Composite = 0.30 × FactualAccuracy
          + 0.20 × LanguageQuality
          + 0.20 × PedagogicalQuality
          + 0.15 × DistractorQuality
          + 0.10 × CulturalSensitivity
          + 0.05 × (100 - PlagiarismScore)
```

### Initial Deployment Strategy

Start with **tight thresholds** (high auto-reject, low auto-approve). Accept a high false-positive rate. Send 40-60% of items to human review.

After 500-1000 human-reviewed items, measure false-positive rate and adjust. Target: <5% false negatives, <20% false positives.

**Rationale**: In educational content, false negatives are more dangerous than false positives. A bad question served to students causes measurable harm (wrong answers reinforced, confidence drops, discrimination data polluted). A false positive merely adds to the review queue.

---

## Part III: Bilingual Considerations

### Arabic Quality Gap

**The problem is real and documented:**
- Arabic is morphologically rich — a single word encodes tense, gender, number, possession — increasing data sparsity
- When training data contains little Arabic, tokenizers split unrecognized Arabic words into single characters, limiting generation quality (arXiv:2510.13430; arXiv:2603.15773)
- Even Arabic-centric LLMs (Jais, AceGPT, ArabianGPT) still lag behind GPT-4/Claude on Arabic tasks
- Key reason: Arabic remains underrepresented in post-training (RLHF/DPO) efforts (arXiv:2507.14688)

**Hebrew has even less coverage:**
- Fewer native speakers (~9M vs ~400M for Arabic) means less training data
- No published Hebrew-specific educational question quality benchmarks exist
- Shares Semitic morphological complexity with Arabic

### Recommendations

1. **Score languages independently**: Do not assume equal LLM quality across languages
2. **Arabic gets higher scrutiny**: Sample 20% of Arabic content for human review vs 10% for Hebrew
3. **Separate Elo ratings per language variant**: An item at 4-unit difficulty in Hebrew may have different actual difficulty in Arabic due to linguistic complexity differences
4. **Cross-lingual consistency check**: When Hebrew and Arabic versions exist, verify mathematical/scientific content is identical (embedding-based cross-lingual similarity)
5. **Tokenization monitoring**: Track token counts for same content across languages. If Arabic tokenization produces >2x more tokens than Hebrew, the embedding model may be suboptimal
6. **Use GPT-4 or Claude for generation** (not Arabic-specific models) — they outperform in practice

### Bagrut Bilingual Context

Per Israeli education system structure (TIMSS 2019 Encyclopedia):
- Mathematics and science exams are written in Hebrew and translated to Arabic
- Arab sector requires Arabic grammar + Hebrew as second language
- All learning materials written in Hebrew are translated to Arabic to ensure similar instruction
- National tests translated from English into both Hebrew and Arabic

**Implication**: Translation quality must be a scored dimension. The quality gate must verify semantic equivalence, not just syntactic translation.

---

## Part IV: Feedback Loop Design

### Post-Deployment Metrics

Implement as a **Marten projection** (`QuestionPerformanceProjection`) processing `ConceptAttempted_V1` events:

**After 30 responses** (rough signal):
- If accuracy < 0.10 or > 0.95 → flag (likely too hard/easy)
- If point-biserial < 0.0 → auto-deprecate with `QualityAlertRaised_V1`

**After 100 responses** (moderate confidence):
- If rpb < 0.15 → flag for review
- If any distractor selected by 0 students → flag (non-functional distractor)
- Update Elo difficulty estimate
- Compute discrimination index (upper/lower 27% method)

**After 250 responses** (high confidence):
- Full statistical profile with confidence intervals
- Compare predicted difficulty (quality gate) vs actual Elo difficulty
- If |predicted - actual| > 0.3 → flag for difficulty recalibration
- Transition to 2PL IRT calibration
- DIF analysis between Hebrew and Arabic cohorts

### Point-Biserial Correlation (rpb)

The core quality signal. Measures correlation between item performance and overall ability.

| rpb Range | Interpretation | Action |
|-----------|---------------|--------|
| >= 0.30 | Highly discriminating | Keep |
| 0.20 - 0.29 | Acceptable | Monitor |
| 0.15 - 0.19 | Marginal | Flag for review |
| < 0.15 | Poor | Flag for revision |
| < 0.00 | Negative — likely miskeyed or misleading | Auto-deprecate immediately |

### Difficulty Auto-Correction

Build a calibration curve: predicted difficulty → actual difficulty.

If students consistently get a "medium difficulty" question wrong (>80% failure after 100+ responses), auto-flag for review with `QualityAlertRaised_V1` event. The event should include:
- Predicted difficulty vs actual
- Sample size
- Response distribution across options
- Point-biserial correlation

Over time, the accumulated calibration data enables systematic correction of the LLM difficulty predictor.

### IRT Sample Size Requirements

| Model | 10 Items | 20 Items | 30 Items |
|-------|---------|---------|---------|
| 2PL | N >= 750 | N >= 500 | N >= 250 |
| 3PL | N >= 750 | N >= 750 | N >= 350 |

For Cena's K-12 population, individual items may take weeks to accumulate 200+ responses. Prioritize high-traffic items for IRT calibration. Use Bayesian hierarchical priors (from LLM-estimated difficulty + Bloom's level + subject area) to bootstrap parameters.

---

## Part V: Cost Optimization

### Tiered Model Routing (40-60% savings)

60-80% of quality gate evaluations are routine items that cheaper models handle well:

| Tier | Model | Cost | Use When |
|------|-------|------|----------|
| 0 | No LLM | $0 | Structural validation, dedup, language detection |
| 1 | Haiku-class | ~$0.0002 | Language quality, basic pedagogy, difficulty estimation |
| 2 | Sonnet/Opus | ~$0.003-0.015 | Math correctness, cultural sensitivity, complex Bloom's |

Route based on complexity: straightforward MCQ with numeric answers → Tier 1. Proofs, Hebrew wordplay, cross-cultural references → Tier 2.

### Semantic Caching (20-40% savings)

Questions with similar stems (cosine similarity > 0.90) produce similar quality evaluations. Cache the assessment, not just the content. Use pgvector (already available via PostgreSQL/Marten).

Research shows semantic caching reduces API calls by up to 68.8% (arXiv:2411.05276).

### Batch Processing (50% discount)

Both OpenAI and Anthropic offer batch API at 50% discount for higher-latency workloads. Use for:
- Nightly re-evaluation of existing items
- Bulk quality assessment during content imports
- Only real-time ingestion needs synchronous evaluation

### Prompt Optimization (15-30% savings)

- Structured output schemas (JSON mode) reduce token waste
- Anthropic prompt caching: 90% reduction on cached system prompt tokens
- Send only stem, options, metadata — not full HTML

### Projected Cost

| Scenario | Without Optimization | With Optimization |
|----------|---------------------|-------------------|
| New items (50/day, sync) | $0.50/day | $0.115/day |
| Re-evaluation (1500, batch, weekly) | $15/week | $2.33/week |
| **Monthly total** | **~$75/month** | **~$13/month** |

---

## Part VI: Industry References

### How Major Platforms Handle This

**Duolingo**: Uses "Birdbrain" (logistic regression IRT model) for continuous difficulty estimation. CEFR levels integrated into AI content generation. AQuAA quality assurance system validates content before publishing. ML models "jump-start" item parameters pre-deployment. Difficulty decomposed into component features (exercise type, vocabulary, grammar concepts).

**Khan Academy**: Mastery-based learning with exercise sets tagged to Common Core. Difficulty implicitly calibrated through completion rates and mastery thresholds rather than formal IRT.

**IXL**: Real-time adaptive algorithms adjusting difficulty from live performance. Algorithmically generated questions ensure uniqueness. Built-in diagnostic mapping strengths/weaknesses to specific skills.

**ALEKS**: Knowledge Space Theory — models knowledge as a partially ordered set of concepts. Categorizes items as "in-state" (>80% mastery likelihood) or "out-of-state" (<20% likelihood). 25-30 adaptive questions for initial assessment.

**Century Tech** (UK): Neuroscience-informed AI combining knowledge graphs with spaced repetition. Proprietary quality assessment.

---

## Part VII: Known Limitations and Failure Modes

### Automated Quality Assessment Limitations

1. **Haladyna's guidelines validated primarily for English** — cross-linguistic applicability to Hebrew/Arabic requires additional validation
2. **LLM-as-judge cannot reliably detect subtle factual errors** in specialized domains (advanced Bagrut physics) without domain grounding
3. **Arabic content evaluation is significantly weaker** than English across all tested LLMs
4. **No LLM handles Israeli-specific cultural sensitivity natively** — requires explicit guidelines
5. **Bloom's taxonomy is inherently subjective** — inter-rater agreement among human experts is only 70-85%
6. **Distractor evaluation pre-deployment is an unsolved problem** — LLM plausibility judgment doesn't predict actual student selection patterns
7. **Mathematical notation poorly handled by embedding models** — LaTeX/Unicode math symbols cause similarity scoring artifacts
8. **IRT assumes unidimensionality** — may not hold for complex Bagrut questions crossing concept boundaries
9. **Position bias in LLM-as-judge**: 10-30% verdict flip when response order is swapped
10. **Verbosity bias**: LLMs prefer longer responses, even with identical content

### Mitigation Strategies

- Cross-model verification (generate with A, judge with B)
- Temperature 0 for all evaluation calls
- Gold reference grounding for factual checks
- 5-10% human sampling for continuous calibration
- Separate mathematical structure encoding for embedding similarity
- Explicit Israeli cultural checklist in evaluation prompts

---

## Part VIII: MVP Implementation Order

### Phase 1 — Structural Validation (Week 1-2)
- Implement Stage 0 (hash dedup) and Stage 1 (Haladyna rules, structural checks)
- Zero LLM cost, catches ~30% of bad items
- Wire into existing ingestion pipeline between "Re-Created" and "In Review"
- Emit `QualityScoreComputed_V1` events to Marten

### Phase 2 — Embedding Deduplication (Week 3-4)
- Set up pgvector index on question bank
- Implement multilingual embedding generation (`multilingual-e5-large` or `BGE-M3`)
- Three-tier similarity thresholds (exact match, near-duplicate, overly-similar)
- Prevents question bank pollution

### Phase 3 — LLM-as-Judge (Week 5-8)
- Implement tiered evaluation (Haiku for routine, Sonnet for complex)
- Structured rubric prompts for each dimension
- Cross-model verification for factual accuracy
- Cultural sensitivity checklist for Hebrew/Arabic
- Semantic caching with pgvector

### Phase 4 — Feedback Loop (Week 9-12)
- Marten projection for `ConceptAttempted_V1` events
- Running statistics: accuracy, rpb, discrimination index
- Auto-flagging at 30/100/250 response thresholds
- Calibration curve dashboard in admin panel
- DIF analysis between Hebrew/Arabic cohorts

### Phase 5 — IRT Calibration (Month 4+)
- Transition high-traffic items from Elo to 2PL IRT
- AutoIRT for automated parameter estimation
- Bayesian hierarchical priors for cold-start items
- Quarterly human review of flagged items

---

## Part IX: Proposed Event Schema

```csharp
// Quality gate events (store in Marten)
record QualityScoreComputed_V1(
    string QuestionId,
    QualityScores Scores,
    string GateDecision,         // "auto-approved" | "needs-review" | "auto-rejected"
    string ModelUsed,
    string ModelTier,            // "none" | "haiku" | "sonnet" | "opus"
    bool CacheHit,
    DateTimeOffset Timestamp);

record QualityGateThresholdUpdated_V1(
    string Dimension,
    float OldThreshold,
    float NewThreshold,
    string Rationale,
    string UpdatedBy,
    DateTimeOffset Timestamp);

// Feedback loop events
record QualityAlertRaised_V1(
    string QuestionId,
    string AlertType,            // "low-discrimination" | "extreme-difficulty" | "non-functional-distractor" | "negative-rpb"
    string Details,
    int SampleSize,
    DateTimeOffset Timestamp);

record DifficultyRecalibrated_V1(
    string QuestionId,
    float PredictedDifficulty,
    float ActualDifficulty,
    int SampleSize,
    float Confidence,
    DateTimeOffset Timestamp);

record QuestionAutoDeprecated_V1(
    string QuestionId,
    string Reason,
    float DiscriminationIndex,
    int SampleSize,
    DateTimeOffset Timestamp);
```

### Extended QualityScores Record

```csharp
public sealed record QualityScores(
    int MathCorrectness,
    int LanguageQuality,
    int PedagogicalQuality,
    int PlagiarismScore,
    int DistractorQuality,
    int CulturalSensitivity,
    int BloomAlignment,
    float CompositeScore,
    bool PassesGate,
    string GateDecision);       // "auto-approved" | "needs-review" | "auto-rejected"
```

---

## Part X: Key Citations

### Rubrics and Item Writing
- Haladyna, T.M. & Downing, S.M. (1989). "A Taxonomy of Multiple-Choice Item-Writing Rules." *Applied Measurement in Education*, 2(1), 37-50.
- Haladyna, T.M., Downing, S.M., & Rodriguez, M.C. (2002). "A Review of Multiple-Choice Item-Writing Guidelines for Classroom Assessment." *Applied Measurement in Education*, 15(3), 309-334.
- Interactive Learning Environments (2025). "Automatic item generation for educational assessments: a systematic literature review."

### LLM-as-Judge
- Li, J. et al. (2024). "A Survey on LLM-as-a-Judge." arXiv:2411.15594.
- Zheng, L. et al. (2024). "Judging LLM-as-a-Judge with MT-Bench and Chatbot Arena." NeurIPS 2023.
- Naismith, B. et al. (2024). "Open Source LLMs Can Provide Feedback." ITiCSE 2024.
- AAAI Spring Symposium (2024). "Enhancing Fairness in LLM Evaluations: Unveiling and Mitigating Biases."

### IRT and Calibration
- Schroeders, U. & Gnambs, T. (2025). "Sample-Size Planning in Item Response Theory: A Tutorial." *Advances in Methods and Practices in Psychological Science*.
- Pelanek, R. (2016). "Applications of the Elo Rating System in Adaptive Educational Systems." *Computers & Education*.
- AutoIRT (2024). "Calibrating Item Response Theory Models with Automated Machine Learning." arXiv:2409.08823.
- Shen (2024). "A two-step item bank calibration strategy." *British Journal of Mathematical and Statistical Psychology*.

### Embedding Deduplication
- SemHash (2025). GitHub: MinishLab/semhash.
- Reimers, N. & Gurevych, I. (2019). "Sentence-BERT." EMNLP 2019.
- arXiv:2505.04916 (2025). "An Open-Source Dual-Loss Embedding Model for Semantic Retrieval in Higher Education."

### Bloom's Taxonomy Classification
- "Analysis of LLMs for educational question classification and generation." *Computers & Education: AI* (2024).
- "Towards Smarter Assessments: Bloom's Classification with Bayesian-Optimized Ensemble." *Electronics* (2025).
- "LLMs meet Bloom's Taxonomy." COLING 2025.
- "Automated Analysis of Learning Outcomes Based on Bloom's Taxonomy." arXiv:2511.10903 (2025).

### Arabic/Hebrew LLM Quality
- arXiv:2510.13430 (2024). "Evaluating Arabic Large Language Models: Benchmarks, Methods, and Gaps."
- arXiv:2603.15773 (2025). "Morphemes Without Borders: Arabic Tokenizers and LLMs."
- arXiv:2507.14688 (2025). "A Review of Arabic Post-Training Datasets and Their Limitations."
- *Computers & Education: AI* (2025). "How well can LLMs grade essays in Arabic?"

### Distractor Quality
- ACL 2025. "Generating Plausible Distractors via Student Choice Prediction." arXiv:2501.13125.
- BEA Workshop 2025. "A Survey on Automated Distractor Evaluation." ACL Anthology.
- EMNLP 2024. "Distractor Generation Survey." arXiv:2402.01512.

### Pipeline Architecture
- Pelanek (2016). "Applications of Elo Rating in Adaptive Educational Systems." PMC.
- Duolingo Research Blog. "Birdbrain" and "AQuAA" quality assurance systems.
- ALEKS. Knowledge Space Theory documentation.
- TIMSS 2019 Encyclopedia — Israel chapter.
- Bagrut certificate documentation (Jewish Virtual Library; Shivat Zion).

### Cost Optimization
- arXiv:2411.05276 (2024). "GPT Semantic Cache."
- Redis Blog. "What is Semantic Caching?"
- PremAI Blog (2026). "LLM Cost Optimization: 8 Strategies That Cut API Spend by 80%."
