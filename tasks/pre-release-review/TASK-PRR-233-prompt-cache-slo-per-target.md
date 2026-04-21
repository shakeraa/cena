# TASK-PRR-233: Prompt cache hit SLO per target + finops observability

**Priority**: P1 — persona-finops
**Effort**: S (3-5 days)
**Lens consensus**: persona-finops
**Source docs**: persona-finops findings (cache hit drop from ~85% → ~68-72% with 4-target variation, right at PRR-047 SLO floor)
**Assignee hint**: kimi-coder + SRE review
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p1, finops, observability
**Status**: Blocked on PRR-218 (aggregate wiring)
**Source**: persona-finops review
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Per persona-finops, multi-target context variation is expected to drop prompt cache hit rate from ~85% → ~68-72%, right at PRR-047's 70% SLO floor. Add per-target-count observability so we can detect SLO breach early.

## Metrics

- `llm.prompt_cache.hit_rate{target_count=1..5}` — bucketed.
- `llm.prompt_cache.hit_rate{exam_code=...}` — per catalog exam.
- `llm.session_cost_usd{target_count=1..5}` — per-session LLM dollar cost estimation.
- Alert: cache hit rate < 70% sustained 1h for any `target_count` bucket.
- Dashboard: per-exam-code and per-target-count hit-rate trends.

## Cost ceiling gate

- Prometheus rule triggers at per-student-per-month estimated LLM cost > $3.30 (persona-finops ceiling). Gate is alert-only at Launch; budget-enforce in a later task.

## Files

- `src/infra/Cena.Infra.Observability/LlmCacheMetrics.cs` or equivalent — extend with per-target buckets.
- Prometheus / Grafana dashboard JSON under `infra/observability/dashboards/`.
- Alert rules under `infra/observability/alerts/`.

## Definition of Done

- Metrics emitted on every LLM call.
- Dashboard published and linked from runbook.
- Alert rule fires correctly on fixture.
- persona-finops sign-off on buckets + thresholds.

## Non-negotiable references

- PRR-047 (cache hit SLO).
- ADR-0026 (3-tier routing).
- Memory "Honest not complimentary" (numbers + CIs).

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<dashboard URL + alert fixture>"`

## Related

- PRR-047, ADR-0026, PRR-218, PRR-226.
