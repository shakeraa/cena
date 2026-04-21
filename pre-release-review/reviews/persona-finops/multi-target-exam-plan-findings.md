---
persona: finops
subject: MULTI-TARGET-EXAM-PLAN-001
date: 2026-04-21
verdict: yellow
---

## Summary

Multi-target plan itself is **cost-neutral** on the scheduler side (picker is deterministic, section 6 is clear). The real cost delta is **content-engineering for SAT + PET v1** plus a measurable **prompt-cache hit-rate decay** from per-target context bloat. Net estimate: per-student-per-month LLM spend rises from ~$2.10 (single-target Bagrut) to ~$2.60–$3.10 (4-target ceiling) — a +25–50% multiplier, well inside the $20/student/month cap from ADR-0026 §6 but meaningful against the $30k/month global ceiling. Verdict **yellow** because SAT+PET item-bank authoring is the cost risk nobody has scoped, not because the aggregate design is broken. Three new PRR tasks recommended below.

## Section 9.10 answers

**(1) Does per-target personalization multiply LLM prompt-generation cost per session?**
No, if sessions stay single-target (section 6 "Out of scope for v1: multi-target interleaving within a single session"). Yes, if we leak target context into the system prompt. Concretely: at session start the `ActiveExamTargetId` picker runs — that is pure deterministic math on `WeeklyHours` + `Deadline`, no LLM (see Q6). Within the session, every hint/Socratic prompt carries **one** exam-target's context (syllabus ref, track, deadline, weekly hours). That's ~80–120 extra system-prompt tokens vs today's Bagrut-assumed prompt. At ADR-0026 tier-3 Sonnet pricing ($3/MTok input), that's **+$0.00024–$0.00036 per call**. Over a turn-budget-capped session (prr-105 = 20 tutor turns, prr-012 = 3 Socratic calls), that is **+$0.005–$0.007 per session**. Not a multiplier — a floor tax.

**(2) Does per-target context push hints past Haiku's cheap bucket?**
Tier-2 Haiku ceiling from ADR-0026 §2 is $0.0002/call. Haiku input pricing is $0.25/MTok. A hint prompt today runs ~1.5k input tokens + ~400 output → ~$0.00058 already, which is **already above Haiku's $0.0002 ceiling** — prr-145 ADR resolved this by keeping **L1 Booster/deterministic** as the default hint tier, Haiku only for short L2 structured hints (<500 tokens total), Sonnet on-demand for L3. Adding "preparing for PET" + "deadline Aug 2026" + "weekly 8h" adds ~40–80 tokens. That **does not** push L2 Haiku over ceiling (still <600 tokens). It **does** push L3 Sonnet prompts from ~2k → ~2.1k input — a 5% cost drift on the Sonnet path. Not a tier flip. Safe.

**(3) Cache hit rate drop (PRR-047 SLO ≥70%)?**
This is the one I'm actually worried about. Prompt-cache hits require **prefix identity** on the static portion (system prompt + exam-target context). Today with Bagrut-Math-only: the static prefix is essentially one value for the whole student population → cache hit rate trivially ≥95% on the static block. With 8 catalog entries × 3 tracks (worst case 24 variants) + variable deadline/weekly-hours interpolation, the static prefix fragments. If we template the target context as `{examCode}/{track}/{deadlineBucket=month-year}/{weeklyHoursBucket=5h-band}`, we get ~24 × ~12 × 8 = **~2,300 prefix variants** across the population. At 10k students, that's ~4 students per variant — cache-friendly if prefix is scoped globally, **cache-hostile** if scoped per-student. Estimate: **hit rate drops from ~85% → ~68–72%**, right at the SLO floor. Mitigation: Anthropic prompt-cache lets us cache the system-prompt-less-target block and only pay input on the target-context tail (~100 tokens). That's already what prr-047 enforces. New task needed: **cache-hit-by-target-distribution observability** (recommended below).

**(4) Catalog API cost — CDN vs direct hit?**
Catalog payload: 8 exams × ~300 bytes each (name en/he/ar, tracks, sittings, default lead-time) ≈ 2.4 KB gzipped ≈ 1 KB. Fetched once per session-start. At 10k students × 5 sessions/day = 50k calls/day = 1.5M/month. Direct hit to `GET /api/catalog/exams` on .NET minimal API = ~2ms compute + DB read (cached in-memory after first hit). Cost: ~negligible, ~$0.50/month in compute + egress. CDN (Cloudflare/Fastly) cache = ~$0.10/month + 150ms → 15ms TTFB improvement. **Delta = ~$0.40/month**. Not worth CDN infra complexity v1. Direct hit with 5-minute in-memory cache + ETag is fine. **Verdict: ship direct, no CDN.**

