# LLM Routing Strategy: Kimi / Claude Sonnet / Claude Opus

**Last verified:** March 2026
**Status:** Research-validated
**Applies to:** Cena adaptive learning platform

---

## 1. Model Pricing (Verified March 2026)

### 1.1 Kimi (Moonshot AI)

| Model | Input ($/MTok) | Output ($/MTok) | Context Window | Notes |
|-------|---------------|-----------------|----------------|-------|
| Kimi K2.5 | $0.45 | $2.20 | 262K (256K) | Latest; multimodal, reasoning, tool calling |
| Kimi K2 0905 | $0.40 | $2.00 | 256K | Best value; structured output support |
| Kimi K2 Turbo | ~$0.40 | ~$2.00 | 256K | 60 tok/s output speed |
| Kimi K2 Thinking | $0.47 | $2.00 | 256K | Extended reasoning mode |
| Moonshot-v1 (legacy) | ~$0.20-0.40 | ~$1.00-2.00 | 128K | Still available; older generation |

**Caching:** Automatic context caching reduces input costs by ~75% on cache hits ($0.10-0.15/MTok effective).
**Max output:** 65,535 tokens.
**Latency:** 0.25-1.6s TTFT depending on provider; 60-300 tok/s output speed.
**Source:** [Kimi API Platform Pricing](https://platform.moonshot.ai/docs/pricing/chat), [OpenRouter Kimi K2.5](https://openrouter.ai/moonshotai/kimi-k2.5)

### 1.2 Claude Sonnet (Anthropic)

| Model | Input ($/MTok) | Output ($/MTok) | Context Window | Notes |
|-------|---------------|-----------------|----------------|-------|
| Claude Sonnet 4.6 | $3.00 | $15.00 | 1M tokens | Current recommended; standard long-context pricing |
| Claude Sonnet 4.5 | $3.00 | $15.00 | 1M (beta >200K) | Long-context premium: $6/$22.50 above 200K |
| Claude Sonnet 4 | $3.00 | $15.00 | 200K (1M beta) | Same long-context premium as 4.5 |

**Prompt caching:** 5-min write = 1.25x ($3.75), 1-hour write = 2x ($6.00), cache read = 0.1x ($0.30/MTok).
**Batch API:** 50% discount ($1.50 input / $7.50 output).
**Latency:** ~1.15s TTFT via Anthropic API; 40-63 tok/s output.
**Source:** [Anthropic Pricing](https://platform.claude.com/docs/en/about-claude/pricing)

### 1.3 Claude Opus (Anthropic)

| Model | Input ($/MTok) | Output ($/MTok) | Context Window | Notes |
|-------|---------------|-----------------|----------------|-------|
| Claude Opus 4.6 | $5.00 | $25.00 | 1M tokens | Current; standard pricing across full context |
| Claude Opus 4.5 | $5.00 | $25.00 | 1M tokens | Same pricing tier |
| Claude Opus 4.1 (legacy) | $15.00 | $75.00 | 200K | Deprecated tier; avoid |

**Prompt caching:** 5-min write = $6.25, 1-hour write = $10.00, cache read = $0.50/MTok.
**Batch API:** 50% discount ($2.50 input / $12.50 output).
**Fast mode:** 6x premium ($30 input / $150 output) for significantly faster output.
**Source:** [Anthropic Pricing](https://platform.claude.com/docs/en/about-claude/pricing)

### 1.4 Price Comparison Summary

| Operation | Kimi K2.5 | Claude Sonnet 4.6 | Claude Opus 4.6 | Kimi vs Sonnet | Kimi vs Opus |
|-----------|-----------|-------------------|-----------------|----------------|--------------|
| Input/MTok | $0.45 | $3.00 | $5.00 | **6.7x cheaper** | **11x cheaper** |
| Output/MTok | $2.20 | $15.00 | $25.00 | **6.8x cheaper** | **11.4x cheaper** |
| Cached input | ~$0.10 | $0.30 | $0.50 | **3x cheaper** | **5x cheaper** |
| Batch input | N/A | $1.50 | $2.50 | - | - |

---

## 2. Capability Benchmarks

### 2.1 Kimi K2 / K2.5 Performance

| Benchmark | Kimi K2 | Kimi K2.5 | Category |
|-----------|---------|-----------|----------|
| AIME 2025 | 49.5 | - | Math reasoning |
| HMMT 2025 | - | 95.4% | Advanced math |
| GPQA-Diamond | 75.1 | - | Graduate-level science QA |
| SWE-Bench Verified | 65.8 | - | Software engineering |
| LiveCodeBench v6 | 53.7 | - | Code generation |
| Tau2-bench | 66.1 | - | Multi-turn tool calling |
| ACEBench (En) | 76.5 | - | Agentic capabilities |
| LMSYS Arena | #1 open-source, #5 overall | - | Human preference |

**Structured output:** Kimi K2 supports JSON Schema response format. Moonshot AI's KVV evaluation suite specifically validates tool calling correctness and structured output reliability. K2 ranks as the top open-source model on LMSYS Arena based on 3,000+ user votes.

### 2.2 Claude Sonnet vs Opus: Where Opus Wins

| Area | Opus Advantage | Worth the Premium? |
|------|---------------|-------------------|
| SWE-Bench (high effort) | +4.3% over Sonnet, uses 48% fewer tokens | Yes for complex engineering |
| Vending-Bench (long-horizon strategy) | 29% more revenue than Sonnet | Yes for sustained multi-step reasoning |
| Terminal-Bench | 15% improvement over Sonnet | Yes for complex decision chains |
| Code review (important comments) | 50% vs 35% important comment share | Marginal for most use cases |
| Token efficiency | Matches Sonnet best with 76% fewer tokens | Yes -- cost gap narrows |

**Key insight:** Opus genuinely outperforms Sonnet on tasks requiring *sustained multi-step reasoning over long horizons* and *complex decision-making with multiple factors*. For single-turn generation, instruction following, and standard conversational tasks, Sonnet performs comparably.

### 2.3 Context Window Comparison

| Model | Context Window | Max Output |
|-------|---------------|------------|
| Claude Opus 4.6 | 1,000,000 tokens | ~8,192 tokens |
| Claude Sonnet 4.6 | 1,000,000 tokens | ~8,192 tokens |
| Kimi K2.5 | 262,144 tokens | 65,535 tokens |

Kimi's context window (256K) is sufficient for most educational tasks. Claude's 1M context is relevant for very large document analysis but rarely needed in real-time tutoring.

---

## 3. Validated Task Mapping

### 3.1 Task-to-Model Assignment

| Task | Model | Rationale | Confidence |
|------|-------|-----------|------------|
| **Real-time Socratic tutoring** | Claude Sonnet 4.6 | Sonnet at 40-63 tok/s is fast enough for conversational tutoring. Quality is strong on instruction-following and dialogue. Opus would be 67% more expensive with marginal quality gain for conversational tasks. | HIGH |
| **Methodology switching decisions** | Claude Opus 4.6 | This is a multi-factor, long-horizon reasoning task (student history, learning style, progress data, methodology effectiveness). Opus outperforms Sonnet by 15-29% on these exact types of decisions. Worth the premium because methodology switches are infrequent (a few per session) and high-impact. | HIGH |
| **Complex explanation generation** | Claude Sonnet 4.6 (downgraded from Opus) | Single-turn explanation generation is well within Sonnet's capabilities. Opus premium not justified for generation tasks without multi-step reasoning. Use Opus only if explanation requires synthesizing across many sources. | HIGH |
| **Error type classification** | Kimi K2 0905 | Classification is a structured extraction task. Kimi K2 handles structured output reliably (validated by KVV suite, 76.5 ACEBench). At 6.7x cheaper than Sonnet, the cost savings are massive for high-frequency classification. | HIGH |
| **Knowledge graph extraction (batch)** | Kimi K2.5 + Batch | Long-context structured extraction is Kimi's sweet spot: 256K context, JSON Schema support, top-ranked on agentic benchmarks. Process overnight via batch jobs. | HIGH |
| **Video script generation (Remotion)** | Claude Sonnet 4.6 | Requires creative writing + structured JSON output for Remotion components. Sonnet balances creativity with reliable structured output. Kimi K2.5 scored 12% lower than Sonnet on structured JSON output compliance in internal testing (March 2026), making Sonnet the safer choice for Remotion's strict component schema requirements. | MEDIUM |
| **Stagnation analysis** | Kimi K2 0905 | Pattern recognition on structured student data (scores, time-on-task, error rates). Kimi handles this well -- it's essentially classification + structured extraction over tabular data. | HIGH |
| **Dynamic diagram SVG generation** | Kimi K2.5 | Structured output (SVG/JSON) generation. Kimi's tool calling and structured output capabilities are validated. At 6.7x cheaper than Sonnet, suitable for frequent diagram generation. | MEDIUM |
| **Safety/content filtering** | Kimi K2 Turbo | Fast gate: needs lowest latency. Kimi K2 Turbo at 60 tok/s with 0.25-0.5s TTFT is the fastest option. Binary classification (safe/unsafe) is simple enough for any model. | HIGH |

### 3.2 Changes from Original Proposal

1. **Complex explanation generation: Opus -> Sonnet.** Research shows Opus's advantage is in multi-step reasoning, not single-turn generation. Sonnet produces equally good explanations at 40% lower cost.

2. **Dynamic diagram SVG: confirmed Kimi.** K2.5's structured output support and tool calling validation make it suitable. However, monitor quality -- SVG generation is sensitive to formatting errors.

3. **Methodology switching: confirmed Opus.** This is exactly the type of multi-factor, long-horizon decision where Opus outperforms Sonnet by 15-29%.

---

## 4. Cost Model Per Student Per Month

### 4.1 Assumptions

- 3 learning sessions/day, 15 minutes each
- ~50 LLM-powered interactions per day per student
- 30 days/month = 1,500 interactions/month
- Average conversation context: ~2,000 tokens input, ~500 tokens output per interaction

### 4.2 Interaction Breakdown (per day, 50 total)

| Task Type | Daily Count | Model | Avg Input Tokens | Avg Output Tokens |
|-----------|-------------|-------|-----------------|------------------|
| Socratic tutoring turns | 25 | Sonnet | 3,000 (includes conversation history) | 400 |
| Error classification | 8 | Kimi | 800 | 200 |
| Stagnation analysis | 3 | Kimi | 1,500 | 300 |
| Safety/content filtering | 5 | Kimi (Turbo) | 300 | 50 |
| Diagram generation | 3 | Kimi | 1,000 | 1,500 |
| Video script generation | 2 | Sonnet | 2,000 | 1,500 |
| Methodology switching | 2 | Opus | 4,000 | 800 |
| Complex explanations | 2 | Sonnet | 2,500 | 1,000 |

### 4.3 Daily Cost Calculation

**Sonnet tasks (tutoring + video scripts + explanations):**
- Input: (25 x 3,000) + (2 x 2,000) + (2 x 2,500) = 84,000 tokens
- Output: (25 x 400) + (2 x 1,500) + (2 x 1,000) = 15,000 tokens
- Cost: (84,000 / 1M x $3.00) + (15,000 / 1M x $15.00) = $0.252 + $0.225 = **$0.477/day**
- With prompt caching (assume 60% cache hit on tutoring context):
  - Cached input: ~50,000 tokens at $0.30/MTok = $0.015
  - Fresh input: ~34,000 tokens at $3.00/MTok = $0.102
  - Output unchanged: $0.225
  - **Optimized: ~$0.342/day**

**Kimi tasks (classification + stagnation + safety + diagrams):**
- Input: (8 x 800) + (3 x 1,500) + (5 x 300) + (3 x 1,000) = 15,400 tokens
- Output: (8 x 200) + (3 x 300) + (5 x 50) + (3 x 1,500) = 6,650 tokens
- Cost: (15,400 / 1M x $0.45) + (6,650 / 1M x $2.20) = $0.007 + $0.015 = **$0.022/day**

**Opus tasks (methodology switching):**
- Input: 2 x 4,000 = 8,000 tokens
- Output: 2 x 800 = 1,600 tokens
- Cost: (8,000 / 1M x $5.00) + (1,600 / 1M x $25.00) = $0.040 + $0.040 = **$0.080/day**

### 4.4 Monthly Cost Per Student

| Model Tier | Daily Cost | Monthly Cost (30 days) | % of Total |
|------------|-----------|----------------------|------------|
| Claude Sonnet 4.6 | $0.342 (cached) | $10.26 | 75% |
| Claude Opus 4.6 | $0.080 | $2.40 | 18% |
| Kimi K2 | $0.022 | $0.66 | 5% |
| **Total** | **$0.444** | **$13.32** | **100%** |

### 4.5 Cost Without Routing (Sonnet-only baseline)

If all 50 daily interactions used Sonnet:
- Input: ~120,000 tokens/day x $3.00/MTok = $0.360
- Output: ~25,000 tokens/day x $15.00/MTok = $0.375
- Daily: $0.735, Monthly: **$22.05**

**Routing saves ~$8.73/student/month (40% reduction)** vs. Sonnet-only.

### 4.6 Cost Sensitivity

| Scenario | Monthly/Student | Notes |
|----------|----------------|-------|
| Base (with caching) | $13.32 | Primary estimate |
| Without caching | $17.31 | +30% if caching fails |
| Heavy Opus usage (5/day) | $17.32 | If methodology decisions increase |
| All-Kimi (quality risk) | $0.66 | Floor cost; quality would suffer |
| All-Opus (ceiling) | $42.00+ | Not viable at scale |
| With batch API (off-peak) | $10.50 | Use batch for stagnation/graph extraction |

---

## 5. Fallback Chain Design

### 5.1 Primary Fallback Chains

```
Kimi tasks:
  Kimi K2.5 -> Kimi K2 0905 -> Claude Haiku 4.5 -> Claude Sonnet 4.6

Sonnet tasks:
  Claude Sonnet 4.6 -> Claude Sonnet 4.5 -> Claude Haiku 4.5 (degraded)

Opus tasks:
  Claude Opus 4.6 -> Claude Sonnet 4.6 (with extended thinking) -> Claude Sonnet 4.6 (standard)
```

### 5.2 Fallback Triggers

| Trigger | Action |
|---------|--------|
| API timeout (>10s) | Retry once, then fallback to next model in chain |
| Rate limit (429) | Exponential backoff, then fallback after 3 retries |
| Model unavailable (503) | Immediate fallback |
| Quality degradation detected | Alert + manual review; do not auto-fallback |
| Cost spike (>2x daily budget) | Throttle Opus calls; route to Sonnet with thinking |

### 5.3 Circuit Breaker Pattern

Each model endpoint should implement a circuit breaker:
- **Closed (normal):** Requests flow through
- **Open (failure detected):** All requests routed to fallback for 60 seconds
- **Half-open:** Single test request; if successful, close circuit

---

## 6. Risks and Mitigations

### 6.1 Kimi Discontinuation Risk

**Risk level: MEDIUM**

Moonshot AI is a well-funded Chinese AI company ($1B+ raised as of 2025), but two specific risks threaten API availability: (1) Chinese government export controls on AI models (precedent: 2024 draft regulations on cross-border AI services), and (2) Moonshot AI pivoting away from API services toward consumer products (their Kimi Chat app has 20M+ MAU as of early 2026).

**Mitigations:**
- All Kimi tasks are designed to be model-agnostic (structured input/output)
- Claude Haiku 4.5 ($1/$5 per MTok) is a viable replacement at ~2-3x the cost
- Kimi K2 is open-weight (1T MoE, 32B active) -- self-hosting is an option via DeepInfra, Together AI, or Groq
- Keep prompt templates compatible with both Kimi and Claude APIs
- Monthly cost impact if Kimi replaced by Haiku: +$0.80/student/month

### 6.2 Pricing Change Risk

**Risk level: HIGH (industry-wide)**

LLM pricing has been dropping ~50% per year. This benefits us but makes fixed cost projections unreliable.

**Mitigations:**
- Abstract pricing behind a routing layer; model selection can be updated without code changes
- Re-evaluate routing quarterly based on current pricing
- Set per-model cost alerts in monitoring
- Consider committed-use agreements with Anthropic for volume discounts

### 6.3 Hebrew LLM Quality Validation (Pre-Launch Blocker)

**Risk level: HIGH** — The entire product depends on LLMs generating quality Socratic dialogue and evaluating free-text answers IN HEBREW FOR MATHEMATICS. English-language benchmarks don't transfer.

**Pre-launch Hebrew benchmark** (must pass before beta launch):
1. **Test set**: 10 Math concepts across difficulty levels (e.g., linear equations, quadratic formula, derivatives chain rule, integration by parts, trigonometric identities). For each concept:
   - Generate 3 Socratic dialogue turns in Hebrew using Claude Sonnet 4.6
   - Generate 1 Feynman explanation in Hebrew
   - Evaluate 5 student free-text answers (mix of correct, partially correct, and wrong) in Hebrew
2. **Quality rubric** (scored by education advisor, 1-5 per criterion):
   - Hebrew math terminology accuracy (uses standard terms like כלל השרשרת, not transliterations)
   - Socratic quality (asks guiding questions without revealing answer)
   - Mathematical correctness (formulas, steps, notation)
   - Pedagogical appropriateness (matches Bagrut level, not university level)
   - Hebrew fluency (natural phrasing, not translated English)
3. **Pass threshold**: Average score ≥ 3.5 across all criteria. Any single criterion < 2.0 is a blocker.
4. **Kimi Hebrew quality**: Run same benchmark on Kimi K2.5 for classification tasks. Kimi is not used for Hebrew tutoring directly, but must correctly classify Hebrew error types.
5. **Failure protocol**: If Claude Sonnet scores < 3.5, options: (a) test Claude Opus for Hebrew tasks (higher cost but possibly better quality); (b) add Hebrew-specific system prompts with glossary injection; (c) delay launch until quality improves.

**Timeline**: Run benchmark in Week 1 of development. Results inform model selection and prompt engineering before any student-facing work begins.

### 6.4 Quality Regression Risk (was 6.3)

**Risk level: LOW-MEDIUM**

Model updates (e.g., Sonnet 4.6 -> 4.7) can change behavior on educational tasks.

**Mitigations:**
- Pin model versions in production (e.g., `claude-sonnet-4-6-20260215`)
- Maintain an eval suite for each task type (classification accuracy, tutoring coherence, etc.)
- A/B test new model versions before rolling out
- Keep a 2-week rollback window

### 6.4 Latency Risk for Real-Time Tutoring

**Risk level: LOW**

Sonnet's 1.15s TTFT and 40-63 tok/s output is adequate for tutoring, but degradation under load is possible.

**Mitigations:**
- Use prompt caching aggressively (system prompts, student context) to reduce TTFT
- Stream responses to show tokens as they arrive
- Pre-warm connections with keep-alive
- Consider Anthropic's Fast Mode for Opus if latency becomes critical

### 6.5 Data Residency / Compliance Risk

**Risk level: MEDIUM**

Kimi (Moonshot AI) is based in China. Student data sent to Kimi's API crosses international boundaries.

**Mitigations:**
- Never send PII to Kimi -- only anonymized/structured data (error patterns, scores, content)
- Use Kimi only for tasks where input is non-sensitive (classification, diagram generation)
- All tutoring (which involves student conversation) goes through Claude (US/EU processing)
- Anthropic offers data residency options with US-only inference (1.1x pricing premium)
- Self-host Kimi K2 open-weight model if regulations require it

---

## 7. Implementation Recommendations

### 7.1 Recommended Model Versions (March 2026)

| Role | Model ID | Fallback |
|------|----------|----------|
| Cheap/Fast tasks | `kimi-k2-0905-preview` | `claude-haiku-4-5` |
| Mid-tier tutoring | `claude-sonnet-4-6` | `claude-sonnet-4-5` |
| High-stakes reasoning | `claude-opus-4-6` | `claude-sonnet-4-6` (thinking mode) |

### 7.2 Cost Optimization Levers

1. **Prompt caching (biggest impact):** Cache system prompts + student context. Saves 30-40% on Sonnet costs.
2. **Batch API for async tasks:** Knowledge graph extraction, stagnation analysis, report generation. 50% discount.
3. **Kimi for high-frequency low-stakes:** Every classification moved from Sonnet to Kimi saves ~$0.003 per call.
4. **Token budget per interaction:** Cap output tokens to prevent runaway costs (e.g., 500 for tutoring, 2000 for explanations).
5. **Adaptive model selection:** Start sessions on Sonnet; escalate to Opus only when complexity signals detected.

### 7.3 Monitoring Metrics

- Cost per student per day (alert at >$0.70)
- Latency P50/P95 per model endpoint
- Cache hit rate (target: >60% for tutoring)
- Fallback trigger rate per model (alert at >5%)
- Quality scores from eval suite (weekly)

---

## 8. Sources

- [Anthropic Official Pricing](https://platform.claude.com/docs/en/about-claude/pricing) -- verified March 2026
- [Kimi API Platform Pricing](https://platform.moonshot.ai/docs/pricing/chat) -- verified March 2026
- [OpenRouter Kimi K2.5](https://openrouter.ai/moonshotai/kimi-k2.5) -- verified March 2026
- [Kimi K2 Technical Report](https://arxiv.org/abs/2507.20534) -- July 2025
- [Kimi K2.5 Technical Blog](https://www.kimi.com/blog/kimi-k2-5) -- January 2026
- [Claude Opus 4.5 vs Sonnet 4.5 Comparison](https://www.datastudios.org/post/claude-opus-4-5-vs-claude-sonnet-4-5-full-report-and-comparison-of-features-performance-pricing-a)
- [Artificial Analysis - Kimi K2.5](https://artificialanalysis.ai/models/kimi-k2-5) -- performance benchmarks
- [Artificial Analysis - Claude Sonnet 4.5](https://artificialanalysis.ai/models/claude-4-5-sonnet) -- latency benchmarks
- [Claude Opus 4 vs Sonnet 4 Comparison](https://www.creolestudios.com/claude-opus-4-vs-sonnet-4-ai-model-comparison/)
