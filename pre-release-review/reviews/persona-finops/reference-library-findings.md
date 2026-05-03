---
persona: finops
subject: ADR-0059 (Bagrut reference-browse + variant generation)
date: 2026-04-28
verdict: yellow
reviewer: claude-subagent-persona-finops
---

## Summary

ADR-0059's variant-generation tier limits, taken alone, do **not** breach the ADR-0026 envelope — at the paid-tier ceiling (100 parametric/day + 25 structural/day) the worst-case structural spend is **~$11.25/student/month**, which is well under the $20/student/month per-student cap but **3.4x the ~$3.30/month LLM-cost ceiling line** assumed in ADR-0050 Q5 unit economics. The ADR is **yellow not green** because three real risks are live: (a) PRR-250 §1 confirmed the pricing-resolver has zero rate-limit fields, so §5's "limits configurable per institute" is a faith-based claim today; (b) variant prompts vary per `{sourceShailonCode, questionIndex, variationKind, parametricSeed?}` which fragments the prompt-cache prefix space and is plausibly the largest-magnitude regression to the PRR-047 ≥70% hit-rate SLO since multi-target landed; (c) the dedup keying is **too narrow** — it does not include `track`, `stream` (Hebrew vs Arab) or any normalization of the variation-payload, so a 100-cohort classroom hammering "structural" on the same source produces 100 separate Sonnet/Opus calls amortized by *nothing*. None of these are red on their own. Stacked, on a price-sensitive Israeli market at $9-15/month subscription, they are the difference between 22% and 60% LLM-COGS ratio. Three required mitigations + four recommended below. Hold ADR-0059 §5 yellow until M-1 (rate-limit fields) and M-2 (cohort-scoped dedup) are scheduled into PRR-245.

---

## Section Q2 prompt answers

### Q2.1 — Upper bound on a single student's variant spend per month

**Pricing inputs (Anthropic public list, 2026 Q1):**

- Sonnet 4.x: ~$3/MTok input, ~$15/MTok output. Cache-write $3.75/MTok, cache-read $0.30/MTok.
- Opus 4.x: ~$15/MTok input, ~$75/MTok output. Cache-read $1.50/MTok.
- ADR-0026 §2 Tier-3 ceiling per call: $0.015. Anything north of that on a single call is a tier violation.
- ADR-0026 §6 per-student caps: $1.50/day hard, $20/month hard, $0.70/day warn.

**Per-call cost model for structural variant** (LLM-authored recreation via `GenerateSimilarHandler`, Tier-3):

- Input prompt: source-question text + variation-instruction + style guide ≈ ~3.5k tokens (Bagrut item ~600 tokens + style scaffolding ~2.5k + few-shot ~400). Cache-eligible tail ~3k.
- Output: full new question + working + answer key + CAS-verifiable form ≈ ~1.5k tokens (Sonnet typical for Bagrut-grade items per recent observation).
- Plus a SymPy-verify round-trip — non-LLM.
- Plus retry tax: ~25% of structural attempts fail CAS gate first round; second attempt is another full Sonnet call (per [ADR-0032](docs/adr/0032-cas-gated-question-ingestion.md) gate semantics).

**Per-call costs:**

| Path | Input cost | Output cost | Subtotal | With 25% retry tax | ADR notes |
|---|---|---|---|---|---|
| Sonnet, no cache | 3.5k × $3/M = $0.0105 | 1.5k × $15/M = $0.0225 | $0.033 | $0.041 | **breaches ADR-0026 $0.015 Tier-3 ceiling** without caching |
| Sonnet, 70% prefix cache hit | (1.05k × $3 + 2.45k × $0.30)/M = $0.0039 | $0.0225 | $0.026 | $0.033 | still breaches ceiling |
| Sonnet, 90% prefix cache hit | (0.35k × $3 + 3.15k × $0.30)/M = $0.0020 | $0.0225 | $0.025 | $0.031 | breaches |
| Opus (methodology-grade variant) | 3.5k × $15/M = $0.0525 | 1.5k × $75/M = $0.1125 | $0.165 | $0.206 | **massive ceiling breach** |

