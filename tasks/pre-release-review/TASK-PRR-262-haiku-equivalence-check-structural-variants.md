# TASK-PRR-262: Tier-2 Haiku second-pass equivalence-check on structural variants (R13 split-out)

**Priority**: P1 — gates ADR-0059 pedagogical + ministry §16 derivative-works distance
**Effort**: M (3-5 days; LLM rubric prompt + scoring + reject-loop)
**Source docs**: ADR-0059 §15.8 + §14.4 R13, persona-cogsci + persona-ministry findings, claude-code self-audit
**Assignee hint**: backend (LLM-tier-2 expertise; whoever owns AiGenerationService)
**Tags**: source=claude-code-audit-2026-04-28,epic=epic-prr-n,priority=p1,backend,llm,quality-gate
**Status**: Ready
**Tier**: launch-adjacent
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

ADR-0002 (SymPy CAS) verifies math correctness, not Bloom-level / skill-scope equivalence. Tier-3 LLM "same skill, different scenario" prompts predictably drift. Persona-cogsci required + persona-ministry confirmed (structural distance is a §16 derivative-works defense): add a Tier-2 Haiku second-pass scoring the candidate against the source on a 3-axis rubric.

## Scope

1. Tier-2 Haiku rubric prompt: score candidate variant against source on:
   - **Bloom level** (Understand / Apply / Analyze / Evaluate / Create — must match source ±0)
   - **Difficulty band** (5-band: easy / med-easy / med / med-hard / hard — must match source ±1)
   - **Skill scope** (concept ids set — must be a non-empty subset of source's, no spurious additions)
2. Reject + regenerate (max 3 attempts) on any axis mismatch.
3. After 3 rejects, fall back to "Practice a similar problem from the catalog" affordance (no variant served). User-facing copy explains the fallback honestly per ADR-0048 framing.
4. Telemetry: `variant_equivalence_check{outcome="pass|reject_bloom|reject_difficulty|reject_skill|fallback"}` counter.
5. Tests: known-good source + known-good variant pair → pass on first try; deliberate Bloom-drifted candidate → reject; 3 rejects in a row → fallback path.

## Files

- `src/actors/Cena.Actors/Llm/VariantEquivalenceCheck.cs` (new)
- `src/api/Cena.Admin.Api/Questions/GenerateSimilarHandler.cs` (extend to invoke equivalence check; reuse pattern from quality gate)
- `src/actors/Cena.Actors.Tests/Llm/VariantEquivalenceCheckTests.cs` (new)

## Definition of Done

- Equivalence check fires on every structural variant before render.
- Reject + retry + fallback paths all tested.
- Telemetry emitted.
- Cost: ~$0.0002 + ~500ms per variant (Haiku tier).

## Blocking

- PRR-245 variant-generation pipeline reaches the persistence path.

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + equivalence-check test results>"`
