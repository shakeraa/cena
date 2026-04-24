# TASK-PRR-432: Bagrut-spike simulation + chaos injection + capacity-validation report

**Priority**: P0
**Effort**: M (~1 week, blocked on PRR-430 + PRR-431)
**Lens consensus**: persona-sre, persona-ministry (Bagrut-day reliability), persona-finops (spike cost)
**Source docs**: [TASK-PRR-053](TASK-PRR-053-exam-day-capacity-plan-bagrut-traffic-forecast.md), [TASK-PRR-231](TASK-PRR-231-amend-capacity-plan-sat-pet.md)
**Assignee hint**: kimi-coder + SRE sign-off + decision-holder review of final report
**Tags**: source=actor-cluster-capacity, epic=epic-prr-k, priority=p0, chaos, capacity-validation, launch-gate
**Status**: Blocked on PRR-430 + PRR-431
**Tier**: launch
**Epic**: [EPIC-PRR-K](EPIC-PRR-K-actor-cluster-capacity-validation.md)

---

## Why

This is the measurement that closes the epic. PRR-053 and PRR-231 make capacity claims ("5-10× spike handled by auto-scaling", "7-window compound calendar at PRR-053 × 7.5"). Those claims are the reason we're comfortable pointing students at Cena on 15 June. This task either confirms them with measured numbers, or produces the amendment that forces a pre-Launch capacity re-plan.

Chaos injection matters because capacity-under-perfect-conditions is not capacity-at-03:00-on-Bagrut-morning. The real failure modes are: a node got evicted, Postgres autovacuum stalled for 30s, NATS experienced a brief partition, an Availability Zone flapped. If the cluster can't survive deliberately-induced versions of those during a 10× spike, it won't survive them in production either.

## How

### Simulation topology

Runs against the PRR-430 k3d cluster (locally) and against the staging k8s cluster (pre-Launch sign-off). Scale pattern:

- **Baseline warmup**: 10 minutes at 1× baseline load (via PRR-431 `student-session-baseline.js` driving 100 concurrent students).
- **Ramp**: 10-minute linear ramp from 1× to 10× (PRR-431 `bagrut-spike-shape.js` ramp phase).
- **Plateau**: 90 minutes at sustained 10× load (1,000 concurrent students on the Dev cluster; 10,000 on staging).
- **Chaos window**: during plateau minutes 30-60, inject scheduled chaos events.
- **Ramp-down**: 30 minutes back to baseline.
- **Post-spike verification**: 10 minutes at 1× to confirm the cluster recovers cleanly (no stuck actors, no leaked connections, event-store consistent).

### Chaos scenarios (real kills, not stubs)

Each scenario runs twice: once without chaos (control), once with chaos (treatment). Compare.

1. **Replica kill at plateau T+30min**: `kubectl delete pod cena-actor-host-2 --force`. Measure: actor-partition-reassignment time, session continuity (how many live sessions survive), p99 latency spike during reassignment, error rate.
2. **Postgres stall at T+40min**: `docker pause cena-postgres` for 30 seconds. Measure: Marten event-write queue depth, retry behavior, whether actors degrade gracefully or crash, recovery time after unpause.
3. **NATS outage at T+50min**: `docker stop cena-nats` for 15 seconds, then `docker start cena-nats`. Measure: message loss, dead-letter queue growth, consumer-rejoin time, whether Admin ↔ Actor Host communication degrades gracefully.
4. **Redis evict + flush at T+55min**: `redis-cli -h cena-redis FLUSHDB` (simulates catastrophic cache loss during peak). Measure: session-store rebuild time, p99 spike, re-authentication storm (how many students see a relogin).
5. **(Staging only) AZ flap**: drain one node with `kubectl drain`, wait 60s, `kubectl uncordon`. Measure: full AZ-loss recovery.

Each scenario has **pass/fail criteria**, not subjective judgement. Example for #1:
- Actor partitions reassigned within 5s (green), 5-15s (yellow), >15s (red — epic cannot close).
- Live session loss <0.5% (green), 0.5-2% (yellow), >2% (red).
- p99 latency spike <500ms during reassignment (green), 500-2000ms (yellow), >2000ms (red).

### Execution

