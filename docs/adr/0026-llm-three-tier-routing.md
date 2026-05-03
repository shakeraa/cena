# ADR-0026 — LLM three-tier routing governance (routing-config.yaml as first-class primitive)

- **Status**: Accepted
- **Date**: 2026-04-20
- **Decision Makers**: Shaker (project owner), persona-finops (Svetlana), persona-enterprise (Rajiv), persona-sre (Dana), persona-cogsci (Dr. Kenji), persona-redteam (Marcus)
- **Task**: prr-004
- **Epic**: [EPIC-PRR-B — ADR-026 3-tier LLM routing governance](../../tasks/pre-release-review/EPIC-PRR-B-llm-routing-governance.md)
- **Related**: [ADR-0002](0002-sympy-correctness-oracle.md), [ADR-0003](0003-misconception-session-scope.md), [ADR-0001](0001-multi-institute-enrollment.md)

---

## Context

Three-tier model routing has been the de-facto norm in the Cena codebase since March 2026:

| Tier | Handler | Latency | Cost (per call, order of magnitude) | Use case |
|------|---------|---------|-------------------------------------|---------|
| **1** | Agent Booster (WASM, deterministic transform) | <1 ms | $0 | Syntactic transforms where an LLM is not needed — var→const, add type, rename, classify against a closed enum. Skip the LLM entirely. |
| **2** | Haiku | ~500 ms | ~$0.0002 | Low-complexity prompts (<30% complexity score): structured classification, binary content-filter gate, short extractions. |
| **3** | Sonnet or Opus (default) | 2–5 s | $0.003–$0.015 | Complex reasoning, Socratic tutoring, methodology switches, architecture and security reasoning. |

The tier policy is referenced in three load-bearing places — [CLAUDE.md](../../CLAUDE.md) §3-tier routing, [SUPERPROMPT.md](../../pre-release-review/SUPERPROMPT.md), and the [pre-release-review synthesis](../../pre-release-review/reviews/SYNTHESIS.md) — and the concrete task→model assignments live in [contracts/llm/routing-config.yaml](../../contracts/llm/routing-config.yaml) under `task_routing:`. None of those references point to an ADR, because until now no ADR existed. The convention was real but unenforced.

Pre-release review prr-004 (lens consensus: finops, enterprise, sre, cogsci, redteam) surfaced this as a P0 ship-blocker. The concrete risks:

- **Silent default to Sonnet.** Any PR can introduce a new LLM call site that implicitly lands on Sonnet without declaring a tier or a task name. At Cena's volume projection (10k students × ~5 Socratic turns/hour × $0.01 per Sonnet call), a single unrouted code path can breach the $30k/month global cap in a week. Svetlana's 2026-04-20 lens review estimated Socratic-at-default-Sonnet alone = ~$480k/mo vs $30k cap — ship-blocker.
- **No PR-level visibility.** Reviewers cannot see from the diff whether a new feature will default to Sonnet, Haiku, or skip the LLM. Cost projection is impossible without reading the runtime config at merge time.
- **No enforcement surface.** The existing ship-gate scanners (scan.mjs, rulepack-scan.mjs) do not look for LLM call sites. Adding a new call site without a `task_routing:` entry produces no build failure.
- **Cross-reference drift.** Multiple files reference "ADR-026" as if it were accepted. An external reviewer looking for the source of truth finds nothing and treats the tier policy as negotiable.

## Decision

### §1 — routing-config.yaml is a first-class architectural primitive

`contracts/llm/routing-config.yaml` is promoted from "operational config" to "architectural primitive". Every LLM call site in production code must map to a `task_routing:` row in that file. Introducing a new LLM-consuming feature requires a routing-config change in the same PR. The YAML is authoritative for:

- Model selection (primary + fallback chain).
- Per-task temperature and max-tokens.
- Rate limits, cost caps, circuit-breaker thresholds, cache strategy, PII policy.

### §2 — Tier selection policy

Tier assignment is not a developer preference; it is a cost and latency contract. Criteria:

- **Tier 1 (Agent Booster / no LLM)** — the task can be expressed as a deterministic transform over a known input shape. Examples: `var`→`const` rewrites, type annotation, enum classification against a closed set. If the task is deterministic, the LLM must not be called.
- **Tier 2 (Haiku)** — complexity score <30%, primarily classification or short structured extraction. Default when the task is a gate, a filter, or a tag — not a reasoning path. Per-call cost ceiling: **$0.0002**. Exceeding this ceiling without an ADR update is a tier violation.
- **Tier 3 (Sonnet / Opus)** — complex reasoning, pedagogy, methodology switches, security reasoning. Default per-call cost ceiling: **$0.015**. Opus is reserved for the highest-stakes reasoning path (currently only `methodology_switch` per routing-config.yaml).

