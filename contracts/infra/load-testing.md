# Cena Platform — Load Testing Specification Contract

**Layer:** Infrastructure / QA | **Tool:** k6 (primary), Locust (LLM-specific scenarios)
**Status:** BLOCKER — no load testing; cannot validate Bagrut night capacity

---

## 1. Load Testing Tools

| Tool | Language | Use Case |
|------|----------|----------|
| k6 (Grafana) | JavaScript | HTTP/gRPC/WebSocket load tests, primary tool |
| Locust | Python | LLM ACL-specific scenarios (Python ecosystem alignment) |

### Infrastructure

| Component | Spec |
|-----------|------|
| Load generators | 3x c5.4xlarge EC2 instances (16 vCPU, 32 GB RAM) |
| Results backend | InfluxDB + Grafana dashboards |
| Test data | Pre-seeded PostgreSQL with 10K student profiles, 500 concepts |
| LLM responses | Mocked via WireMock (deterministic latency: 200ms P50, 800ms P99) |

---

## 2. Test Scenarios

### Scenario 1: Normal Load (Weekday Evening)

| Parameter | Value |
|-----------|-------|
| Name | `normal_weekday` |
| Concurrent users | 1,000 |
| Ramp-up | 0 -> 1,000 over 5 minutes |
| Steady state | 10 minutes |
| Ramp-down | 1,000 -> 0 over 2 minutes |
| User behavior | 1 session per user: 10 AttemptConcept commands, 2 GetReviewSchedule queries |

### Scenario 2: Peak Load (Bagrut Sunday Night)

| Parameter | Value |
|-----------|-------|
| Name | `bagrut_peak` |
| Concurrent users | 5,000 |
| Ramp-up | 0 -> 5,000 over 10 minutes |
| Steady state | 30 minutes |
| Ramp-down | 5,000 -> 0 over 5 minutes |
| User behavior | 1 session per user: 15 AttemptConcept, 5 GetReviewSchedule, 2 MethodologySwitch |
| Spike | At minute 15: burst from 5,000 to 7,000 over 30 seconds (simulates exam schedule release) |

### Scenario 3: Stress Test (Breaking Point)

| Parameter | Value |
|-----------|-------|
| Name | `stress_breaking_point` |
| Concurrent users | 10,000 (stepped) |
| Ramp-up | 0 -> 2K -> 5K -> 8K -> 10K (2 min per step) |
| Steady state | 5 minutes at each step |
| Goal | Find breaking point: which component fails first, at what concurrency |
| Accept criteria | Graceful degradation (error rate rises, no crashes or data loss) |

### Scenario 4: Soak Test (Overnight Stability)

| Parameter | Value |
|-----------|-------|
| Name | `soak_overnight` |
| Concurrent users | 500 (constant) |
| Duration | 8 hours |
| Goal | Detect memory leaks, connection pool exhaustion, actor state drift |
| Accept criteria | No increase in P99 latency or error rate over 8 hours |

---

## 3. Performance Targets (SLA)

### API Latency

| Endpoint | P50 | P95 | P99 | Max |
|----------|-----|-----|-----|-----|
| `POST /api/session/attempt` (AttemptConcept) | 50ms | 200ms | 500ms | 2s |
| `GET /api/session/review-schedule` | 30ms | 100ms | 250ms | 1s |
| `POST /api/session/start` (StartSession) | 80ms | 300ms | 700ms | 3s |
| `WS /hub` (SignalR connect) | 100ms | 300ms | 500ms | 2s |
| `WS message` (SignalR round-trip) | 20ms | 80ms | 200ms | 500ms |

### Error Rates

| Scenario | Max Error Rate | Max 5xx Rate |
|----------|---------------|--------------|
| Normal (1K) | 0.1% | 0.01% |
| Peak (5K) | 0.5% | 0.05% |
| Stress (10K) | 2.0% | 0.5% |

### Throughput

| Metric | Target |
|--------|--------|
| Requests per second (normal) | 2,000 RPS sustained |
| Requests per second (peak) | 10,000 RPS sustained |
| WebSocket messages per second | 15,000 msg/sec |

---

## 4. Component-Specific Tests

### 4.1 Actor Activation (Proto.Actor)

| Test | Spec |
|------|------|
| Name | `actor_activation_burst` |
| Scenario | 5,000 unique StudentActor activations in 60 seconds |
| Measurement | Activation latency (time from first message to actor ready) |
| Target | P95 activation < 50ms, P99 < 200ms |
| Verify | No actor duplication, no lost messages during activation storm |
| Cluster | 3-node Proto.Actor cluster with consistent hashing |

### 4.2 LLM ACL (Mocked Responses)

| Test | Spec |
|------|------|
| Name | `llm_throughput` |
| Scenario | 5,000 SocraticQuestion requests in 60 seconds |
| LLM mock | WireMock returning 200ms fixed latency response |
| Target | P95 < 400ms end-to-end (including PII stripping, routing, parsing) |
| Verify | Token budget enforcement works under load, circuit breaker triggers correctly |
| Fallback | Inject 10% mock failures to test Kimi -> Sonnet fallback chain |

### 4.3 Offline Sync

| Test | Spec |
|------|------|
| Name | `offline_sync_burst` |
| Scenario | 1,000 concurrent sync requests, each with 50 queued events |
| Total events | 50,000 events processed in sync batch |
| Target | Full sync completes in < 30 seconds for 50 events per student |
| Verify | Event ordering preserved, no duplicate events, BKT state consistent |
| Conflict | 5% of syncs include a conflict (server has newer state) |