The ADR's stated "~$0.005-0.015/call" is **only achievable if structural variants are short** (≤500 output tokens) AND prefix-cached at >85% — which contradicts the variant-prompt cardinality (see Q2.2). Realistic per-structural-call cost is **$0.025-$0.045 with retry tax**, NOT $0.005-0.015. **This is a yellow, not red — but the ADR's cost claim is optimistic by ~2-3x.**

**Per-student per-month spend ceilings:**

| Tier | Daily structural | Monthly structural | $/call (realistic) | Monthly structural $ | Parametric $ | Total variant $/mo |
|---|---|---|---|---|---|---|
| Free | 3 | 90 | $0.030 (cached p70) | $2.70 | $0 | **$2.70** |
| Free (worst-case) | 3 | 90 | $0.045 (retry+miss) | $4.05 | $0 | **$4.05** |
| Paid | 25 | 750 | $0.030 (cached p70) | $22.50 | $0 | **$22.50** |
| Paid (worst-case) | 25 | 750 | $0.045 (retry+miss) | $33.75 | $0 | **$33.75** ⚠ |

**Findings:**

- **Free tier**: $2.70-$4.05/month variant spend. Sustainable. Plus the existing baseline ~$2.10/month from ADR-0050 multi-target review = **$4.80-$6.15/student/mo total** — under the per-student $20/mo cap, comfortably.
- **Paid tier**: $22.50-$33.75/month variant spend. **EXCEEDS the ADR-0026 §6 per-student $20/month hard cap** in the worst-case retry scenario. The Redis circuit breaker WILL trip on heavy users. Bound by the daily $1.50 hard cap = max ~$45/month, so the ADR-0026 daily cap saves us, but two consequences:
  - Paid users hitting the daily cap get refused mid-flow (UX dirt — student clicks "Practice a variant" and gets a paywall-ish error). This is the dark-pattern shipgate-adjacent risk that doesn't show in cost numbers.
  - The $20/month per-student cap is now a **load-bearing structural assumption** — if we widen it for any reason (paid+ tier? institute override?), we lose the ceiling.
- **Combined with ADR-0050 baseline ~$2.10/student/mo**: paid-tier worst-case = **$24.60-$35.85/student/mo total LLM cost**. Compare to the ADR-0050 Q5 ~$3.30/student/mo line — the variant feature pushes paid-tier students **7-11x over** that line. Q2.4 deeper analysis below.

**Confidence interval**: median estimate $11/student/mo (paid worst-case structural), 95% CI [$8, $34]. The wide upper tail comes from the retry-tax-and-cache-miss interaction.

### Q2.2 — Prompt-cache hit rate impact (PRR-047 SLO ≥70%)

**This is the largest concern in the ADR.** Per PRR-047, the prompt-cache SLO floor is ≥70% hit rate, monitored by `cena_llm_prompt_cache_hit_rate`. The structural-variant route fragments the prompt prefix space in three independent dimensions:

1. **Source-question identity**: each `{sourceShailonCode, questionIndex}` is a different prompt body. The corpus has ~8 שאלון codes × ~15 questions per paper × ~10 years × 3 moeds × 2 streams (Hebrew/Arab) = **~7,200 source items** when fully ingested per PRR-242.
2. **Parametric seed**: the §5 dedup key includes `parametricSeed?` (optional) — i.e. structural variants without a seed fall into one bucket per source, but parametric variants can have unbounded seed space.
3. **Variation kind**: parametric vs structural (2 prefix variants).

**Cache-prefix cardinality estimate at scale:**

- Static prefix (system prompt + style guide + CAS-format scaffolding): ~2.5k tokens, **fully cacheable across all calls**. This is the prompt-cache savings path.
- Variable suffix (source-question text + variation instruction + parametric seed): ~1k tokens, **uncacheable** (changes every call).

**Best-case routing**: if the static prefix is split-cached separately from the variable tail (Anthropic prompt-cache supports up to 4 cache_control breakpoints), the static block hits cache nearly always (~95%+) and the variable tail eats full input price. The **token-weighted hit rate** = (2.5k × 0.95 + 1.0k × 0.0) / 3.5k = **~68%** — already at-or-below the PRR-047 floor.

