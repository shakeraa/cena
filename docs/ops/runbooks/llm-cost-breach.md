# Runbook — LLM cost breach (per-institute)

**Alert sources**: `CenaLlmInstituteSpendWarning` (80%) / `CenaLlmInstituteSpendCritical` (100%)
**Ticket source**: prr-084 (Epic B: LLM routing governance)
**Primary paging channel**: SRE PagerDuty for critical; `#cena-finops` Slack for warning
**Owner**: cena-finops (warning) / cena-sre (critical, co-owned with cena-finops)

---

## 1. Context

Every LLM call carries `institute_id` as a Prometheus label
(`cena_llm_call_cost_usd_total{institute_id="…"}`, prr-046). We project
spend per institute as `rate(7d) × (30/7)` and compare against a
per-institute monthly ceiling recorded in
`cena_llm_institute_monthly_ceiling_usd`.

Severities:

| Band | Projection vs. ceiling | Severity | Route |
|------|------------------------|----------|-------|
| Normal | < 80% | — | — |
| Warning | 80% to <100% | `warning` | Slack `#cena-finops` |
| Critical | ≥ 100% | `critical` | PagerDuty (SRE + finops on-call) |

An institute that emits spend but has NO ceiling row fires
`CenaLlmInstituteMissingCeiling` (info). Default fallback is $100/mo
until finops records a real ceiling — intentionally conservative so an
unbounded tenant cannot silently burn the global budget.

## 2. First 15 minutes (on-call SRE or finops rotator)

1. **Confirm the alert is not a label-split artefact.** Query
   `sum by (institute_id) (increase(cena_llm_call_cost_usd_total[1h]))`
   in Grafana. If the offending institute is `unknown`, jump to §5.
2. **Pull the "Per-institute projected spend" Grafana panel.** Ranking
   by projection-over-ceiling ratio. The top row is the alert's subject.
3. **Cross-check with the per-cohort dashboard (prr-112)**:
   `GET /api/admin/llm-cost/per-cohort?cohort=…&from=…&to=…`. This
   answers "which feature × cohort is driving the spike".
4. **Look for deploys in the last 24h** that touched
   `contracts/llm/routing-config.yaml`. A routing-config change that
   bumps a feature from Haiku → Sonnet silently 15× its cost.
5. **Page finops-on-call (critical only).** Their decision tree is
   in §4. Warning-level: post in `#cena-finops` + ack within 2h.

## 3. Detection & failover checklist

- [ ] Confirm the breach is real (not scrape gap / metric-reset).
- [ ] Identify root cause: routing-config regression / organic growth
      / abusive cohort / new feature rollout.
- [ ] Determine whether the institute has agreed-to elasticity
      (enterprise SOW allows overrun) or a hard cap (SMB default).
- [ ] Capture a snapshot of current spend + projection in the incident
      record.

## 4. Actions by scenario

### 4a. Routing-config regression

A deploy in the last 24h bumped a feature's `route:` to a higher tier.
**Action**: roll back the PR. Do NOT raise the ceiling to paper over a
regression — that is the exact failure mode prr-084 prevents.

### 4b. Organic growth (enterprise SOW elastic)

Enterprise tenant has signed elastic pricing. **Action**: raise the
`cena_llm_institute_monthly_ceiling_usd` recording-rule value after
finops signs off. Log the ceiling change in
`docs/ops/decisions/` with the signoff link.

### 4c. Organic growth (SMB fixed cap)

Default SMB tenant has fixed monthly cap. **Action**: apply a soft
throttle via Admin API (institute-settings → `llm_tier_ceiling`
lowers Tier-3 quotas for this institute). Notify the institute
super-admin; offer an upgrade path.

### 4d. Abusive cohort / runaway script

A single cohort or class is driving 5×+ the usual per-user spend.
**Action**: use `GET /api/admin/llm-cost/per-cohort` (prr-112) to
confirm. Pause the cohort's `turn-budget` (prr-105) while triaging;
notify the institute admin.

### 4e. Vendor outage cascade

Prompt-cache miss rate spiked after a Redis incident; we retried every
Socratic turn fresh against Sonnet. **Action**: cross-reference
`CenaPromptCacheHitRateCritical` (prr-047). Failover per
[llm-vendor-outage.md](llm-vendor-outage.md) applies in reverse —
re-prime cache before declaring resolved.

## 5. `institute_id="unknown"` handling

`unknown` means a call site didn't thread tenant scope (see
`LlmCostMetric.cs` rationale comment). This is a known gap; see
per-feature dashboard's top-10-institutes panel — `unknown` is always
the work-to-close marker.

**Action**:

1. Identify the feature from `rate(cena_llm_call_cost_usd_total
   {institute_id="unknown"}[1h])`.
2. File an issue to thread `institute_id` through that feature's
   LLM call path.
3. Do NOT raise the ceiling for `unknown` — it is a gap, not a
   tenant.

## 6. Resolution criteria

- Projection drops below 80% of ceiling for 30 consecutive minutes.
- Root-cause ticket linked in the incident record.
- If a ceiling was raised: signoff link captured in decisions log.
- If a cohort was paused: unpause confirmed with institute admin.

## 7. Related

- prr-084 — source task
- prr-046 — per-feature cost metric the alert rides on
- prr-047 — prompt-cache hit-rate SLO (upstream driver)
- prr-112 — per-cohort cost dashboard (triage surface)
- [ADR-0026 §7](../../adr/0026-llm-three-tier-routing.md) — routing + cost-alert architecture
- [llm-vendor-outage.md](llm-vendor-outage.md) — cascade failover runbook (prr-095)
