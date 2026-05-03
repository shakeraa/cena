# ADR-0045 — Hint-generation and LLM consumer tier selection (prr-145)

- **Status**: Accepted
- **Date**: 2026-04-20
- **Decision Makers**: Shaker (project owner), persona-finops (Svetlana), persona-cogsci (Dr. Kenji), persona-sre (Dana)
- **Task**: prr-145 (Epic B follow-up to ADR-0026)
- **Epic**: [EPIC-PRR-B — ADR-026 3-tier LLM routing governance](../../tasks/pre-release-review/EPIC-PRR-B-llm-routing-governance.md)
- **Related**: [ADR-0026](0026-llm-three-tier-routing.md), [ADR-0002](0002-sympy-correctness-oracle.md)

---

## Context

[ADR-0026](0026-llm-three-tier-routing.md) established the three-tier model routing framework (Tier 1 Agent Booster / Tier 2 Haiku / Tier 3 Sonnet-Opus) and required every LLM-consuming class to carry a `[TaskRouting(tier, task-name)]` attribute. The scanner (`scripts/shipgate/llm-routing-scanner.mjs`) ships in advisory mode until every call site is tagged or allowlisted. ADR-0026's follow-up list names nine LLM-consuming services whose tier assignment was deferred to `TODO(prr-012)` and `TODO(prr-145)` allowlist entries so the scanner could land without being held hostage to tier decisions.

The nine pending services, with the reason each one is tier-sensitive:

| Service | File | LLM usage pattern |
|---|---|---|
| `L3ExplanationGenerator` | `src/actors/Cena.Actors/Services/L3ExplanationGenerator.cs` | Full personalized explanation (Sonnet, 150-500 tokens, methodology + affect + scaffolding shaping the prompt). |
| `ExplanationGenerator` | `src/actors/Cena.Actors/Services/ExplanationGenerator.cs` | Methodology-aware misconception explanation (Sonnet, 256-768 tokens). |
| `ErrorClassificationService` | `src/actors/Cena.Actors/Services/ErrorClassificationService.cs` | 5-class enum classification (Haiku, 200 tokens, temp=0). |
| `ContentSegmenter` | `src/actors/Cena.Actors/Ingest/ContentSegmenter.cs` | OCR-to-structured-JSON extraction (4096 tokens, batch-scale). |
| `TutorMessageService` | `src/actors/Cena.Actors/Tutor/TutorMessageService.cs` | Wraps `ITutorLlmService` for non-streaming tutor reply (delegates to Claude Sonnet). |
| `TutorActor` | `src/actors/Cena.Actors/Tutoring/TutorActor.cs` | 10-turn conversational Socratic dialogue (Sonnet, cache-enabled). |
| `AiFigureGenerator` | `src/api/Cena.Admin.Api/Figures/AiFigureGenerator.cs` | Multi-attempt figure-spec JSON generation with quality-gate retry loop. |
| `AiGenerationService` | `src/api/Cena.Admin.Api/AiGenerationService.cs` | Full question-batch generation via Anthropic tool-use (Sonnet, 4096 tokens). |
| `QualityGateService` | `src/api/Cena.Admin.Api/QualityGate/QualityGateService.cs` | 0-100 scoring across three dimensions via Haiku tool-use. |

These are the entire P1 hot path for Cena's student-facing LLM spend. Without tier assignments, every call in this list silently defaults to Sonnet — exactly the failure mode ADR-0026 §Context §silent-default flagged as a ship-blocker.

Additionally, prr-145's named concern is hint generation. The three-level hint ladder (L1 nudge → L2 method → L3 worked example) maps onto different cost/quality points and needs a specific policy before we ship a hint feature that silently lands all three levels on Sonnet.

## Decision

### §1 — Tier assignment heuristic

For each LLM-consuming service, the tier is decided by answering, in order:

1. **Is the task deterministic with a closed output shape?** — If yes, tier 1 (no LLM). Examples: enum-tag lookup, simple template substitution, regex normalisation. Not applicable to any of the nine services — all of them produce open-form NL output conditioned on runtime context.
2. **Is the task classification, short-form extraction, or binary judgement with a fixed schema?** — If yes, tier 2 (Haiku). Per-call cost ceiling: $0.0002.
3. **Else — reasoning, pedagogy, figure generation, multi-turn dialogue, or structured extraction over long context?** — Tier 3 (Sonnet default, Opus reserved for `methodology_switch`). Per-call cost ceiling: $0.015.

A tier assignment is not a developer preference; it is a cost contract that appears in `contracts/llm/routing-config.yaml` as the `task_routing.<task-name>` row.

### §2 — The nine tier assignments