Tier 3 is the default for any task whose output is directly student-facing and pedagogically load-bearing. Tier 2 is the default for any task that is a gate, a tag, or a classification. Tier 1 is the default when the LLM is not needed.

### §3 — `[TaskRouting]` attribute on every call site

A new `Cena.Infrastructure.Llm.TaskRoutingAttribute` (declared in [src/shared/Cena.Infrastructure/Llm/TaskRoutingAttribute.cs](../../src/shared/Cena.Infrastructure/Llm/TaskRoutingAttribute.cs)) annotates every class or method that constructs or consumes an LLM client (Anthropic, Moonshot, future providers). The attribute carries two strings:

```csharp
[TaskRouting("tier3", "socratic_question")]
public sealed class ClaudeTutorLlmService : ITutorLlmService { ... }
```

- `tier` ∈ { `tier1`, `tier2`, `tier3` } — constructor throws if anything else.
- `taskName` — must match a row under `task_routing:` in `routing-config.yaml`. The CI scanner cross-checks this.

The attribute is intentionally in `Cena.Infrastructure.Llm` (not a shared-kernel project) because every host (Cena.Actors, Cena.Admin.Api, Cena.Student.Api) already references Cena.Infrastructure; no new dependency is created.

### §4 — CI scanner

A new `scripts/shipgate/llm-routing-scanner.mjs` walks `src/**/*.cs` and reports, for every call site that either (a) references `Anthropic` / `AnthropicClient` / `OpenAi*`, or (b) implements `ILlmClient`, or (c) declares a class whose name ends in `LlmService` or `LlmClient` or `LlmGenerator`, whether the enclosing class or a method on that class carries `[TaskRouting(...)]`.

On violation, the scanner emits:

```
LLM call at src/actors/Cena.Actors/Foo/BarLlmService.cs:42 missing [TaskRouting] attribute.
Either tag the class with [TaskRouting("tier2|tier3", "<task-name>")] matching a row in
contracts/llm/routing-config.yaml OR add this file to
scripts/shipgate/llm-routing-allowlist.yml with a justification.
```

The scanner starts in `--advisory` mode (exit 0, violations surfaced in logs) and flips to strict (non-zero exit) once every legitimate call site is either tagged or allowlisted. The flip is gated on Epic B sub-task completion (prr-012, prr-145, prr-105) because those tasks determine the tier assignments for the currently-untagged paths.

### §5 — Allowlist for legitimate untagged sites

`scripts/shipgate/llm-routing-allowlist.yml` carries a short list of paths that are LLM-adjacent but should not themselves be tagged: abstract base classes, DI factory registration sites, test doubles, the router dispatcher (which multiplexes across tiers and has no single tier), and the provider-agnostic wire protocol (`ILlmClient` / `LlmRequest` / `LlmResponse`). Each entry carries a one-line justification.

### §6 — Per-student cost cap

The per-student cost cap defined in routing-config.yaml §4 remains authoritative:

- **$1.50/day hard cap** — any call that would push a student's same-calendar-day spend over $1.50 is refused by `RedisCostCircuitBreaker` with a structured error.
- **$0.70/day warn threshold** — crossing this threshold emits an alert but does not refuse the call.
- **$20.00/month per student** — per-student monthly ceiling.
- **$30,000/month global** — aggregate ceiling across all models; breach trips the emergency kill switch.

The circuit breaker runs on every outbound call, not periodically. Enforcement is Redis-atomic to survive replicas and pod restarts.

### §7 — Cost alerts

