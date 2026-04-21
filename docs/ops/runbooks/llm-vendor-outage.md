# Runbook — LLM vendor outage & failover

**Ticket source**: prr-095 (Epic B: LLM routing governance)
**Primary paging channel**: SRE PagerDuty
**Owner**: cena-sre (co-owned with cena-platform for config rollbacks)

---

## 1. Context

Cena's 3-tier LLM routing ([ADR-0026](../../adr/0026-llm-three-tier-routing.md)) runs every student-facing inference through a vendor. When that vendor degrades — rate-limiting all requests, returning 5xx at scale, or going fully dark — every Socratic turn, hint-generation, and question-authoring path fails unless the cascade below is already wired.

This runbook documents (a) how to detect the outage, (b) the automatic cascade, and (c) what ops has to do manually. Everything after §4 is the runbook for a **live incident**.

## 2. Detection signals

The alert sources that indicate a vendor outage (any of):

- `cena_llm_vendor_error_rate` > 10% for 5 min on a specific `{vendor, model_id}`
- `cena_llm_request_latency_p99` > 15s (normal p99 is ~3s for Sonnet)
- `cena_llm_circuit_breaker_state{state="open"}` > 0 for any tier-3 model
- `CenaPromptCacheHitRateCritical` + visible drop in `cena_llm_call_total` rate (miss + fail = no cache prime)

Escalation thresholds:

| State | Error rate | Action |
|-------|-----------|--------|
| Degraded | 1–10% | Record; no operator action |
| Unstable | 10–50% | Automatic cascade engages (see §3) |
| Dark | > 50% for 5 min | PagerDuty SRE + manual runbook (this doc) |

## 3. Automatic cascade (already wired)

The routing layer degrades **in order**:

1. **Tier 3 Sonnet → Tier 2 Haiku.**
   When `LlmCircuitBreakerActor` opens the Sonnet circuit for a task, routing-config's `fallback_tier` field (per-task) redirects to Haiku. Cost drops 30×, quality drops on hard reasoning tasks but stays acceptable for hints + classification.
2. **Tier 2 Haiku → Static fallback content.**
   For tasks that have a pre-baked fallback (Socratic hints use `StaticHintLadderFallback`), the router short-circuits to the static content and logs `route=static_fallback`. Student sees a pedagogically-reviewed generic prompt ("think about what operation inverts what you wrote") instead of a vendor error.
3. **Tier 1 (Agent Booster / WASM) unchanged.**
   Tier-1 has no vendor dependency — it is WASM-local. Outages never affect it.

Everything above is automatic. Operators do NOT flip a switch to engage the cascade.

## 4. Manual actions (operators)

### 4a. Confirm the outage is external, not internal.

Before declaring a vendor outage, eliminate:

- [ ] Anthropic status page green? (https://status.anthropic.com)
- [ ] Our outbound firewall / egress NAT healthy?
- [ ] PiiPromptScrubber not silently rejecting every prompt? Check
      `cena_pii_prompt_scrub_blocked_total` rate.
- [ ] Prompt cache Redis not stuck? Check
      `cena_prompt_cache_redis_up`.

If any of the above is the true cause, stop and triage that instead.

### 4b. Declare the incident.

Open an incident in PagerDuty; bridge channel `#incident-llm-vendor`.
Assign: SRE primary, platform secondary, product lead as liaison.

### 4c. Freeze non-essential LLM consumption.

Disable Tier-3 features that still queue fresh calls. The fast levers:

- `POST /api/admin/feature-flags/freeze`
  `{ "features": ["question-generation", "explanation-l3"] }`
- This does NOT affect student Tier-2 Socratic hints — those already
  degraded automatically via §3.

### 4d. Monitor cache hit rate climb.

When the cascade is engaging correctly, you should see:

- `cena_llm_call_total{tier="tier3"}` drops 90%+
- `cena_llm_call_total{tier="tier2"}` rises proportionally
- `cena_prompt_cache_hits_total{cache_type="hint_l2"}` holds steady

If tier-2 is also dark (full-vendor outage), §4e applies.

### 4e. Full vendor blackout — Tier 2 + Tier 3 unreachable.

This is the dark-scenario. Actions:

- [ ] Surface an in-app banner via `/api/admin/banner/activate`
      with pre-approved copy (no loss-aversion language per
      ship-gate scanner): "We're experiencing a temporary hiccup
      on the helper — saved work is safe; hints will be back
      shortly."
- [ ] Ensure `StaticHintLadderFallback` remains warm; the static
      content store is local to the pod and should be unaffected.
- [ ] Verify practice-mode questions (CAS-gated by ADR-0002) STILL
      work — CAS is local SymPy, not a vendor.
- [ ] Decide deferral: if the vendor outage exceeds 30 minutes at
      an exam-prep-critical time, activate the pre-committed
      question set fallback (Ministry practice exams, ADR-0002
      reference material).

## 5. Recovery criteria

- Vendor error rate < 1% for 15 consecutive minutes on `{sonnet, haiku}`.
- `cena_llm_circuit_breaker_state{state="open"}` drops to 0.
- Prompt-cache hit rate returns to > 40% (SLO from prr-047).
- No new Tier-3 failures in the last 30 minutes.

Once all of the above hold:

1. Close the incident.
2. Un-freeze non-essential features via
   `POST /api/admin/feature-flags/unfreeze`.
3. Remove the in-app banner.
4. Write a post-mortem within 48h (cost impact, cache re-prime time, any
   student-facing degradation observed).

## 6. Testing & verification

`LlmVendorOutageCascadeTests.cs` (in `Cena.Actors.Tests`) exercises the
cascade end-to-end with a mocked vendor returning 500 on every call:

- Tier-3 circuit opens → request retries against Tier-2.
- Tier-2 circuit opens → request returns the static fallback content.
- Static fallback carries `route=static_fallback` on the metric label.
- No vendor call is made during circuit-open; the cost counter is
  quiet during the outage window.

Run: `dotnet test src/actors/Cena.Actors.Tests/Cena.Actors.Tests.csproj --filter LlmVendorOutageCascadeTests`

## 7. Related

- prr-095 — source task (this runbook)
- [ADR-0026](../../adr/0026-llm-three-tier-routing.md) — 3-tier routing, cascade semantics
- prr-047 — prompt-cache SLO (upstream cost driver)
- prr-084 / [llm-cost-breach.md](llm-cost-breach.md) — cost alerts during degradation
- `src/actors/Cena.Actors/Gateway/LlmCircuitBreakerActor.cs` — circuit state machine
- `src/actors/Cena.Actors/Tutor/StaticHintLadderFallback.cs` — Tier-2 → static content path