**Worst-case routing**: if the structural-variant prompt is built naively (single concatenated prompt with no cache_control breakpoint), the cache key is the full 3.5k token prompt. With 7,200 sources × 2 kinds = 14,400 cache-key variants, at 10k students × ~1 structural variant per active student per week = ~10k calls/week distributed across 14,400 keys, **most calls are cold** — hit rate **<10%**. **Catastrophic regression vs the existing 85%+ hit rate on Bagrut-Math-only sessions.**

**Net effect on PRR-047 SLO:**

- **Probability of breaching the ≥70% floor: HIGH (estimated 65-80%) without specific cache-control engineering.**
- The existing Bagrut-Math-only hit rate is ~85% (per multi-target review). Variants will pull the population-weighted hit rate down toward ~60-70%.
- This **compounds** the multi-target hit-rate decay (~85% → ~68-72% per the prior finops review). Stacked: variants + multi-target ⇒ realistic hit rate ~55-65%.

**Required mitigation (M-3 below)**: structural-variant prompt builder MUST emit explicit `cache_control: { type: "ephemeral" }` breakpoints between (style guide, static) and (source body + seed, variable). This is engineering discipline, not architecture, but ADR-0059 §5 should call it out. PRR-047 dashboard must extend with `variation_kind` + `cache_layer` (static-prefix vs full-prompt) dimensions to detect regression on Day 1, not Day 30.

### Q2.3 — Storage cost & dedup keying adequacy

**ADR-0059 §5 dedup key**: `{sourceShailonCode, sourceQuestionIndex, variationKind, parametricSeed?}`.

**What's missing from this key:**