- `scripts/chaos-run.sh bagrut-spike-full` — orchestrates the full ramp + plateau + chaos + recovery sequence. Prints a markdown report to stdout + writes raw metrics to `tests/load/runs/<timestamp>/`.
- Chaos injection uses Kubernetes primitives (`kubectl delete pod`, `kubectl drain`) + docker-compose primitives (`docker pause`, `docker stop`) — not a framework like Chaos Mesh. Reason: fewer moving parts; the chaos is the test-point, not the framework. If we later want Chaos Mesh for continuous chaos in staging, that's a separate task.
- Each run is idempotent and self-cleaning: `scripts/chaos-teardown.sh` returns the cluster to a known state.

### Capacity-validation report

The output is a markdown document at `docs/ops/capacity/bagrut-spike-validation-<YYYY-MM-DD>.md` with:

- **Measured per-replica ceiling**: sessions/second, events-written/second, p95 end-to-end latency at saturation.
- **Measured 10×-spike behavior**: peak concurrent sessions the cluster sustained, replica auto-scaling trajectory, dollar cost of the burst.
- **Chaos results table**: each scenario × green/yellow/red + the raw numbers.
- **PRR-053 + PRR-231 reconciliation**: does the measurement confirm the claim? If not, proposed amendments to both plans.
- **Open risks**: what the test couldn't cover and what remains unknown.

Per memory "Honest not complimentary": the report presents numbers with confidence intervals across ≥3 runs. No single-run conclusions.

### CI integration

- Nightly run of `bagrut-spike-full` in staging (if staging is available) at 10% of production load, without chaos. Posts short summary to Slack; full report archived.
- Pre-Launch gate: the report must land and be signed off by persona-sre + decision-holder. No sign-off → no Launch.

## Files

- `scripts/chaos-run.sh` (new)
- `scripts/chaos-teardown.sh` (new)
- `scripts/chaos-scenarios/` (new directory)
  - `replica-kill.sh`
  - `postgres-stall.sh`
  - `nats-outage.sh`
  - `redis-flush.sh`
  - `az-drain.sh` (staging-only)
- `tests/load/bagrut-spike-full.js` (new; orchestrates ramp + plateau + ramp-down using PRR-431 suites)
- `docs/ops/capacity/bagrut-spike-validation-template.md` (new — the report template)
- `docs/ops/runbooks/bagrut-morning-incident-response.md` (new — derived from scenarios; actionable by on-call)
- `docs/ops/runbooks/chaos-test-execution.md` (new)
- `.github/workflows/chaos-nightly.yml` (new; staging only)

## Definition of Done

- All 5 chaos scenarios execute cleanly against a PRR-430 k3d cluster; each produces raw metrics + a pass/fail verdict.
- Full `bagrut-spike-full` scenario runs end-to-end on k3d (Dev) and on staging (at smaller scale if staging capacity is limited).
- Capacity-validation report lands in `docs/ops/capacity/bagrut-spike-validation-<YYYY-MM-DD>.md` with measured numbers replacing (or amending) PRR-053 + PRR-231 projections.
- Bagrut-morning incident-response runbook covers each chaos scenario's symptoms + response actions; reviewed + signed off by persona-sre.
- No red-tier results remain. Any red triggers either a fix before Launch or a documented exception with compensating control.
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- Memory "No stubs — production grade" — real kills, real components.
- Memory "Senior Architect mindset" — measuring failure modes, not assuming them.
- Memory "Honest not complimentary" — ≥3 runs, CIs, no single-point claims.
- Memory "Full sln build gate".
- Memory "Check container state before build".
- [PRR-053](TASK-PRR-053-exam-day-capacity-plan-bagrut-traffic-forecast.md), [PRR-231](TASK-PRR-231-amend-capacity-plan-sat-pet.md) — the plans this task verifies or amends.
- [ADR-0012](../../docs/adr/0012-student-actor-split.md) — aggregates whose partition behavior under chaos matters.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + capacity-validation report URL + SRE sign-off>"`

## Related

- EPIC-PRR-K.
- PRR-430 (the cluster under test).
- PRR-431 (the load harness this scenario drives).
- PRR-053 + PRR-231 (amended by this report).
