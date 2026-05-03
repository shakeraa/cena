# EPIC-PRR-K: Actor cluster + capacity validation + load-test harness

**Priority**: P0 — launch-gate; invalidates PRR-053/PRR-231 capacity claims if not done
**Effort**: L-XL (3-5 weeks aggregate across 3 sub-tasks; can parallelize compute vs runner work)
**Lens consensus**: persona-sre primarily; persona-ministry + persona-finops downstream beneficiaries
**Source docs**:
- [src/actors/Cena.Actors.Host/Program.cs:489-537](../../src/actors/Cena.Actors.Host/Program.cs) — existing TestProvider/KubernetesProvider switch
- [docker-compose.app.yml](../../docker-compose.app.yml) — current single-replica actor-host deploy shape
- [TASK-PRR-053](TASK-PRR-053-exam-day-capacity-plan-bagrut-traffic-forecast.md) — Bagrut-day capacity plan (unverified numbers today)
- [TASK-PRR-231](TASK-PRR-231-amend-capacity-plan-sat-pet.md) — SAT+PET 7-window extension
- Persona-sre findings across 001-brief + 002-brief (correlated latency, no runbook, capacity claims stale)
**Assignee hint**: kimi-coder + SRE review; human-architect signs off on k8s topology
**Tags**: source=actor-cluster-capacity, type=epic, epic=epic-prr-k, sre, launch-gate, no-stubs
**Status**: Not Started — ready to spawn once decision-holder greenlights
**Tier**: launch
**Related epics**: [PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md) (multi-target scheduler feeds this load), [PRR-H](EPIC-PRR-H-student-input-modalities.md) (vision-LLM fallback has correlated outage surface), [PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md) (photo pipeline shares the actor-host capacity budget)

---

## 1. Why this epic exists

Cena's actor host uses Proto.Actor 1.8.0 with Proto.Cluster + Proto.Remote + Proto.Cluster.Kubernetes. The clustering framework is real; the wiring is real (Program.cs lines 489-537 branch on environment, TestProvider in Dev and KubernetesProvider in Prod). Three facts invalidate the current deploy:

1. **Dev compose runs a single actor-host replica with `container_name: cena-actor-host`.** Scaling via `docker-compose up --scale` is blocked by the name collision. No one has ever seen 2+ real replicas coordinate on a developer laptop.
2. **TestProvider is single-process, in-memory.** It satisfies `AddProtoCluster(…)` at compile time but does not exercise Partition+Identity placement, replica rebalance on pod churn, or k8s API discovery — the exact things that break under production pod-rollover.
3. **PRR-053 capacity numbers are unverified.** The plan projects 5-10× Bagrut-morning spike handled by auto-scaling; nothing in the repo measures per-replica capacity, the rebalance-under-churn time, or the tail latency when an actor migrates mid-session. Shipping on those projections is shipping on faith.

Per the "Senior Architect mindset" memory: questions before tasks. The three questions this epic answers:

- **Q1.** Does Cena's Proto.Cluster configuration actually cluster under production-shape discovery? We don't know.
- **Q2.** What is the real per-replica capacity? Sessions/second, event-write/second, Redis-hit-rate at p95 — we don't know.
- **Q3.** When we deliberately break things (kill a replica mid-session, kill Postgres for 30s, saturate NATS) does the cluster degrade or fail catastrophically? We don't know.

Every one of those "we don't know" is a 03:00-on-Bagrut-morning failure mode per memory "Honest not complimentary." The epic's job is to turn them into measured numbers before Launch.

## 2. How the epic answers those questions

Three coordinated sub-tasks. Each one is production-grade (no simulation frameworks, no fake actors, no "we'll harden it later"):

### PRR-430 — Local k3d cluster running the real KubernetesProvider wiring

Replaces the Dev TestProvider path with actual k8s-native discovery. Uses k3d (k3s-in-Docker) so the developer runs `k3d cluster create cena-dev` and the real `Proto.Cluster.Kubernetes.KubernetesProvider` + `PartitionIdentityLookup` code path boots with N replicas. Same manifests that go to production k8s. **No new Dev-only code.** The point is to remove the Dev/Prod divergence, not widen it.

### PRR-431 — Production-grade load-test harness (k6 with xk6-nats extensions)

Real HTTP + WebSocket + NATS load generator that exercises the actual Student/Admin API endpoints + SignalR hubs + actor-hosted session flows end-to-end. Stored in `tests/load/` as versioned code. Runs locally against the PRR-430 k3d cluster and against staging against the real deployment. Output: p50/p95/p99 per endpoint + sustained throughput ceiling per replica count. This is the number that makes or breaks the PRR-053 claims.

### PRR-432 — Bagrut-spike simulation + chaos scenarios

