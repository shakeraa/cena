# TASK-PRR-259: Cache observability + cache_control breakpoints on variant prompts (R10 split-out)

**Priority**: P1 — gates ADR-0059 cost ceiling
**Effort**: S (2-3 days)
**Source docs**: ADR-0059 §15.5 + §14.4 R10, persona-finops findings, claude-code self-audit
**Assignee hint**: claude-2 (PRR-244 + finops context)
**Tags**: source=claude-code-audit-2026-04-28,epic=epic-prr-n,priority=p1,observability,cost,llm
**Status**: Ready
**Tier**: launch-adjacent
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

Persona-finops review predicts ADR-0059 variant prompts will fragment the prompt cache, plausibly dropping PRR-047 hit-rate from ~85% to <10% without explicit `cache_control` breakpoints separating static scaffolding from variable source body. R10 was originally written as "concurrent with PRR-245 implementation" but is more honestly a P1 task that gates the cost ceiling claim.

## Scope

1. Add explicit Anthropic `cache_control` breakpoints on variant-generation prompts: static scaffolding (system prompt, format spec, output schema) above the breakpoint; variable source body (Ministry question text, student context) below.
2. Per-`{variation_kind, cache_layer}` dimensions on PRR-047's hit-rate metric (`prompt_cache_hit_rate{variation_kind="parametric|structural", cache_layer="ephemeral|1h|persistent"}`).
3. Alert at hit-rate <70% (PRR-047 SLO floor).
4. Cost-per-call observability per institute (cross-cuts PRR-253 — coordinate keys).

## Files

- `src/actors/Cena.Actors/Llm/VariantPromptBuilder.cs` (new or modified)
- `src/actors/Cena.Actors/Observability/PromptCacheMetrics.cs` (extended)
- `src/actors/Cena.Actors.Tests/Llm/VariantPromptCacheTests.cs` (new)

## Definition of Done

- Hit-rate metrics emitted with new dimensions.
- Alert wiring in place.
- Test fixture proves hit-rate ≥70% on a 100-call workload of mixed parametric + structural.

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + hit-rate test fixture summary>"`