**(5) SAT + PET v1 content-engineering LLM cost?**
This is where finops should be making noise. Two paths:

- **SAT**: US-curriculum math is well-covered by existing LLM training data → **LLM-generated items with CAS oracle verification (ADR-0002) is viable**. Authoring cost: ~$0.05–$0.15/item (Sonnet generation + SymPy verification + rubric-tag pass). For a shippable item bank (~1,500 SAT Math + ~800 Reading/Writing items) = **~$150–$350 one-shot content-gen cost**. Reading/Writing can't CAS-verify; those need human review at ~$2–$5/item = **~$1,600–$4,000 editorial cost**. Repeatable per major exam form change.
- **PET verbal (Hebrew + Arabic native)**: The brief explicitly calls out "PET verbal cannot be machine-translated — the sections are language-native." Two options:
  - **LLM-generated-per-language**: Claude/Sonnet Hebrew verbal generation is empirically poor (analogy/reading-comp quality drops; Arabic worse). Expect ~30% reject rate → human review at ~$5/item × 600 items × 2 languages = **~$6,000 editorial floor**.
  - **Human-authored**: ~$15–$25/item × 600 × 2 = **$18,000–$30,000** upfront. Higher quality, slower.
  - My recommendation: hybrid — LLM-generate Hebrew drafts, human edit; **budget $8–12k one-shot for PET verbal authoring**, exclusive of ongoing refresh.
- **PET quantitative**: Hebrew math is CAS-verifiable same as Bagrut. Cheap — ~$200 one-shot.
- **Total SAT+PET content-gen floor**: **~$10k–$15k one-shot**, plus **~$500–$1,500/quarter refresh** to keep items from leaking to prep-site scrapers. This cost lives on the **content-engineering epic**, not EPIC-PRR-F. **It is not in any current budget line.** Flag.

**(6) Multi-target session-start picker — LLM or deterministic?**
Section 6 rules are pure arithmetic: deadline-proximity (14-day lock) + weighted round-robin by `WeeklyHours / sum(WeeklyHours)`, RNG seed = hash(userId, dayOfYear). **Zero LLM calls.** Keep it that way. If anyone proposes an "LLM-personalized smart target recommender" — veto at review. A single Sonnet call at session-start × 10k students × 5 sessions/day × $0.008/call = **~$1,200/day = $36k/month**, which breaches the global $30k/month cap by itself. Non-negotiable: session-start picker stays deterministic forever.

**(7) Unit economics — per-student-per-month, single-target vs multi-target:**

| Scenario | LLM calls/session | Avg cost/session | Sessions/month | $/student/month |
|---|---|---|---|---|
| Today (Bagrut-Math-only, single target) | ~8 (3 Socratic + 3 hints L2/L3 + 2 explanations) | ~$0.035 | 60 | **~$2.10** |
| 1 target (SAT or PET only) | ~8 | ~$0.038 (+tokens) | 60 | ~$2.28 |
| 2 targets (Bagrut Math + Physics) | ~8 | ~$0.040 | 60 | ~$2.40 |
| 4 targets (ceiling) | ~8 | ~$0.045 (cache miss tax) | 60 | **~$2.70** |
| 4 targets + worst-case cache decay to 50% | ~8 | ~$0.055 | 60 | **~$3.30** |

All scenarios are well under ADR-0026 §6 per-student cap ($20/month). Global cap check at 10k paying students: 10k × $3.30 = **$33k/month** — **breaches $30k global cap by 10%** in worst case. Mitigation already in place: per-student $1.50/day hard cap + circuit breaker. But the cap room is shrinking.

Break-even (if paid): at $15/month subscription, LLM COGS goes from 14% → 22% of revenue in worst case. Still sustainable. At $9/month (price-sensitive Israeli market), it goes 23% → 37%. **Tight.** Recommend pricing floor ≥$12/month if multi-target is priced as a premium feature.

**(8) Other:** None beyond above.

## Additional findings

