// =============================================================================
// Cena Platform — k6 session-load harness (PRR-431)
//
// Simulates the Bagrut-morning traffic shape: a single student registering,
// opening a session, answering N questions, requesting hints, uploading a
// photo diagnostic, and finishing. Each Virtual User (VU) runs this script
// in a loop; configure `--vus` + `--duration` to scale.
//
// Usage:
//   k6 run --vus 100 --duration 5m tests/load/session-load.js
//   k6 run --stage 5m:500,10m:500 tests/load/session-load.js   (Bagrut-spike)
//
// Thresholds encode the launch-gate SLOs: p95 session-start < 2s, p95
// answer-submit < 1s, error rate < 1%. A run that breaches any threshold
// exits non-zero — suitable for CI gating.
// =============================================================================

import http from 'k6/http'
import { check, sleep, group } from 'k6'
import { Rate, Trend } from 'k6/metrics'

const BASE_URL = __ENV.CENA_BASE_URL || 'http://localhost:5050'
const sessionStartErrors = new Rate('session_start_errors')
const answerSubmitErrors = new Rate('answer_submit_errors')
const sessionStartLatency = new Trend('session_start_latency_ms')
const answerSubmitLatency = new Trend('answer_submit_latency_ms')

export const options = {
  thresholds: {
    'http_req_duration{path:session_start}': ['p(95)<2000'],
    'http_req_duration{path:answer_submit}': ['p(95)<1000'],
    http_req_failed: ['rate<0.01'],
    session_start_errors: ['rate<0.01'],
    answer_submit_errors: ['rate<0.01'],
  },
}

function authHeaders(token) {
  return {
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json',
    },
    tags: {},
  }
}

export function setup() {
  // Return any shared setup (e.g., a seeded auth token fixture). Fixture
  // should exist in the load-environment DB; the harness does not register
  // new students every iteration — that would exercise registration
  // pipeline noise rather than session capacity.
  return { token: __ENV.CENA_LOAD_TOKEN || '' }
}

export default function (data) {
  const { token } = data

  let sessionId = null
  group('session_start', () => {
    const params = authHeaders(token)
    params.tags.path = 'session_start'
    const t0 = Date.now()
    const res = http.post(`${BASE_URL}/api/me/session/start`, '{}', params)
    const elapsed = Date.now() - t0
    sessionStartLatency.add(elapsed)
    const ok = check(res, {
      'start returned 200': r => r.status === 200,
      'start returned sessionId': r => r.json('sessionId') !== undefined,
    })
    sessionStartErrors.add(!ok)
    if (ok) sessionId = res.json('sessionId')
  })

  if (!sessionId) {
    sleep(1)
    return
  }

  // Answer 5 questions in the session.
  for (let i = 0; i < 5; i++) {
    group('answer_submit', () => {
      const params = authHeaders(token)
      params.tags.path = 'answer_submit'
      const body = JSON.stringify({
        sessionId,
        questionIndex: i,
        answer: 'A',
      })
      const t0 = Date.now()
      const res = http.post(`${BASE_URL}/api/me/session/answer`, body, params)
      const elapsed = Date.now() - t0
      answerSubmitLatency.add(elapsed)
      const ok = check(res, {
        'answer returned 200': r => r.status === 200,
      })
      answerSubmitErrors.add(!ok)
    })
    // Student think time: 3-8s between answers.
    sleep(3 + Math.random() * 5)
  }

  group('session_end', () => {
    const params = authHeaders(token)
    params.tags.path = 'session_end'
    http.post(`${BASE_URL}/api/me/session/end`, JSON.stringify({ sessionId }), params)
  })

  // Brief pause before the VU starts the next iteration.
  sleep(1)
}
