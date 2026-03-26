# INF-016: Load Testing Infrastructure

**Priority:** P0 — BLOCKER (no load testing = no confidence in Bagrut night capacity)
**Blocked by:** ACT-002 (StudentActor), INF-001 (NATS cluster), DATA-001 (PostgreSQL)
**Blocks:** Production go-live approval
**Estimated effort:** 5 days
**Contract:** `contracts/backend/nats-subjects.md` (stream configs, consumer groups), `contracts/backend/actor-contracts.cs` (message types), `contracts/backend/domain-services.cs` (BKT, HLR, stagnation)

---

## Context
Cena targets 5,000 concurrent students during Bagrut exam preparation nights (January, June). Each student drives a Proto.Actor virtual actor, generates 2-5 NATS events per question attempt, and triggers LLM calls via gRPC. Without load testing, we have no confidence that the actor cluster scales, NATS JetStream keeps up, PostgreSQL/Marten handles event append throughput, and the LLM ACL stays within budget. The load testing infrastructure must be permanent (CI-integrated), not a one-off script.

## Subtasks

### INF-016.1: k6/Locust Test Harness Setup
**Files:**
- `tests/load/k6/config.js` — k6 base configuration
- `tests/load/k6/helpers/signalr-client.js` — k6 SignalR WebSocket helper
- `tests/load/k6/helpers/auth.js` — Firebase token generation for load users
- `tests/load/locust/locustfile.py` — Locust file for gRPC LLM ACL testing
- `tests/load/locust/grpc_client.py` — gRPC client for Locust
- `tests/load/docker-compose.load.yml` — Docker Compose for load test infrastructure
- `tests/load/Makefile` — orchestration commands

**Acceptance:**
- [ ] k6 handles SignalR WebSocket protocol (binary framing, hub method invocation)
- [ ] k6 SignalR client supports: `StartSession`, `SubmitAnswer`, `EndSession`, `RequestHint` commands
- [ ] k6 SignalR client validates: `SessionStarted`, `QuestionPresented`, `AnswerEvaluated`, `XpAwarded` events
- [ ] Locust handles gRPC mTLS to LLM ACL (SocraticTutorService, AnswerEvaluationService)
- [ ] Test user pool: 10,000 pre-created Firebase users (`loadtest-0001@cena.app` through `loadtest-10000@cena.app`)
- [ ] Test data: 50 concepts with BKT parameters, 200 pre-generated questions per concept
- [ ] Grafana dashboards: k6 metrics → InfluxDB → Grafana (included in docker-compose)
- [ ] `make load-test-normal` / `make load-test-peak` / `make load-test-stress` entry points

**Test:**
```javascript
// tests/load/k6/smoke-test.js — validates harness works before real load
import { check } from 'k6';
import { connectSignalR, startSession, submitAnswer, endSession } from './helpers/signalr-client.js';
import { getFirebaseToken } from './helpers/auth.js';

export const options = {
  vus: 1,
  duration: '30s',
  thresholds: {
    'signalr_session_start_duration': ['p(95)<2000'],
    'signalr_answer_submit_duration': ['p(95)<1000'],
    'checks': ['rate==1.0'],
  },
};

export default function () {
  const token = getFirebaseToken(`loadtest-0001@cena.app`);
  const conn = connectSignalR(__ENV.SIGNALR_URL, token);

  const session = startSession(conn, { subjectId: 'math', conceptId: null });
  check(session, {
    'session started': (s) => s.sessionId !== null,
    'methodology assigned': (s) => s.methodology !== null,
  });

  // Attempt 3 questions
  for (let i = 0; i < 3; i++) {
    const answer = submitAnswer(conn, {
      sessionId: session.sessionId,
      questionId: session.currentQuestionId,
      answer: 'x = 5',
      responseTimeMs: 3000 + Math.random() * 2000,
    });
    check(answer, {
      'answer evaluated': (a) => a.correct !== undefined,
      'mastery updated': (a) => a.updatedMastery >= 0,
      'xp awarded': (a) => a.xpEarned >= 0,
    });
  }

  endSession(conn, { sessionId: session.sessionId, reason: 'completed' });
  conn.close();
}
```

