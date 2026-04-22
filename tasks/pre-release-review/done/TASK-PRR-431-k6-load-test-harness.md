# TASK-PRR-431: k6 load-test harness with xk6-nats + WebSocket, versioned in `tests/load/`

**Priority**: P0
**Effort**: M (1-2 weeks; can parallel with PRR-430)
**Lens consensus**: persona-sre, persona-finops
**Source docs**: [TASK-PRR-053](TASK-PRR-053-exam-day-capacity-plan-bagrut-traffic-forecast.md), persona-sre findings in 001-brief + 002-brief
**Assignee hint**: kimi-coder + SRE review
**Tags**: source=actor-cluster-capacity, epic=epic-prr-k, priority=p0, load-test, observability
**Status**: Ready (can run in parallel with PRR-430)
**Tier**: launch
**Epic**: [EPIC-PRR-K](EPIC-PRR-K-actor-cluster-capacity-validation.md)

---

## Why

PRR-053's Bagrut-morning capacity projection is "5-10× baseline for 4-5 hours" — a claim with no measurement behind it. Persona-sre has flagged this across 001 + 002 reviews. Without a real load generator, we are shipping a plan whose failure mode lives at 03:00 on 15 June. Per memory "Honest not complimentary": numbers over assertions.

The harness must be:

- **Code, not clicks.** Versioned in git, reviewable, runnable by any engineer.
- **Realistic traffic, not synthetic garbage.** A real student session has HTTP calls + WebSocket/SignalR connection + actor-hosted message flow + Marten event writes + Redis session reads + NATS publishes. The harness must exercise all of those in their natural ordering.
- **Deterministic where it matters.** Same seed → same traffic profile → comparable runs across versions.
- **Observable.** Every load run produces Prometheus metrics that show up in the existing Grafana dashboards alongside the cluster's own metrics, so we can correlate "harness emitted X" with "cluster did Y."

## How

### Tool choice: k6 + xk6-nats + xk6-websockets

k6 was chosen over NBomber (.NET-native) because:

- k6 speaks HTTP, WebSocket, gRPC, and via xk6-nats speaks NATS natively — the harness needs all three.
- k6 extensions (`xk6-nats`, `xk6-websockets`, `xk6-output-prometheus-remote`) are production-grade.
- k6 has a mature Prometheus + Grafana integration; metrics flow into the existing observability stack without bespoke wiring.
- k6 runs as a Docker container — no Node or .NET installed on the runner host required.

NBomber stays a valid fallback if the team later wants .NET-native tests; for this epic the answer is k6.

### Test suites to build

Each suite is a versioned `.js` file under `tests/load/`:

- **`student-session-baseline.js`** — simulates one student completing a 20-minute session: login → fetch session → answer 15 items (mix of MC + step-solver math) → receive SignalR updates → complete session → fetch progress. Runs at 1 VU (virtual user) for correctness verification. No load generated; this is the "does the harness work?" test.
- **`student-session-ramp.js`** — ramps from 1 → 10 → 100 → 500 → 1,000 → 2,000 concurrent VUs over 30 minutes. Each VU runs the baseline flow on repeat. Outputs p50/p95/p99 per endpoint + throughput-per-replica-count.
- **`admin-api-smoke.js`** — exercises teacher/admin flows: classroom view, bulk target assignment (PRR-236), roster import, audit log read. 50 VUs sustained 5 minutes.
- **`photo-upload-burst.js`** — simulates Q1 photo-of-solution uploads (PRR-J pipeline). Bursts 200 uploads / 60s. Validates rate-limiting + vision-LLM queue behavior.
- **`nats-publish-load.js`** — direct NATS publish load on the bus-channels that actor-host consumes. Validates the bus under write pressure.
- **`bagrut-spike-shape.js`** — the shape of the real Bagrut spike: 10-minute ramp from baseline to 10× concurrency, 90-minute plateau, 30-minute ramp down. Used by PRR-432 chaos simulation.

### Fixtures + data

