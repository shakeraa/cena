---
persona: finops
subject: STUDENT-INPUT-MODALITIES-001
date: 2026-04-21
verdict: red
---

## Summary

Q1 (photo-of-work) and Q3 (rubric-graded long-answer) each force **Tier 3 Sonnet** routing with near-zero prompt-cache benefit on the variable payload. My central-case estimate: **+$1.40–$2.10/student/month** on top of the already-committed $3.30 from ADR-0050 Q5 — that is a **42–64% budget overrun** and by itself breaches the $30k global cap at 10k paying students. Verdict **red** unless we impose per-feature per-student monthly caps and ship hash-dedupe for photos before Q1 reaches >5% of the student population. The $3.30 ceiling was negotiated under the assumption that modality stayed MC + MathLive; vision + rubric-graded freeform invalidates that assumption.

## Section 7.10 — hard numbers

### Q1 vision-model cost per photo call (Claude Sonnet vision)

Per-call token accounting for "diagnose my paper working":
- **Input**: ~1 image (1024×1024 tile ≈ 1,600 vision tokens on Claude Sonnet) + system prompt (~1,200 text tokens, cacheable) + OCR text fallback (~300 tokens) + student context tail (~200 tokens, variable) ≈ **~3,300 input tokens** effective, of which ~1,400 cache-eligible.
- **Output**: ~300–500 tokens (diagnostic response: "I see you factored correctly but made a sign error on line 3").
- **Pricing (Sonnet)**: input $3/MTok, output $15/MTok, cached-read $0.30/MTok.
- **Per-call cost**:
  - Cold (no cache): 3,300 × $3/MTok + 400 × $15/MTok ≈ **$0.0099 + $0.0060 = ~$0.016/call**.
  - Warm (cache hit on static prefix): 1,900 × $3 + 1,400 × $0.30 + 400 × $15 ≈ **~$0.0121/call**.
  - Realistic blended (PRR-047 ~70% hit): **~$0.013/call**.