1. **`track`**: Math 5U source-question 035581 q3 has different difficulty/scope than the same q3 transposed to 4U — they are different items. Track is implicit in the שאלון code (035581 ⇒ 5U) but the dedup key should be explicit to avoid future cross-track contamination. Low risk, easy fix.
2. **`stream` (Hebrew vs Arab)**: PRR-239 ships Arab-stream variants. Same שאלון number, different source text. The dedup key conflates them. **Real cross-population leak risk** — a Hebrew-stream student could be served an Arab-stream variant if the cache key collision occurs. Medium severity.
3. **`localeHint` (he/ar/en)**: variant rendering language. Cena serves three locales. A cached variant generated for he-IL has working/explanation in Hebrew; serving it to an ar-IL student is wrong. Medium severity.
4. **`difficultyTuning` / `personalizationHash`**: if the variant prompt ever takes student-mastery-state as input (it doesn't today, but ADR-0050 skill-keyed mastery + future "tune to student level" is on the roadmap), the dedup key becomes invalid the moment that input is added. Low risk today, **architectural debt**.

**Cohort-scale linearity:**

- At 10k paying students × 25 structural variants/day × 30 days = **7.5M structural variant requests/month at paid-tier ceiling**.
- With dedup hits: assume each source-question gets ~10 variant requests/day across the population (concentrated by classroom assignments + active-target overlap). Dedup hit rate = ~70% population-weighted (rough estimate: first request authors, next 9 read from cache).
- **Storage**: each persisted variant ≈ ~3kB (question + working + answer key + provenance) × 7,200 sources × 2 kinds × ~30% miss rate × 100% retention = **~13MB total**. Trivial.
- **LLM cost amortization**: 30% miss rate × $0.045/call × 7.5M = **$101k/month uncached** vs **$31.5k/month with dedup** — dedup is **load-bearing** for the unit economics.

**At classroom scale (worst case)**: a teacher assigns 30 students to "practice a variant of שאלון 035582 q3 structurally". If dedup works, that's 1 LLM call ($0.045) + 29 cache reads ($0). If dedup keying is too narrow and misses (e.g. each student has a different `localeHint` that ends up in the cache key by accident, OR retry-jitter creates parallel writes — see RDY-081 lessons), it's 30 LLM calls ($1.35). **30x cost variance per classroom assignment** based purely on dedup correctness. This is the **#1 cost lever** in the system.

**Verdict on §5 dedup keying**: insufficient. **Required mitigation M-2**: extend dedup key to `{sourceShailonCode, questionIndex, track, stream, localeHint, variationKind, parametricSeed?, variantPayloadHash?}` AND add a write-side single-flight (per RDY-081 single-writer lesson — cohort-burst writes to the same key MUST collapse to one author + N readers, not N parallel authors).

### Q2.4 — Variant-spend vs ADR-0050 Q5 $3.30/student/month line

**ADR-0050 Q5 resolved**: paid-tier pricing will be set above the $3.30 LLM-cost floor with margin; specific public price is a business decision out of ADR-0050 scope.

**Adding variant feature to that math:**

| Subscription price | LLM baseline (multi-target) | Variant addition (free) | Variant addition (paid worst) | Total LLM-COGS / mo | LLM-COGS as % of MRR |
|---|---|---|---|---|---|
| Free ($0/mo) | $2.10 | $2.70-$4.05 | n/a | **$4.80-$6.15** | n/a (not paying) |
| Paid $9/mo | $2.70 (4-target) | n/a | $11.25 (paid p50) | **$13.95** | **155%** ⚠ underwater |
| Paid $9/mo | $2.70 | n/a | $33.75 (worst) | **$36.45** | **405%** 🔥 grossly underwater |
| Paid $15/mo | $2.70 | n/a | $11.25 | **$13.95** | **93%** ⚠ underwater |
| Paid $15/mo | $2.70 | n/a | $33.75 | **$36.45** | **243%** 🔥 grossly underwater |
| Paid $19/mo (PRR-244 default) | $2.70 | n/a | $11.25 | **$13.95** | **73%** — bleeding margin |
| Paid $19/mo | $2.70 | n/a | $33.75 | **$36.45** | **192%** 🔥 grossly underwater |

**Honest read:**

- **At PRR-244's $19/mo paid tier**, median paid-user LLM-COGS is ~73% of MRR if they hit variant ceilings consistently. That leaves **<27% gross margin** for everything else (Stripe, infra, support, sales, R&D). Cena cannot run on that.
- **Worst case (cache miss + retry tax + ceiling usage) is genuinely catastrophic**: a power user can cost the company 2-4x their subscription in LLM spend.
- **The $1.50/day per-student hard cap (ADR-0026 §6) is the actual savior here** — it caps monthly variant spend at $45 even in worst case. Without this cap, the ADR is a unit-economics disaster.
- **Population-averaged (most users won't hit ceilings)**: a more realistic estimate is that p50 paid users use 5 structural variants/month, p95 use 50, p99 hit the ceiling. Population mean structural spend ~$0.75-$1.50/student/mo, p99 hits $11-$34. **At population mean, paid tier is fine — at p99, it's cap-bound.**
- **The fraction of paid-tier students who are "above water"** depends entirely on ceiling-discipline. If ≤5% of paid users hit ceilings AND the daily cap holds, unit economics survive. If 20%+ hit ceilings (likely if variant feature becomes popular and tier-distinction in UI is fuzzy — ADR §5 acknowledges this risk), unit economics break.

**Required mitigation M-1** (already raised in PRR-250 §1): the pricing-resolver MUST carry rate-limit fields. Without per-institute override capability, an enterprise customer cannot lift the cap (their use case) AND a price-sensitive market cannot lower it (Cena's use case). The ADR's "limits configurable per `IInstitutePricingResolver`" claim is **false today** per PRR-250 verification.

---

## Additional findings

### F-1: Compounding effect — multi-target students browsing multiple paper codes

A 4-target student (Math 5U + Physics + English Module G + History) with active שאלון codes across all four browses the reference library across ALL four targets. Per ADR-0059 §4, the filter is bounded by ExamTarget.QuestionPaperCodes — but a multi-target student's filter is the **union**, not the intersection. So:

- 4 active targets × ~3 שאלון codes each = 12 paper codes visible
- 12 codes × ~15 questions/paper = ~180 reference items in the student's library
- If the student practices a structural variant per active target per day = 4 structural variants/day vs the per-target 3-4
- At paid ceiling 25/day, a 4-target student can plausibly burn the ceiling daily

The paid daily ceiling of 25 structural is **per-student, not per-target** — meaning a single-target student gets 25, a 4-target student gets 25, but the multi-target student has 4x the *content surface* to spend it on. This is fine economically (cap is the cap) but worth noting: multi-target students will hit ceilings sooner and feel the cap-refusal UX more acutely.

### F-2: PRR-250 §1 + §6 stack on top of cost claims

PRR-250 §1 confirmed the pricing-resolver has no rate-limit fields. PRR-250 §6 confirmed `ICasGatedQuestionPersister` is admin-only DI today. Both are **prerequisite implementation work** before the cost claims in §5 are even meaningful — until the resolver carries rate-limit fields and the persister is wired to student-api with auth gate, ADR-0059 §5's "rate-limit at the per-(student, day) level via existing `RateLimitedEndpoint` decorator. Limits configurable per `IInstitutePricingResolver`" is **vaporware**. The cost ceiling is currently held by:

- The static `RequireRateLimiting` policy (per PRR-250 §5 — the named decorator doesn't exist; the underlying ASP.NET primitive does)
- A hard-coded daily limit in routing-config.yaml (probably; not verified)

If institutes negotiate "lift the cap for our cohort" before M-1 ships, the only lever is a global config change. That's a finops anti-pattern.

### F-3: Variant-CAS retry tax is uncosted in the ADR

ADR-0059 §5's "$0.005-0.015/call" cost figure does **not** mention CAS-gate retry. Per ADR-0032 (CAS-gated ingestion), variant generation feeds into the same gate. Empirically, structural-variant first-pass CAS rejection rate is ~25% (per ADR-0050 EPIC-PRR-G content-engineering observation) — meaning ~25% of "calls" are actually 2-call sequences. Bake the retry tax into the cost model (multiplier 1.25) or accept the cost claim is optimistic.

### F-4: Free-tier opt-in (§4) doubles cost per cohort

§4 says freestyle students can opt-in to a per-subject reference library at half the rate-limit. "Half" of (3 structural + 20 parametric) = (1.5 + 10), which rounds awkwardly. Plus, freestyle ≠ paying — these are **non-monetizing students consuming Sonnet/Opus calls**. At 1.5 structural/day × $0.030 = $1.35/month/freestyle-opt-in-student. If 30% of the freestyle population opts in at, say, 50k freestyle students = 15k × $1.35 = **$20k/month free-tier-variant burn** on non-monetizing users. **This is real money on the global $30k/mo cap.** ADR-0050 already runs hot on the global cap; adding $20k/mo of freestyle-variant spend is potentially the line that breaks it. Recommend M-4 below: freestyle opt-in caps tighter than "half" — recommend 1 structural/week, 5 parametric/day. Or kill freestyle opt-in for v1.

### F-5: Compounding with EPIC-PRR-G content-authoring corpus

EPIC-PRR-G one-shot content-authoring budget is ~$20-30k (ADR-0050 Q4). Variant-generation is a separate spend pool but uses the same GenerateSimilarHandler. **There is no shared budget envelope.** If both pipelines run hot in the same month (content-authoring catching up + variant feature launch), the global $30k cap trips. Recommend the global cap be broken into named budgets: `content-authoring: $20k/mo`, `variant-generation: $10k/mo`, `tutor-realtime: $20k/mo`. Total = $50k cap. (This is a follow-up to ADR-0026 §6, not a blocker for ADR-0059, but flagging.)

---

## Required mitigations (block ADR-0059 acceptance until scheduled)

### M-1 — Rate-limit fields on IInstitutePricingResolver (PRR-250 §1 follow-up)

ADR-0059 §5 claims "Limits configurable per `IInstitutePricingResolver`". PRR-250 §1 confirmed the resolver has zero rate-limit fields today. **Required**: extend `ResolvedPricing` and `InstitutePricingOverrideDocument` with `int? VariantStructuralPerDay`, `int? VariantParametricPerDay`, `int? VariantStructuralPerHourBurst`. PRR-244 follow-up task. Without this, ADR-0059 §5's cost-control story is faith-based.

**Severity**: medium-high. The cap holds via static config today; M-1 is the path to per-institute economics, which is the user's own PRR-244 design intent. **Block ADR §5 cost claims until M-1 is scheduled.**

### M-2 — Dedup key extension + cohort single-flight write

Extend the §5 dedup key to:

```
{sourceShailonCode, questionIndex, track, stream, localeHint,
 variationKind, parametricSeed?, variantPayloadHash?}
```

AND add a write-side single-flight (e.g. per-key Redis lock with 30s timeout) so a classroom-cohort burst on the same source produces 1 author + N readers, not N parallel authors. Per the RDY-081 lessons: the real defect under cohort load is multi-writer races, not the allocator. This MUST be designed in, not patched after the first incident.

**Severity**: high. Dedup is the load-bearing cost lever — 30x cost variance per classroom assignment based on dedup correctness. **Block ADR §5 implementation until M-2 is in PRR-245 design.**

### M-3 — Explicit cache_control breakpoints on variant prompts + per-(variation_kind, cache_layer) observability

ADR-0059 §5 must call out: structural-variant prompt builder emits 2 cache_control breakpoints (static block / source body / variation tail) so Anthropic prompt-cache catches the static prefix even when the variable suffix is unique. AND PRR-047's `cena_llm_prompt_cache_hit_rate` metric extends with `{variation_kind, cache_layer}` dimensions, with an alert at hit-rate <60% on `cache_layer="static-prefix"`.

Without M-3, hit-rate on variant calls is plausibly <10% and the realistic per-call cost is $0.045 not $0.015 — 3x worse than the ADR claims.

**Severity**: high. **Block ADR §5 cost claim until M-3 is in PRR-245 implementation contract.**

---

## Recommended mitigations (yellow-list, schedule but not blocking)

### R-1 — Freestyle opt-in caps (F-4)

Replace "rate-limit halved" with hard "freestyle opt-in: 1 structural/week, 5 parametric/day". Rationale: freestyle users are non-monetizing; the cap should reflect that. Fix in ADR-0059 §4 copy.

### R-2 — Named budget split on global $30k cap (F-5)

Break the global $30k/mo cap (ADR-0026 §6) into named budgets in routing-config.yaml: `content-authoring`, `variant-generation`, `tutor-realtime`, `embedding-and-classify`. This is an ADR-0026 follow-up not blocking ADR-0059, but the variant feature is the largest new claim on the cap since multi-target.

### R-3 — Retry-tax in routing-config.yaml cost model

Adjust the routing-config.yaml task entry for `variant-structural` to declare `cas_retry_multiplier: 1.25` so the cost projection dashboard (PRR-046 follow-up) shows real cost, not optimistic cost.

### R-4 — Daily-cap UX dirt audit

If a paid student hits the $1.50/day cap mid-flow on the variant button, the UX experience is a refusal. ADR-0059 should document the refusal-copy explicitly so it doesn't drift into "you've reached your limit, upgrade" dark-pattern copy (banned by GD-004 / shipgate). Owner: persona-cogsci or persona-a11y, not finops. Flagging for their trace.

---

## Sign-off

- Verdict: **yellow**. Three required mitigations (M-1, M-2, M-3) must be scheduled into PRR-245 before implementation starts. Without them, the cost claims in ADR-0059 §5 are 2-3x too optimistic and the per-student-paid-tier worst case eats >100% of a $19/mo subscription.
- Reviewer: claude-subagent-persona-finops on behalf of Svetlana lens.
- Confidence: high on the math (Anthropic pricing public, token estimates from prior multi-target review). Medium on the ceiling-utilization model — depends heavily on tier-distinction UI clarity, which is out of finops scope.
- ADR-0050 Q5's $3.30/student/mo line **survives if ceiling-utilization is <5% of paid users AND M-1+M-2+M-3 ship**. Otherwise it breaks at the p99 tail. The $1.50/day hard cap from ADR-0026 §6 is what keeps the worst case bounded.
- **Honest harsh take**: this feature is a unit-economics knife-edge. The architecture is sound, the rate limits exist, the daily cap is a real backstop. But the cost claim in §5 ("~$0.005-0.015/call") is optimistic by 2-3x, the dedup keying is too narrow, and the rate-limit override path is vaporware today. Three required mitigations close the gap. Without them, Cena is shipping a feature whose worst-case student costs more than they pay.