### 4.4 PostgreSQL (Marten Event Store)

| Test | Spec |
|------|------|
| Name | `postgres_connection_pool` |
| Scenario | Ramp connections until pool exhaustion |
| Pool size | 100 connections per node (3 nodes = 300 total) |
| Target | No connection leak after 8-hour soak test |
| Verify | Connection wait time < 50ms at normal load, < 500ms at peak |
| Measure | `pg_stat_activity` connection count, idle transaction count |

### 4.5 NATS (JetStream)

| Test | Spec |
|------|------|
| Name | `nats_consumer_lag` |
| Scenario | Publish 10,000 events/sec to `cena.learner.events.*` for 10 minutes |
| Target | Consumer lag stays < 100 messages for all consumers |
| Verify | No message loss (ack all, verify sequence numbers) |
| DLQ | Inject 1% poison messages, verify they land in DLQ within 30 seconds |
| Measure | `nats_jetstream_consumer_num_pending`, `nats_jetstream_consumer_num_ack_pending` |

### 4.6 SignalR WebSocket

| Test | Spec |
|------|------|
| Name | `signalr_concurrent_connections` |
| Scenario | 5,000 concurrent WebSocket connections |
| Message rate | Each connection sends 1 msg/sec, receives 2 msg/sec |
| Target | Connection success rate > 99.5%, message delivery rate > 99.9% |
| Verify | Reconnection after server restart (rolling deployment simulation) |

---

## 5. Test Data Seeding

| Entity | Count | Notes |
|--------|-------|-------|
| Student profiles | 10,000 | Distributed across grades 7-12 |
| Concepts | 500 | With prerequisite graph (avg 3 prerequisites each) |
| Mastery maps | 10,000 x 50 | Each student has mastery for ~50 concepts |
| Event history | 500,000 events | 50 events per student average |
| BKT parameters | 500 | One per concept |

### Seeding Script

```bash
# Seed test data before load test run
k6 run scripts/seed-test-data.js --env TARGET=staging

# Verify seed data
k6 run scripts/verify-seed-data.js --env TARGET=staging
```

---

## 6. Run Schedule

| Trigger | Scenario | Environment |
|---------|----------|-------------|
| Every staging deploy | `normal_weekday` (smoke) | Staging |
| Weekly (Friday night) | `bagrut_peak` | Staging |
| Before prod promotion | `bagrut_peak` + `soak_overnight` | Staging |
| Monthly | `stress_breaking_point` | Dedicated load-test env |
| Before Bagrut season | Full suite (all scenarios) | Staging (production-sized) |

### CI/CD Gate

Load test results are a **gate for staging -> production promotion**:

1. Run `normal_weekday` scenario.
2. If P95 > 200ms on AttemptConcept OR error rate > 0.1%: **FAIL pipeline**.
3. Run `bagrut_peak` scenario.
4. If P95 > 500ms OR error rate > 0.5%: **FAIL pipeline** (warn, manual override allowed).
5. Results posted to Slack `#load-test-results` channel.

---

## 7. Reporting

### Per-Run Report Contents

| Section | Contents |
|---------|----------|
| Summary | Pass/fail, scenario name, duration, total requests |
| Latency | P50, P75, P90, P95, P99, max — per endpoint |
| Error rates | Total errors, 4xx breakdown, 5xx breakdown |
| Throughput | RPS over time (time series chart) |
| Resource utilization | CPU, memory, network — per service (from Prometheus) |
| Database | Connection pool usage, query latency, lock contention |
| NATS | Consumer lag, message throughput, DLQ count |
| Actor cluster | Active actors, activation rate, message queue depth |
| Cost projection | Estimated monthly cost at tested load (compute + LLM + NATS) |

### Report Storage

- Reports stored in S3: `s3://cena-load-tests/{date}/{scenario}/report.html`
- Historical trend dashboard in Grafana: latency percentiles over last 30 runs.
- Regression alerts: if P95 increases > 20% from previous run, alert in Slack.

---

## 8. Cost Projection Model

| Component | Normal (1K) | Peak (5K) | Stress (10K) |
|-----------|-------------|-----------|---------------|
| ECS/EKS compute | $X/month | $Y/month | $Z/month |
| RDS PostgreSQL | $X/month | $Y/month | $Z/month |
| NATS (Synadia) | $X/month | $Y/month | $Z/month |
| LLM API costs | $X/month | $Y/month | $Z/month |
| Data transfer | $X/month | $Y/month | $Z/month |

Cost values are populated after initial load test run with real metrics.
Formula: `monthly_cost = (cost_per_request * daily_requests * 30) + fixed_infra_cost`

---

## 9. Failure Injection (Chaos Engineering)

Integrated into peak and stress scenarios:

| Injection | Frequency | Purpose |
|-----------|-----------|---------|
| Kill 1 actor cluster node | Once during peak | Test Proto.Actor rebalancing |
| PostgreSQL 5-second latency spike | Every 10 minutes | Test circuit breaker + retry |
| NATS disconnect (30 seconds) | Once during peak | Test in-memory buffer + reconnect |
| LLM provider 100% failure | 2 minutes during peak | Test full fallback chain |
| Redis unavailable | Once during soak | Test cache bypass to database |
