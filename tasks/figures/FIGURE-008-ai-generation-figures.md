# FIGURE-008: AI generation — propose figure specs during variant generation

## Goal
Extend `src/api/Cena.Admin.Api/AiGenerationService.cs` (885 lines currently) so that when it generates a new question or variant, it also proposes a `figure_spec` when the question warrants one. The figure must pass FIGURE-007's quality gate before the variant is accepted.

## Depends on
- FIGURE-002 (schema)
- FIGURE-005 (physics service)
- FIGURE-007 (quality gate)

## Context
- Cena's "same format, different level" regeneration story (from the 2026-04-11 research sweep + the downstream ingest pipeline) requires that figures regenerate **parametrically** alongside the stem. If the stem changes `roots = (3, −2)` to `roots = (4, −1)`, the figure's curve and markers must update automatically.
- The AI never computes correctness (ADR to be produced — SymPy is oracle, LLM explains). So the LLM may propose a spec but the spec gets verified programmatically before it lands.

## Work to do
1. Extend the generation prompt to request a `figure_spec` JSON alongside the stem/answer/distractors, schema-constrained to the types from FIGURE-002.
2. Parse + validate the LLM output against the C# DTOs. Reject malformed.
3. Run the proposed spec through `FigureQualityGate` (FIGURE-007). If it fails, feed the violations back to the LLM for one retry. After one retry, mark the variant as `requires_human_review` and do not publish.
4. For physics specs: call `PhysicsDiagramService` to render and cache the SVG immediately, so the variant is ready to serve.
5. Generate the `aria-label` from the spec deterministically (from FIGURE-005 helpers) — don't trust the LLM with accessibility copy.
6. Audit trail: every AI-generated figure stores (a) prompt version, (b) model + temperature, (c) accepted/rejected/repaired.
7. Cost guardrails: figure-spec generation adds ≤ 30% tokens over the base variant; hard cap `maxFiguresPerDay` in `system/ai-settings.vue` admin.
8. Tests:
   - Happy path: function plot with clean roots → accepted
   - Sad path: physics spec with `Σ F ≠ 0` when `equilibrium = true` → rejected → retry → accepted
   - Sad path: LLM proposes a raster URL → rejected (AI is not allowed to propose raster specs — those come from ingest only)

## Non-negotiables
- **LLM does not compute correctness**. It proposes; SymPy / physics check verifies.
- Profile-scoped data (student mastery, names) must never enter the generation prompt (Edmodo precedent — Track 8).
- The generated figure is serving content to minors, so every gate rule is blocking.

## Do NOT
- Let the LLM bypass the quality gate
- Let the LLM author the `aria-label` directly — that's a deterministic helper

## DoD
- AI generation produces figures for function-plot and physics variants end-to-end
- Rejection/retry loop visible in logs + admin AI dashboard
- Generated variants ready to serve to students with passing quality gate

## Reporting
Complete with branch, a link to a dashboard filter showing the last 20 generated figures with their gate outcomes, and one example of a rejection → retry → accept trace.