A scripted test that spins up PRR-430 at production replica-count, loads PRR-431 at 5×, 8×, 10× baseline, and injects chaos: kill a replica at t=120s, stall Postgres at t=240s, drop NATS at t=360s. Measures session-continuity rate, actor-rebalance time, p99 latency during each event. Output: a capacity-validation report that either confirms PRR-053/PRR-231 or forces their amendment. Integrates with existing OpenTelemetry + Grafana (docker-compose.observability.yml already there).

## 3. Non-negotiables (architect guardrails)

- **No stubs, no simulations of components Cena owns.** The actor host is the real actor host; the cluster is a real cluster; Marten writes real events; Redis caches real sessions. The only test-doubles allowed are outbound vendor calls (Anthropic, Firebase, Mashov) and those are mocked at their existing seams (`CenaLlmGateway`, `FirebaseAuthService`).
- **Full `Cena.Actors.sln` builds cleanly before any PR merges.** Per memory 2026-04-13.
- **Dev and Prod deploy shapes converge.** After this epic, the ONLY difference between `k3d cluster create cena-dev` and the production deployment is replica count + region. Same manifests, same providers, same discovery, same identity lookup.
- **Measurements replace assertions.** Anywhere PRR-053 / PRR-231 say "handles 5-10× spike," this epic produces a number with a confidence interval. Per memory "Honest not complimentary."
- **Observability is first-class.** Every sub-task emits metrics through the existing OpenTelemetry pipeline to Grafana; no bespoke dashboards that die when ops isn't watching.
- **Chaos tests are real kills, not simulated degradation.** `kubectl delete pod` on a running replica, `docker stop cena-postgres` for 30s, `nats-server -signal stop` — not stubs returning 500.

## 4. Sub-task table

| ID | Title | Priority | Effort |
|---|---|---|---|
| [PRR-430](TASK-PRR-430-k3d-actor-cluster-dev-environment.md) | Local k3d cluster running production-parity KubernetesProvider | P0 | M (1-2 weeks) |
| [PRR-431](TASK-PRR-431-k6-load-test-harness.md) | k6 load-test harness hitting real endpoints + WS + NATS | P0 | M (1-2 weeks, parallel with PRR-430) |
| [PRR-432](TASK-PRR-432-bagrut-spike-chaos-simulation.md) | Bagrut-spike simulation + chaos injection + capacity report | P0 | M (1 week, blocked on 430+431) |

## 5. Definition of Done (epic-level)

- A developer can run `k3d cluster create cena-dev && ./scripts/deploy-local.sh && npm test:load:baseline` and see ≥2 actor-host replicas forming a real Proto.Cluster, serving real load, reporting real metrics.
- PRR-053 + PRR-231 capacity numbers have measured confirmation or documented amendments (ranges, not points; CIs cited).
- Chaos scenarios run in CI nightly and fail the build on regression.
- Runbook updates: `docs/ops/runbooks/actor-host-*.md` covers replica-down, postgres-stall, nats-outage. Ties to persona-sre non-negotiables.

## 6. Non-negotiable references

- [ADR-0001](../../docs/adr/0001-multi-institute-enrollment.md) — tenancy isolation.
- [ADR-0012](../../docs/adr/0012-student-actor-split.md) — StudentActor split; successor aggregates live on the cluster.
- [ADR-0026](../../docs/adr/0026-llm-three-tier-routing.md) — LLM routing; cluster outage must not cascade to vendor calls.
- [PRR-053](TASK-PRR-053-exam-day-capacity-plan-bagrut-traffic-forecast.md), [PRR-231](TASK-PRR-231-amend-capacity-plan-sat-pet.md) — claims this epic verifies.
- Memory "No stubs — production grade" (2026-04-11).
- Memory "Senior Architect mindset".
- Memory "Full sln build gate" (2026-04-13).
- Memory "Check container state before build" (2026-04-19).
- Memory "Honest not complimentary" (2026-04-20).

## 7. Out of scope (intentional)

- Multi-region k8s — Launch is single-region (il-central-1 preferred). Multi-region DR is Post-Launch.
- Proto.Actor upgrade beyond 1.8.0 — stick with what's wired.
- Non-k8s production shapes (Nomad, ECS, bare-metal) — k8s is the production target.
- LLM vendor load testing — different workload, belongs in PRR-233 finops observability or an LLM-specific runbook.

## 8. Reporting

Epic closes when all 3 sub-tasks close AND the capacity-validation report lands in `docs/ops/capacity/exam-day-capacity-plan.md` with measured numbers.

## 9. Related

- Depends on existing infra (Postgres, Redis, NATS, Neo4j) running via docker-compose or inside k3d.
- Feeds: PRR-053 + PRR-231 amendment with real numbers.
- Coordinates with: EPIC-PRR-J photo-upload pipeline capacity share; EPIC-PRR-H vision-LLM fallback correlated outage.