---

### INF-016.2: Normal, Peak, and Stress Scenarios
**Files:**
- `tests/load/k6/scenarios/normal.js` — weekday evening (500 concurrent)
- `tests/load/k6/scenarios/peak.js` — Bagrut prep night (2,000 concurrent)
- `tests/load/k6/scenarios/stress.js` — breaking point discovery (5,000+ concurrent)
- `tests/load/k6/scenarios/soak.js` — 4-hour sustained load (1,000 concurrent)

**Acceptance:**
- [ ] **Normal** (500 VUs): ramp 0→500 over 2min, hold 10min, ramp down 2min
  - P95 response time < 200ms for `SubmitAnswer`
  - 0% error rate
  - NATS stream lag < 100 messages
- [ ] **Peak** (2,000 VUs): ramp 0→2000 over 5min, hold 15min, ramp down 3min
  - P95 response time < 500ms for `SubmitAnswer`
  - Error rate < 0.1%
  - NATS stream lag < 500 messages
  - Actor cluster stable (no split-brain, no OOM kills)
- [ ] **Stress** (5,000 VUs): ramp 0→5000 over 10min, hold 10min, ramp down 5min
  - P95 response time < 2000ms for `SubmitAnswer`
  - Error rate < 1%
  - System recovers after ramp-down (no lingering failures)
  - Identify breaking point (VU count where error rate crosses 5%)
- [ ] **Soak** (1,000 VUs for 4 hours):
  - No memory leaks (actor memory stable ±10%)
  - No connection pool exhaustion (PostgreSQL, NATS, Redis)
  - Marten event store latency stable (P99 < 50ms at end of soak)
- [ ] Each scenario emits structured results JSON for CI comparison

**Test (scenario thresholds):**
```javascript
// tests/load/k6/scenarios/normal.js
export const options = {
  scenarios: {
    normal_evening: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '2m', target: 500 },
        { duration: '10m', target: 500 },
        { duration: '2m', target: 0 },
      ],
    },
  },
  thresholds: {
    'http_req_duration{type:SubmitAnswer}': ['p(95)<200'],
    'http_req_failed': ['rate<0.001'],                       // 0% error
    'signalr_session_start_duration': ['p(95)<1000'],
    'signalr_answer_submit_duration': ['p(95)<200'],
    'nats_stream_lag': ['max<100'],
    'actor_activation_ms': ['p(95)<500'],
    'marten_append_ms': ['p(95)<50'],
    'iterations': ['count>5000'],                            // At least 5000 question attempts
  },
};
```

---

### INF-016.3: Bagrut Night Simulation (Realistic Traffic Pattern)
**Files:**
- `tests/load/k6/scenarios/bagrut-night.js` — realistic Bagrut exam prep simulation
- `tests/load/k6/data/bagrut-traffic-profile.json` — hourly traffic curve
- `tests/load/k6/data/concept-distribution.json` — weighted concept selection

**Acceptance:**
- [ ] Traffic curve matches real Israeli student behavior:
  - 16:00-18:00: ramp 0→1000 (after school)
  - 18:00-20:00: hold 1000 (dinner dip at 19:00 → 800)
  - 20:00-22:00: peak 2000-3000 (prime study time)
  - 22:00-00:00: ramp down 3000→500
  - 00:00-02:00: tail 500→100 (night owls)
- [ ] Concept distribution: 60% math (algebra, calculus, geometry), 25% physics, 15% CS
- [ ] Session behavior: avg 20min session, 15 question attempts, 70% correct rate
- [ ] Methodology switches: 10% of students trigger stagnation → methodology switch
- [ ] Offline sync: 5% of students go offline mid-session, reconnect within 2 min, sync 5-10 events
- [ ] LLM budget: verify no student exceeds 25K output tokens/day under load
- [ ] 5,000 unique student actors activated within 60 seconds at peak ramp