**Per-student-month at N photos**:
| Usage | $/student/month |
|---|---|
| 2 photos/mo (conservative, narrow framing, low uptake) | ~$0.026 |
| 5 photos/mo (brief's example) | ~$0.065 |
| 15 photos/mo (broad framing, engaged student) | ~$0.20 |
| 40 photos/mo (broad framing, cram-week spike) | ~$0.52 |

**At 10k paying students** the aggregate runs **$260–$5,200/month**. The brief's "$0.10/photo × 5 × 100k = $50k/month" estimate **understates per-call cost by 4×** (the real per-call is ~$0.013, not $0.10) but **overstates volume** (100k paying is not realistic at launch). Order of magnitude at 10k: worst-case ~$5k/month just on photo calls. Tolerable if narrow framing.

### Q3 rubric-graded long-answer per item

Per-call:
- **Input**: rubric DSL (~800 tokens, cacheable per rubric) + question stem (~200 tokens, cacheable per item) + student free-form answer (~300–1,200 tokens, **unique every call — uncacheable**) + system prompt (~1,000 cacheable) ≈ **~2,300–3,200 input tokens**, ~2,000 cache-eligible, ~300–1,200 uncacheable.
- **Output**: ~400–800 tokens (rubric scoring + per-criterion justification).
- **Per-call cost** (Sonnet, assume 60% cache hit — lower than Q1 because student answer dominates the input): **~$0.015–$0.024/call, central $0.019**.

**Expected cache hit rate: 50–65%**, meaningfully below the PRR-047 70% SLO floor. Harsh-honest: **rubric grading will miss the cache SLO**. Budget a separate SLO tier for it (see recommended task F2 below).

**Per-student-month**:
| Usage | $/student/month |
|---|---|
| 2 long-answers/mo (math-primary student) | ~$0.04 |
| 8 long-answers/mo (humanities Bagrut student) | ~$0.15 |
| 25 long-answers/mo (literature-heavy, essay prep) | ~$0.48 |

### Total budget delta on top of $3.30

**Central case** (Q1 narrow framing 5 photos/mo + Q3 8 long-answers/mo): **+$0.22/student/month** → new run-rate $3.52 (+6.6%). Survivable.

**Aggressive case** (Q1 broad framing 15 photos + Q3 25 long-answers): **+$0.68/student/month** → $3.98 (+21%). Eats all remaining headroom in the $20/student cap but clears it.

**Cram-week + broad framing worst-case** (40 photos + 25 long-answers): **+$1.00–$1.40/student/month** → $4.30–$4.70. **Breaches $30k global cap at 10k students by ~$14k/month** (43k vs 30k). Not acceptable without per-feature daily caps.

**Verdict: narrow framing for Q1 is not a nice-to-have, it is the cost-viability precondition.** Broad framing must be gated behind a premium tier or disabled.

## Does vision call force Tier 3?

**Yes, effectively.** Claude Haiku 3.5 does have a vision path but:
- Handwriting OCR + spatial reasoning ("line 3 of the working, sign error") is the weak spot of Haiku vision; empirical failure rate on math-working diagnosis is high enough that student-facing quality would embarrass the product.
- Cost saving is smaller than it looks: Haiku vision is ~$1/MTok input (vs Sonnet $3) — a 3× saving on the input side, but the output tokens (which dominate when diagnostic text is ~400 tokens) price at $5 vs $15, also 3×. So Haiku would be ~$0.004–$0.005/call vs Sonnet $0.013 — ~3× cheaper, but with noticeably degraded diagnostic quality.
- **Recommendation**: keep Sonnet on Q1 vision. Do NOT route Q1 to Haiku to save money; route to Haiku only if we want to reject the feature and ship a worse fallback.
- **Tier 1 (Agent Booster / WASM)**: not applicable — this is a vision+reasoning task, no deterministic transform exists.

## Cache strategy for photo uploads

**Hash-dedupe strategy (strong recommend)**:
- Compute SHA-256 of the decoded image after EXIF-strip + perceptual downscale to 512×512 grayscale.
- Store hash → (OCR output, vision-diagnosis output) in a per-student or per-session KV with 30-day TTL (matches ADR-0003 session scope).
- If student re-uploads the same photo (common when retrying an already-diagnosed problem, or when network retries trigger dup uploads), serve cached result — **zero LLM call**.
- Expected duplicate rate: **15–25%** based on comparable educational photo workflows (students repeat-upload when unsure the first upload "worked"). At 5 photos/month baseline, that is ~1 photo/month saved at ~$0.013/call = **$0.003/student/month saving**. Modest.
- **Real win**: perceptual hash (pHash, not just SHA) across students. If the same textbook problem worked by many students produces near-identical photos of the same printed problem, a cross-student perceptual-hash cache could save 40–60% of calls. Requires careful privacy review (cross-student cache = photos leave session scope, **breaches ADR-0003**). **Do not ship cross-student cache without a privacy ADR.**
- **Ship**: per-student SHA-dedupe. **Do not ship**: cross-student perceptual dedupe without ADR.

## Section 8 positions

1. **Q1 framing (narrow vs broad)**: **Hard-vote narrow.** Broad framing breaks the budget at any non-trivial adoption; narrow framing keeps cost floor at ~$0.03/student/month. Not negotiable on cost grounds.
2. **Q2 hide-then-reveal**: zero LLM cost impact. finops-neutral; defer to cogsci/educator.
3. **Q3 architecture shared vs per-subject**: shared `FreeformInputField<T>` is cheaper to maintain but does not change LLM cost. Neutral.
4. **Q3 chem Launch-scope**: MC-only at Launch **saves** ~$0.05–$0.10/student/month but ships degraded pedagogy. Trade is not finops's call.
5. **Q3 humanities Launch-scope**: same as 4.
6. **Tier bump for premium users**: **Yes, strongly recommended.** Split Q1 broad framing + Q3 unlimited into a premium SKU at ≥$18/month. Basic SKU caps Q1 at 5 photos/month + Q3 at 8 long-answers/month. Premium SKU absorbs 40-photo + 25-long-answer workloads. This is how we square "pedagogy wants broad" with "budget wants narrow". Experiment design in F3 below.

## Recommended new PRR tasks

1. **PRR-NEW-F1 — Per-feature per-student monthly caps.** Hard caps enforced at the routing layer: `photo_diagnosis_calls_per_student_per_month ≤ 10` (basic) / 50 (premium); `rubric_grading_calls_per_student_per_month ≤ 15` / 50. Circuit breaker returns "cap reached" UX copy. Epic: EPIC-PRR-B. P0, M.
2. **PRR-NEW-F2 — Separate cache SLO tier for rubric grading.** Extend PRR-047 metric with `llm_feature` label (`photo_vision` / `rubric_grading`). SLO: photo_vision ≥ 70% (unchanged), rubric_grading ≥ 50% (new, lower). Alert rubric_grading <40%. Epic: EPIC-PRR-B. P1, S.
3. **PRR-NEW-F3 — Tier-bump experiment design for premium SKU.** A/B design for premium-tier pricing ($15 vs $18 vs $22), feature gate (photo cap, rubric cap), and conversion hypothesis. 6-week experiment, n≥500 per arm. Epic: new pricing/commercial epic. P1, M.
4. **PRR-NEW-F4 — Photo hash-dedupe (per-student SHA).** Cache key = SHA-256 of post-normalize image + per-student scope. 30-day TTL. Saves ~20% of photo vision calls. Epic: EPIC-PRR-B. P1, S.
5. **PRR-NEW-F5 — Cost dashboard per input modality.** `cena_llm_cost_per_student_per_month_by_modality{modality=photo|rubric|mathlive|mc}`. Must ship before Q1 rolls to >5% of population. Epic: EPIC-PRR-B. P0, S.
6. **PRR-NEW-F6 — Renegotiate $3.30 ceiling in ADR-0050 Q5 or lock per-feature caps.** Either raise ceiling to $4.00 with explicit Q1+Q3 allowance, or lock hard caps (F1) as the mitigation. Do not ship Q1+Q3 against the current ceiling language. P0, XS (ADR update).

## Blockers / non-negotiables

- **Blocker**: current $3.30/student/month ceiling (ADR-0050 Q5) **does not cover** broad-framing Q1 + unlimited Q3. Either narrow framing + caps ship together with the features, or the ADR is renegotiated. No feature launches until F1 + F6 are resolved.
- **Non-negotiable**: no cross-student photo cache without a privacy ADR. ADR-0003 session scope wins over cost optimization.
- **Non-negotiable**: Q1 stays Tier 3 Sonnet vision. Do not let anyone propose Haiku vision to save money — it ships worse diagnosis and erodes trust faster than it saves dollars.
- **Watch**: rubric grading cache hit rate will miss the 70% SLO. Land F2 before Q3 ships, or we will trip PRR-047 alerts constantly and normalize ignoring them.
- **Watch**: if Q1 goes broad and hits 40 photos/month in cram weeks, per-student **daily** cost (not just monthly) can spike to $5+/day, breaching the ADR-0026 per-student $1.50/day circuit breaker. Circuit breaker will cut students off mid-session. Harsh UX. Fix with F1 rate-shaping, not just monthly caps.

## Questions back to decision-holder

1. Do you accept narrow framing for Q1 at Launch, with broad framing deferred to a premium SKU? (Finops says yes; product may push back.)
2. Is the $3.30 ceiling soft (can move to $4.00 with evidence) or hard? If hard, F1 caps are mandatory.
3. Premium SKU pricing — are we willing to run F3 A/B or does pricing need to land by fiat before Launch?
4. Cross-student perceptual-hash photo cache: do we want to open the privacy-ADR conversation to unlock 40–60% vision cost reduction, or is ADR-0003 untouchable?