- Student users: pre-seeded in a dedicated `load-test` tenant; 10,000 accounts with realistic plan/target distribution. Seeded via the existing seed pipeline (not a bespoke test-only path).
- Item bank: a load-test-specific item bank of 500 items that exercises every question type (MC, step-solver, free-form, chem, language) without colliding with the production item bank. Loaded via the same `Cena.Admin.Api` pipeline real authors use — not a back-door insert.
- Authentication: k6 authenticates against the Firebase emulator in Dev and against real Firebase with test-only service-account credentials in Staging. Tokens are cached across VU iterations per k6's built-in token cache, not re-fetched per request.

### Integration with observability

- k6 outputs metrics via the Prometheus Remote Write protocol (`xk6-output-prometheus-remote`) to the same Prometheus instance the cluster exports to. Shared time axis → correlatable traces.
- Grafana dashboard at `infra/observability/dashboards/cena-load-test.json` (new) cross-plots harness-side latency against cluster-side `actor_activations_per_second`, `nats_messages_in`, `marten_events_written_per_second`, `redis_hit_rate`.
- Runs tagged with `{tenant, commit_sha, test_suite, replica_count}` so you can compare "10 replicas at SHA abc vs 10 replicas at SHA def."

### Developer + CI workflows

- Dev: `./scripts/load-test-local.sh student-session-ramp` — runs against a PRR-430 k3d cluster at 3 replicas by default.
- CI: on PRs touching `src/actors/**` or `src/api/**`, run `student-session-baseline.js` + a 2-minute `student-session-ramp.js` against a disposable k3d cluster; fail PR if p95 latency regresses >20% vs baseline stored in `tests/load/baselines/`.
- Nightly: full suite runs against staging, posts the report to the team Slack.

### No stubs, real components

- No mocks of Cena-owned services. Actor-host is real. Postgres is real. Redis is real. NATS is real. k3d is real.
- Outbound vendor calls (Anthropic, Firebase, Stripe if applicable) are mocked **at their existing seams** (`CenaLlmGateway`, `FirebaseAuthService`, existing Stripe test harness). Reason: we're testing the cluster's behavior under load, not the vendors'; vendor latency is a separately-researched variable (see PRR-233 finops observability).

## Files

- `tests/load/` (new directory)
  - `student-session-baseline.js`
  - `student-session-ramp.js`
  - `admin-api-smoke.js`
  - `photo-upload-burst.js`
  - `nats-publish-load.js`
  - `bagrut-spike-shape.js`
  - `lib/` — shared helpers (auth, session-state, item-fetch)
  - `fixtures/load-test-tenant.sql` or equivalent seed
  - `baselines/` — stored p50/p95 per commit for regression detection
- `scripts/load-test-local.sh` (new)
- `scripts/load-test-ci.sh` (new — CI entrypoint)
- `.github/workflows/load-test.yml` (new — nightly + PR gates)
- `infra/observability/dashboards/cena-load-test.json` (new)
- `docs/ops/runbooks/load-test-execution.md` (new)
- Dockerfile for a k6-with-extensions image at `infra/load-test/Dockerfile`

## Definition of Done

- All 6 suites run cleanly against a 3-replica PRR-430 k3d cluster and produce metrics in Grafana.
- Baselines stored for `student-session-baseline` + `student-session-ramp` at every replica count 1, 3, 5, 10.
- Per-replica throughput ceiling documented: sessions/second, events-written/second, p95 latency at saturation.
- CI gate active on `src/actors/**` + `src/api/**` PRs; demonstrated by intentionally regressing a line and seeing the gate fail.
- No new test-only code paths in production source (verified by architecture test or manual audit).
- Full `Cena.Actors.sln` builds cleanly.
- Runbook covers how to run the harness + interpret its output + known flakiness sources.

## Non-negotiable references

- Memory "No stubs — production grade".
- Memory "Senior Architect mindset" — measuring, not asserting.
- Memory "Honest not complimentary" — p50/p95/p99 with runs + CIs.
- Memory "Full sln build gate".
- [PRR-053](TASK-PRR-053-exam-day-capacity-plan-bagrut-traffic-forecast.md), [PRR-231](TASK-PRR-231-amend-capacity-plan-sat-pet.md) — claims under test.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + sample Grafana dashboard screenshot URL + baseline file SHAs>"`

## Related

- EPIC-PRR-K.
- PRR-430 (the cluster it runs against).
- PRR-432 (chaos simulation consumes this harness).