**Test:**
```javascript
// tests/load/k6/scenarios/bagrut-night.js
import { SharedArray } from 'k6/data';
import trafficProfile from './data/bagrut-traffic-profile.json';

export const options = {
  scenarios: {
    bagrut_prep_night: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: trafficProfile.stages, // From JSON: [{duration:'2h', target:1000}, ...]
    },
  },
  thresholds: {
    'signalr_answer_submit_duration': ['p(95)<500'],
    'http_req_failed': ['rate<0.005'],              // <0.5% errors
    'nats_stream_lag': ['max<100'],                 // NATS lag < 100 messages
    'actor_activation_count_60s': ['value>=5000'],  // 5K actors in 60s
    'llm_budget_exceeded': ['count==0'],            // No student exceeds budget
    'marten_append_ms': ['p(99)<100'],
    'signalr_session_start_duration': ['p(95)<2000'],
  },
};

export default function () {
  const userId = `loadtest-${String(__VU).padStart(5, '0')}@cena.app`;
  const token = getFirebaseToken(userId);
  const conn = connectSignalR(__ENV.SIGNALR_URL, token);

  const session = startSession(conn, {
    subjectId: weightedConceptSelect(),  // 60% math, 25% physics, 15% cs
    conceptId: null,
  });

  const questionCount = randomInt(10, 25); // avg 15
  for (let i = 0; i < questionCount; i++) {
    const isCorrect = Math.random() < 0.70;
    submitAnswer(conn, {
      sessionId: session.sessionId,
      questionId: session.currentQuestionId,
      answer: isCorrect ? 'correct-answer' : 'wrong-answer',
      responseTimeMs: randomInt(2000, 8000),
    });
    sleep(randomFloat(3, 10)); // Think time between questions
  }

  // 5% chance of offline simulation
  if (Math.random() < 0.05) {
    conn.close();
    sleep(randomFloat(30, 120)); // Offline for 30s-2min
    const newConn = connectSignalR(__ENV.SIGNALR_URL, token);
    syncOfflineEvents(newConn, session.sessionId, randomInt(5, 10));
    endSession(newConn, { sessionId: session.sessionId, reason: 'completed' });
    newConn.close();
  } else {
    endSession(conn, { sessionId: session.sessionId, reason: 'completed' });
    conn.close();
  }
}
```

---

### INF-016.4: CI Integration & Regression Detection
**Files:**
- `.github/workflows/load-test.yml` — GitHub Actions workflow
- `tests/load/scripts/compare-results.py` — regression comparison script
- `tests/load/scripts/generate-report.py` — HTML report generator
- `tests/load/baselines/` — stored baseline results per scenario

**Acceptance:**
- [ ] Load test runs nightly on staging environment via GitHub Actions
- [ ] Normal scenario runs on every PR merge to `main` (blocking)
- [ ] Peak and stress scenarios run weekly (non-blocking, report-only)
- [ ] Bagrut night simulation runs monthly and before every release
- [ ] Regression detection: compare current results against stored baselines
  - P95 latency regression > 20% → CI fails with detailed diff
  - Error rate increase > 0.5pp → CI fails
  - NATS lag increase > 50% → CI warns
- [ ] Results stored as JSON artifacts in S3: `s3://cena-load-tests/{date}/{scenario}.json`
- [ ] HTML report generated with charts: latency distribution, throughput, error rate, NATS lag, actor count
- [ ] Slack notification on regression detection