| Service | Tier | task-name | Rationale |
|---|---|---|---|
| `L3ExplanationGenerator` | 3 | `full_explanation` | Personalized explanation, methodology-gated, affect-gated, variable length 150-500 tokens. Pedagogically load-bearing — this is the primary post-mistake feedback surface. |
| `ExplanationGenerator` | 3 | `full_explanation` | Same shape as L3 but without full student context. Still methodology-aware and Bloom's-calibrated — Haiku quality insufficient per routing-config `feynman_explanation` notes ("Sonnet sufficient for grading quality"). Shares the `full_explanation` row with L3. |
| `ErrorClassificationService` | 2 | `error_classification` | 5-class classification over (question, correct-answer, wrong-answer, optional-rationale). 200 output tokens, temp=0. Already a well-fit Haiku path; the existing routing-config row uses Kimi primary / Haiku fallback. |
| `ContentSegmenter` | 3 | `knowledge_graph_extraction` | OCR-to-structured-JSON extraction over a full textbook page. 4096 output tokens. Already in routing-config as Kimi K2.5 primary / Sonnet fallback — tier 3 per the Sonnet floor in that chain. |
| `TutorMessageService` | 3 | `socratic_question` | Wraps `ITutorLlmService` for non-streaming tutor replies. Same pedagogical surface as the streaming endpoint; same tier. |
| `TutorActor` | 3 | `socratic_question` | Multi-turn Socratic dialogue with cache-enabled Sonnet. Canonical tier-3 path. |
| `AiFigureGenerator` | 3 | `diagram_generation` | Multi-step figure-spec JSON with up to 3 retry attempts when schema or quality gate fails. Shares `diagram_generation` row with existing figure paths. |
| `AiGenerationService` | 3 | `question_generation` | Tool-use structured output over Bagrut-curriculum-aligned prompts. 4096 tokens, complex multi-question batch. New row. |
| `QualityGateService` | 2 | `quality_gate` | Three-dimension 0-100 scoring with tool-use schema. Low temp (0.1). Designed for Haiku per in-code comment `"Haiku: cheaper model for assessment tasks"`. New row — caps this evaluator at tier-2 cost so it cannot drift to Sonnet without an ADR amendment. |

### §3 — Hint-generation tier policy (prr-145's named concern)

Cena's planned hint ladder is three levels of escalation when a student gets stuck. Each level has a fixed tier; the student advances through the ladder, not between providers.

| Hint level | Product surface | Tier | task-name | Rationale |
|---|---|---|---|---|
| L1 | "Try this step" — re-states the most recent solved step in the methodology's vocabulary | 1 (no LLM) | n/a — static template | Deterministic transform over the most-recent-step state. An LLM call here is a Sonnet-default waste of cap. Template engine + step-index lookup is sufficient. |
| L2 | "Here's the method" — short pattern-based suggestion ("try factoring the common term") | 2 (Haiku) | `ideation_l2_hint` | Short structured suggestion (<150 tokens), conditioned on (error-type, concept, last-step). Bounded output shape, no multi-step reasoning. Haiku per the tier-2 classification default. |
| L3 | "Here's a worked example" — full step-by-step solution in the student's methodology | 3 (Sonnet) | `worked_example_l3_hint` | Full multi-step worked example with Bloom's-appropriate depth and methodology-aware framing. Same pedagogical surface as `full_explanation` but called from the hint UI rather than from a wrong-answer reaction. Separate task-name so its spend is separately trackable — cogsci (Dr. Kenji) specifically wanted L3 visible in the cost dashboard because over-use of L3 signals a scaffolding regression. |

The hint services themselves are not yet implemented. When they land, they inherit tiers from the table above, tagged via `[TaskRouting]`, with rows added to `routing-config.yaml` in the same PR.

**Why L1 is no-LLM.** Dr. Kenji's 2026-04-20 lens review flagged that L1 hints account for ≥70% of hint-ladder usage in comparable tutoring systems (Khan Academy, ASSISTments). At 70% of, e.g., 100k hints/day × $0.003 Sonnet, that is $210/day — $6.3k/month — of pure wasted cap on a task that is deterministic. L1 must be a template.

### §4 — `routing-config.yaml` additions

Three new rows are added to `contracts/llm/routing-config.yaml` under `task_routing:`:

- `full_explanation` — Sonnet primary, Sonnet 4.5 fallback, Haiku 4.5 fallback. Temp 0.3, max_tokens 512. Notes: "Personalised misconception explanation — methodology + Bloom's + affect shape the prompt. Sonnet sufficient; Haiku degraded fallback."
- `question_generation` — Sonnet primary, Sonnet 4.5 fallback. Temp 0.5, max_tokens 4096. Notes: "Tool-use structured question batches — matches `video_script` pattern for JSON compliance. Anthropic-only (Kimi's JSON-schema adherence insufficient for Bagrut-curriculum tool_use)."
- `quality_gate` — Haiku primary, Haiku 4.5 fallback, Sonnet 4.6 secondary fallback. Temp 0.1, max_tokens 1024. Notes: "0-100 rubric scoring across three dimensions via tool-use. Haiku deliberately — this evaluator runs on every AI-generated question and must not drift to Sonnet cost."

Two new rows are added to cover the hint ladder when it ships:

- `ideation_l2_hint` — Haiku primary, Haiku 4.5 fallback. Temp 0.2, max_tokens 150.
- `worked_example_l3_hint` — Sonnet primary, Sonnet 4.5 fallback. Temp 0.3, max_tokens 500.

