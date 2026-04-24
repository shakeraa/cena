---
persona: finops
subject: STUDENT-INPUT-MODALITIES-002
date: 2026-04-22
verdict: red
---

## 6.10 — persona-finops

### Summary

The brief's own Section 5 buries the lede: **$5.20/student/month HWR cost on writing-pad-heavy use is a 57% overrun of the ADR-0050 Q5 $3.30 ceiling**, and that is before we stack Q1 photo-of-solution (~$0.07–$0.52) and Q3 rubric grading (~$0.04–$0.48) from the 001-brief on top. Combined worst-case: **$5.70–$6.20/student/month on LLM/HWR spend alone**, 73–88% over the entire $3.30 ceiling with no room for retrieval practice, planner generation, misconception detection, or any other LLM workload. Verdict **red**. Writing-pad-primary math modality (4.1) cannot ship without (a) per-modality hard caps, (b) tier-bump path, and (c) a renegotiated or per-feature-segmented ceiling.

### Section 5 cost sanity — N validation

The brief's 400 answers/month figure is defensible **only for a cram-engaged student in peak weeks**. Real Cena usage distribution I would budget against:

| Student cohort | Answers/month (central) | Writing-pad share | HWR calls/mo |
|---|---|---|---|
| Light (check-in 2–3×/week) | 60–120 | ~40% math/physics | 24–48 |
| Median Bagrut-prep student | 150–250 | ~55% (math + physics + chem Lewis) | 82–137 |
| Engaged (daily practice) | 300–500 | ~60% | 180–300 |
| Cram-week spike (2 weeks/yr) | 600–900 | ~70% | 420–630 |

At **$0.013/HWR call** (Claude Sonnet vision, per 001 finops):

| Cohort | $/student/month (HWR only) |
|---|---|
| Light | $0.31–$0.62 |
| Median | $1.07–$1.78 |
| Engaged | $2.34–$3.90 |
| Cram spike | $5.46–$8.19 |

**The brief's $5.20 figure is the 90th-percentile cram-week outcome, not the median.** Median is ~$1.40, which fits the ceiling with Q1+Q3 headroom gone but survivable. The honest risk: engaged + cram-week students are **exactly the paying cohort**, and for them writing-pad math breaks the cap every month.

### Cache hit rate — HWR vs photo-of-solution

Lower. Confidently lower. Per-call composition:

- **Q1 photo** (001 finops): ~70% cache-eligible prefix (system prompt, rubric, subject context), student photo varies per call. Blended hit ~70%, cost $0.013.
- **HWR on writing-pad stroke**: the **stroke data is unique every call** — every attempt at every sub-step is a fresh image. System prompt + subject primer cache (~900–1,200 tokens) survives, but the stroke-image vision tokens (~1,600) are always cold. Effective cache hit: **~35–45%**, meaningfully below Q1's ~70%.
- **Per-call cost revised**: 1,600 cold vision + 300 cold text + 1,100 cached + 250 output ≈ **$0.014–$0.016/call**, **8–23% higher** than the brief's $0.013 assumption. Re-price Section 5 accordingly: median moves from $1.40 → **$1.55**, engaged from $3.12 → **$3.45**, cram-week from $5.46 → **$6.05**.

Brief's $0.013 HWR cost is **optimistic by ~15%**. Budget against $0.015.

### MyScript (client HWR) vs cloud-vision per-call

MyScript iink SDK licensing (public indications, 2025): ~$0.25–$0.40/MAU enterprise, or seat-based $15–30k/yr for ~5k seats via partner channels. At **10k paying students**:

- MyScript annual: **$25k–$40k/yr ≈ $0.21–$0.33/student/month flat**, accuracy ~85–92%, zero per-call cost, offline-capable, no moderation surface.
- Claude Sonnet vision: **$1.07–$6.05/student/month** (variable, grows with usage), accuracy ~90% claimed, requires Q1 MSP gates.

**MyScript is 5–20× cheaper at median-and-above usage** and flat-rate (predictable). It loses on accuracy ceiling and on the cost of not reusing Q1 infra. Honest math: if ≥25% of paying students are median-or-above on writing-pad use, **MyScript wins on unit economics**. If the population is mostly light users, cloud-vision wins because the flat license is wasted.

**Recommendation**: MyScript primary, Claude Sonnet vision as fallback for confidence-low strokes (the ~8–15% where MyScript returns low-confidence results). Blended cost target: **$0.30–$0.60/student/month** for median cohort. This is the only configuration that fits the ceiling without breaking Section 5.

### Per-subject dashboard breakdown