Prometheus metric `cena_llm_cost_by_task_and_tier{task_name,tier,model_id,tenant_id,success}` is emitted on every call. Alert rules (defined in `deploy/helm/cena/templates/prometheus-rules.yaml` — forthcoming under this ADR's follow-up) fire on:

- Per-institute daily cap breach.
- Per-task monthly spend >120% of allocated budget.
- Task-name appearing in metric but missing from `routing-config.yaml` (means a call site slipped past the scanner).

## Consequences

### Positive

- **Predictable cost.** Every LLM call is traceable from routing-config.yaml → `[TaskRouting]` attribute → runtime metric. Reviewers see the tier in the diff; no more "the analytics said Sonnet looks better" paths shipping silently.
- **Cost ceilings become contracts.** Tier 2 calls that drift over $0.0002 per call fail the build via the scanner's cost-ceiling cross-check (future sub-task). Tier 3 calls that drift over $0.015 get the same treatment.
- **Feature reviewability.** An LLM-consuming PR is a routing-config.yaml change + attribute + implementation. One diff, three artefacts, all visible to the reviewer.
- **Cross-referenceable.** CLAUDE.md, SUPERPROMPT.md, and the pre-release-review memory file now link to this ADR instead of the phantom "ADR-026" string.

### Negative

- **Epic B burden.** Every existing LLM call site must eventually carry the attribute. This ADR kicks the retroactive apply into Epic B follow-up sub-tasks (prr-012, prr-145, prr-105). Until those land, the scanner runs in advisory mode. Live code is un-enforced during this window; the allowlist carries the untagged sites with a `TODO(prr-012)` comment.
- **Schema churn guardrail.** Every ADR-0026 change (new tier, new ceiling, tier reassignment of a task) requires human sign-off — no LLM-agent can amend this ADR by itself. This is deliberate; routing policy is where silent drift kills the cost cap.

### Neutral

- **`LlmClientRouter` is not tagged.** The router dispatches across tiers and has no single tier; it is explicitly allowlisted. Every call it forwards carries its own tier via the consumer's `[TaskRouting]`.
- **Test doubles are not tagged.** `NullTutorLlmService` and in-memory fakes under `tests/` are allowlisted. They do not make LLM calls.

## Alternatives considered

- **(a) No enforcement (status quo).** Rejected. Silent-default-to-Sonnet is the exact failure mode prr-004 raised as ship-blocker. An unenforced norm is equivalent to no norm at production volume.
- **(b) Strip all LLM calls.** Rejected. Cena's core pedagogical value depends on LLM-driven Socratic tutoring and methodology switches. Removing LLM calls is not a product we can ship.
- **(c) Single-tier Haiku-only.** Rejected. Quality collapse on reasoning tasks (methodology switch, Socratic explanation grading, video-script generation) is empirically documented in the routing-config.yaml `notes:` fields. Haiku is a cost-optimised classifier, not a reasoner.
- **(d) Runtime enforcement only (no attribute).** Rejected. Runtime-only enforcement produces no compile-time or PR-review surface; a new call site slips in unnoticed, breaches the cap in production, and the fire drill happens during a Bagrut morning. The compile-time + CI scanner layer is necessary because the runtime layer fires too late.

## Cross-references

- [CLAUDE.md §3-tier routing](../../CLAUDE.md)
- [SUPERPROMPT.md §system purpose](../../pre-release-review/SUPERPROMPT.md)
- [EPIC-PRR-B — ADR-026 3-tier LLM routing governance](../../tasks/pre-release-review/EPIC-PRR-B-llm-routing-governance.md)
- [prr-004 task body](../../tasks/pre-release-review/TASK-PRR-004-promote-contracts-llm-routing-config-yaml-governance-to-adr.md)
- [contracts/llm/routing-config.yaml](../../contracts/llm/routing-config.yaml) — authoritative `task_routing:` table
- [src/shared/Cena.Infrastructure/Llm/TaskRoutingAttribute.cs](../../src/shared/Cena.Infrastructure/Llm/TaskRoutingAttribute.cs) — the attribute declaration
- [scripts/shipgate/llm-routing-scanner.mjs](../../scripts/shipgate/llm-routing-scanner.mjs) — CI scanner
- [scripts/shipgate/llm-routing-allowlist.yml](../../scripts/shipgate/llm-routing-allowlist.yml) — allowlisted untagged sites

## Follow-up (Epic B)

1. **prr-145** — ADR on hint-generation tier selection (clarifies L1/L2/L3 tier choice).
2. **prr-012** — Cap Socratic self-explanation to 3 LLM calls/session + cache reuse.
3. **prr-105** — Tutor turn-budget enforcement from ADR-0002.
4. **prr-047** — LLM prompt-cache enforcement + hit-rate SLO.
5. **prr-046 / prr-084 / prr-112** — Cost projection dashboard + per-institute alerts + per-feature/per-cohort admin UI.
6. **prr-143** — Trace-id on every LLM call (observability backbone).
7. **prr-095** — Runbook: LLM vendor outage failover.

Flipping the scanner from `--advisory` to strict is gated on 1–3 completing (the tier assignments they pin down remove the remaining "TODO(prr-012)" allowlist entries).