- **Per-target note field (≤200 chars)** is a cost landmine if surfaced to LLM prompts. At 200 chars ≈ 50 tokens × embedded in every prompt for that target's sessions = 50 × ~200 prompts/month = 10k extra tokens/student/month = trivial ($0.03). Fine **if PII-scrubbed**. If not scrubbed (ADR-0022 violation), we eat legal cost not LLM cost. Privacy lens owns that; flagging for their trace.
- **Migration path (section 7)** — one-shot upcast on first login is zero LLM cost. Idempotent flag is a must, else a crash-loop re-runs the upcast on every login. Sre lens owns.
- **Archive-on-deadline-past** (section 6) — runs a scheduler tick, no LLM. Negligible.

## Section 10 positions

1. **SAT+PET v1 inclusion (resolved)**: cost-supportive on runtime, cost-hostile on one-shot content authoring. Budget line needed: **$10–15k one-shot content-gen + $500–1,500/quarter refresh**. Flag for the content-engineering epic. Do not let it ship without an authored budget owner.
2. **Max-targets cap at 4**: cap is **correct as a cost-cap**. Each target roughly adds +5% static-prefix variation to the cache-key space. At 4 targets per student, cache hit rate decay is bounded to ~15 pp from baseline. Allowing 5+ pushes us through the PRR-047 SLO floor. **Hold at 4.** Retake candidates with 5 subjects: archive old targets first.
3. **Classroom-assigned targets** (q5 9.5): v2 — no runtime cost impact for v1 deferral.
4. **Parent visibility**: no LLM cost; aggregate read path. Whatever privacy decides.

## Recommended new PRR tasks

1. **PRR-NEW-F1 — Per-target cache-hit-rate observability dashboard.** Extend PRR-047's Prometheus metric `cena_llm_prompt_cache_hit_rate` with `exam_target_code` + `track` dimensions. SLO: ≥70% hit rate per-target, alert at <65%. Epic: EPIC-PRR-B. P1, S effort. Owner hint: kimi-coder.
2. **PRR-NEW-F2 — Cost SLO per exam-target type.** Add `cena_llm_cost_per_student_per_month_by_target_count{target_count}` histogram. SLO: p95 ≤ $4/student/month at 4-target ceiling. Alert at p99 > $5. Epic: EPIC-PRR-B. P1, S.
3. **PRR-NEW-F3 — SAT+PET content-engineering budget & ownership ADR.** Document the ~$10–15k one-shot content-gen cost, refresh cadence, and human-editorial ownership for PET verbal. Must land **before** content-engineering epic starts authoring. Epic: new content-engineering epic (parallel to EPIC-PRR-F). P0, S.
4. **PRR-NEW-F4 — Ban LLM in session-start target picker (enforce §6 determinism).** Add a lint rule / CI scanner entry to `scripts/shipgate/llm-routing-scanner.mjs` for `SchedulerInputs`/`ActiveExamTargetId` paths. Prevents future regression. Epic: EPIC-PRR-B. P2, XS.

## Blockers / non-negotiables

- **Blocker**: SAT+PET item-bank authoring budget is unowned. Until PRR-NEW-F3 lands, content engineering can ship stubs (banned 2026-04-11) or blow budget silently.
- **Non-negotiable**: session-start target picker stays deterministic. LLM-in-picker would breach the $30k global cap on its own.
- **Non-negotiable**: per-target free-text note must be PII-scrubbed before any LLM surface (inherit ADR-0022). Cost-adjacent because legal > LLM spend.
- **Watch**: PRR-047 cache hit-rate SLO at 70% is now a tighter margin. Cache prefix design in the hint/Socratic prompts must explicitly split "pedagogy frame" (cacheable, global) from "target context tail" (variable, short). If this split is skipped, hit rate craters 20 pp.

## Questions back to decision-holder

1. Is $10–15k one-shot content-engineering budget approved for SAT+PET v1? If not, PET verbal is the first thing that shrinks.
2. Pricing floor: is Cena paid-tier pricing ≥$12/month locked, or could it drop to $9? At $9 with 4-target students and cache decay, LLM COGS is uncomfortably tight.
3. Max-targets cap — am I right to hold at 4 over the cost-cap rationale, or does persona-educator need 5 for retake candidates and you'll accept the 5 pp SLO risk?
4. Catalog API CDN: do you want us to spend $0.40/month for ~150ms TTFB saving on session-start? My call is no; want confirmation.