**Mandatory.** A `cena_llm_cost_per_student_per_month_by_modality{modality, subject}` cardinality explosion concern (modality × subject ≈ 4 × 7 = 28 series/student) is real but aggregated to subject-tenant-day is fine. Without subject breakdown, "math HWR is bleeding us" and "chem Lewis HWR is bleeding us" are indistinguishable — we cannot apply targeted caps. 001 finops F5 already covers modality; extend to `{modality, subject}`. Ship-gate: dashboard **before writing-pad reaches >2% of population**.

### Tier-bump path

Yes, mandatory. Basic SKU: keyboard/MathLive primary, writing-pad disabled OR hard-capped at 40 HWR calls/month (≈$0.60 cost). Premium SKU ($18–22/month): writing-pad unlimited up to 600 HWR calls/month (≈$9 cost envelope, leaves $9–13 gross margin per student after all LLM overhead). This is how "Bagrut exam fidelity" and "unit economics" coexist. Do not let writing-pad ship as a free-tier feature.

### Section 7 positions

- **Q2 default state (1)**: zero LLM cost. Neutral.
- **Q2 server enforcement (2)**: classroom-enforced redaction adds a `POST /reveal` call — tiny cost, ignore.
- **Q2 commit-and-compare (3)**: if the typed-answer uses CAS compare, zero LLM cost. If it uses LLM equivalence fallback, +$0.002/item. Prefer CAS.
- **Q3 math modality (4)**: **MathLive primary, writing-pad scratch (client-only, no HWR) secondary.** Reserve HWR for items where student explicitly says "submit handwritten". Caps $/student/mo at ~$0.60 median.
- **Q3 chem (5)**: typed reactions (zero extra cost) + Lewis via writing-pad (low volume, ~4–8 HWR calls/mo/user ≈ $0.06–$0.12). Fine.
- **Q3 language (6)**: keyboard-only. No HWR cost. Confirmed.
- **Q3 HWR procurement (7)**: **MyScript primary + Claude Sonnet fallback.** Not pure-Sonnet. Reuses-Q1-infra argument is pedagogically appealing but financially wrong.
- **Cap strategy (8)**: **hard daily + monthly caps, tier-bump for premium, MyScript for bulk.** "Scope down" (HWR only certain question types) is the cheapest but ships a worse UX; tier-bump is the honest answer.

### Recommended PRR tasks

1. **PRR-NEW-F7 — HWR vendor bake-off (MyScript vs Claude vision vs Mathpix).** 4-week eval: accuracy on 500 Bagrut math items, latency p95, per-student cost at median + cram-week loads. Decision blocker for writing-pad-primary. Epic: EPIC-PRR-B. **P0, M**.
2. **PRR-NEW-F8 — Writing-pad HWR per-student daily + monthly caps.** `hwr_calls_per_student_per_day ≤ 30` (basic) / 80 (premium); monthly ≤ 200/600. Circuit-break with "switch to MathLive" UX. Epic: EPIC-PRR-B. **P0, S**.
3. **PRR-NEW-F9 — Per-modality per-subject cost dashboard.** Extends 001 F5 with `subject` label. Mandatory before writing-pad ships to >2% of population. **P0, S**.
4. **PRR-NEW-F10 — Tier-bump SKU for writing-pad-heavy users.** Extends 001 F3 experiment design with writing-pad gate: premium unlocks HWR unlimited; basic caps at F8 levels. **P1, M**.
5. **PRR-NEW-F11 — Stroke-confidence routing.** Client-side MyScript returns confidence; low-confidence (<0.75) strokes fall through to Claude Sonnet vision. Hybrid cost model. Epic: EPIC-PRR-B. **P1, M**.
6. **PRR-NEW-F12 — ADR-0050 Q5 ceiling split.** Carve $3.30 into per-feature envelopes: core $2.10 + photo $0.40 + rubric $0.30 + HWR $0.50. Forces honesty per-feature. **P0, XS (ADR)**.

### Blockers / non-negotiables

- **Blocker**: writing-pad-primary math (Section 4.1) cannot ship against the $3.30 ceiling at engaged-cohort volumes. F7 bake-off + F8 caps + F10 tier + F12 ceiling-split must land first.
- **Blocker**: "reuse Q1 MSP" is pedagogically elegant, financially expensive at scale. F7 must include MyScript, not just cloud-vision variants.
- **Non-negotiable**: F9 per-subject dashboard before any writing-pad feature crosses 2% of paying population. Without it, cost incidents will be invisible until the monthly bill.
- **Watch**: cram-week daily spike ($6+/student/day HWR) will trip ADR-0026 per-student $1.50/day circuit breakers mid-Bagrut-prep — worst UX moment of the year. F8 daily caps + rate-shaping mandatory.
- **Watch**: Section 4.1's "writing-pad primary, MathLive secondary" posture is the exam-fidelity win but the cost loser. If F7 shows MyScript can't hit ~92% on Bagrut math, the posture has to flip to MathLive-primary on cost grounds and we eat the exam-fidelity regression.