**Test:**
```python
# tests/load/scripts/test_compare_results.py
def test_regression_detected_on_latency_increase():
    baseline = {"p95_submit_answer_ms": 180, "error_rate": 0.0, "nats_max_lag": 50}
    current = {"p95_submit_answer_ms": 250, "error_rate": 0.0, "nats_max_lag": 55}

    result = compare_results(baseline, current, thresholds={
        "p95_submit_answer_ms": {"max_regression_pct": 20},
        "error_rate": {"max_increase_pp": 0.5},
        "nats_max_lag": {"max_regression_pct": 50},
    })

    assert result.has_regression
    assert "p95_submit_answer_ms" in result.regressions
    assert result.regressions["p95_submit_answer_ms"]["pct_change"] == pytest.approx(38.9, rel=0.1)

def test_no_regression_within_threshold():
    baseline = {"p95_submit_answer_ms": 180, "error_rate": 0.0, "nats_max_lag": 50}
    current = {"p95_submit_answer_ms": 195, "error_rate": 0.001, "nats_max_lag": 60}

    result = compare_results(baseline, current, thresholds={
        "p95_submit_answer_ms": {"max_regression_pct": 20},
        "error_rate": {"max_increase_pp": 0.5},
        "nats_max_lag": {"max_regression_pct": 50},
    })

    assert not result.has_regression

def test_error_rate_regression():
    baseline = {"p95_submit_answer_ms": 180, "error_rate": 0.001, "nats_max_lag": 50}
    current = {"p95_submit_answer_ms": 180, "error_rate": 0.008, "nats_max_lag": 50}

    result = compare_results(baseline, current, thresholds={
        "error_rate": {"max_increase_pp": 0.5},
    })

    assert result.has_regression
    assert "error_rate" in result.regressions
```

---

## Integration Test (full load test pipeline)

```bash
#!/bin/bash
# tests/load/integration-test.sh

set -euo pipefail

# 1. Bring up load test infrastructure
docker compose -f docker-compose.load.yml up -d
echo "Waiting for infrastructure..."
sleep 10

# 2. Run smoke test (1 VU, 30s)
k6 run tests/load/k6/smoke-test.js \
  --env SIGNALR_URL=ws://localhost:5000/hub/learning \
  --out json=results/smoke.json
echo "Smoke test passed"

# 3. Run normal scenario (500 VUs, 14min)
k6 run tests/load/k6/scenarios/normal.js \
  --env SIGNALR_URL=ws://localhost:5000/hub/learning \
  --out json=results/normal.json
echo "Normal scenario completed"

# 4. Compare against baseline
python tests/load/scripts/compare-results.py \
  --baseline tests/load/baselines/normal.json \
  --current results/normal.json \
  --output results/comparison.json

# 5. Generate HTML report
python tests/load/scripts/generate-report.py \
  --input results/normal.json \
  --comparison results/comparison.json \
  --output results/report.html

echo "Load test pipeline complete. Report: results/report.html"
docker compose -f docker-compose.load.yml down
```

## Edge Cases
- k6 SignalR WebSocket frame parsing fails for MessagePack → test with JSON transport only
- Firebase token generation rate-limited → pre-generate tokens in batch, cache for test duration
- Load test itself causes NATS backpressure → separate load generators from SUT network
- Marten snapshot creation under load → verify snapshots don't block event append (async projection)

## Rollback Criteria
- If load test infrastructure costs >$200/month: reduce to on-demand spot instances for CI runs
- If k6 SignalR client is too brittle: switch to Artillery with WebSocket plugin
- If nightly CI run exceeds 30 minutes: reduce normal scenario to 5-minute hold

## Definition of Done
- [ ] All 4 subtasks implemented and validated
- [ ] Smoke test passes in CI on every PR
- [ ] Normal scenario passes with all thresholds met
- [ ] Peak scenario documented with results
- [ ] Bagrut night simulation completed at least once with 5,000 actors
- [ ] P95 `SubmitAnswer` < 200ms at 500 VUs (normal)
- [ ] 5,000 actors activated in 60 seconds (stress)
- [ ] NATS stream lag < 100 at normal load
- [ ] Baseline results stored in S3 for regression comparison
- [ ] Grafana dashboard accessible for real-time monitoring during load tests
- [ ] PR reviewed by architect (you)