Existing rows (`socratic_question`, `error_classification`, `diagram_generation`, `knowledge_graph_extraction`) are reused by the nine tagged services — no duplication.

### §5 — Allowlist cleanup

Once each of the nine services carries `[TaskRouting]`, it is no longer an allowlist exception. The nine `TODO(prr-012/145)` entries are removed from `scripts/shipgate/llm-routing-allowlist.yml`. The remaining allowlist entries (wire-protocol interfaces, DI composition roots, test doubles, architecture-test fixtures) are retained with their original justifications.

## Consequences

### Positive

- **Cost ceilings are explicit per service.** QualityGateService is pinned to tier 2 / Haiku; it cannot silently migrate to Sonnet. ErrorClassification stays at tier 2. The three high-volume student-facing paths (L3ExplanationGenerator, TutorActor, TutorMessageService) are explicitly tier 3 so their per-student spend appears in `cena_llm_cost_by_task_and_tier` with the right labels.
- **L1 hints are banned from reaching the LLM.** Saves an estimated $6k/month at product volume vs a naive "all hints are Sonnet" default.
- **Scanner can flip to strict.** Nine allowlist exceptions cleared; the remaining entries are genuine interface/protocol sites. The ship-gate maintainer can stage the strict-mode flip once CI pipelines are updated (tracked separately).
- **QualityGate cost is bounded.** Every AI-generated question runs through QualityGate; pinning it to Haiku caps its per-question evaluation cost at ~$0.0002 vs ~$0.01 on Sonnet — a 50x multiplier on a path that otherwise would have been silent.

### Negative

- **ExplanationGenerator + L3ExplanationGenerator share the `full_explanation` task row.** The distinction between "L2-ish explanation from classified error" and "L3 fully personalised explanation" exists in code, but for routing purposes both paths use Sonnet with the same max_tokens envelope. If the product later wants to degrade L2 to Haiku as a cost lever, that is a routing-config change and a tier re-assignment — it is not blocked by this ADR, but it does require another amendment.
- **QualityGate pinning to Haiku has a quality ceiling.** Haiku is weaker than Sonnet at rubric reasoning. If Haiku produces drift on FactualAccuracy/LanguageQuality scores at scale, the fix is not to raise the tier — it is to tighten the rubric prompt or pre-gate with CAS (already done for math/physics per RDY-034 §13). An ADR amendment would be required to migrate QualityGate to tier 3.

### Neutral

- **Hint services are not implemented yet.** This ADR pre-assigns their tiers so whichever sprint ships the hint feature does not have to re-litigate the decision. The three-level policy is the product contract; the task-names are reserved.
- **Kimi is not named as a tier.** Kimi's models appear in fallback chains but are not a separate tier — ADR-0026 §Tier selection policy defines three tiers by quality and cost class, and Kimi K2-turbo / K2.5 sit in the tier-2/tier-3 quality bands respectively. The `[TaskRouting]` tier reflects the Cena tier taxonomy, not the provider.

## Alternatives considered

- **(a) Route all nine at tier 3 to avoid thinking about tiers.** Rejected. Haiku-eligible paths (ErrorClassification, QualityGate) at Sonnet cost projects to ~$450/month on volume that Haiku covers at ~$9/month. The whole point of ADR-0026 is to make this kind of silent default impossible.
- **(b) Route all hint levels at tier 3 "for quality".** Rejected. Dr. Kenji's volume estimate ($6.3k/month on L1 alone) makes Sonnet-per-L1 a ship-blocker at 10k-student scale. L1 is provably template-generable from the last solved step; there is no quality argument for LLM generation at that level.
- **(c) Wait until the hint services are implemented before pre-assigning tiers.** Rejected. The allowlist carries `TODO(prr-145)` exactly because of this deferral. Keeping the nine services untagged in production while the hint services are TBD re-introduces the silent-Sonnet risk ADR-0026 was created to prevent.

## Cross-references

- [ADR-0026 — LLM three-tier routing governance](0026-llm-three-tier-routing.md) — framework this ADR fills in
- [contracts/llm/routing-config.yaml](../../contracts/llm/routing-config.yaml) — `task_routing` rows added
- [scripts/shipgate/llm-routing-allowlist.yml](../../scripts/shipgate/llm-routing-allowlist.yml) — `TODO(prr-012/145)` entries removed
- [scripts/shipgate/llm-routing-scanner.mjs](../../scripts/shipgate/llm-routing-scanner.mjs) — CI scanner whose strict-mode gate is advanced by this ADR
- [src/shared/Cena.Infrastructure/Llm/TaskRoutingAttribute.cs](../../src/shared/Cena.Infrastructure/Llm/TaskRoutingAttribute.cs) — attribute applied to each service

## Follow-up

1. Implement the two hint services (L2 `ideation_l2_hint`, L3 `worked_example_l3_hint`) with `[TaskRouting]` already required by the allowlist — no further ADR needed; tier pre-assigned here.
2. Implement the L1 template hint engine (no LLM) — Tier 1, no `[TaskRouting]`, no routing-config row.
3. Advance the scanner from `--advisory` to strict once the remaining Epic B sub-tasks (prr-012, prr-105) clear their call sites.
