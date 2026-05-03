// =============================================================================
// Cena Platform — E2E Student Journey Load Test (Phase 4.4, RDY-OCR-ROADMAP)
//
// Exercises the four highest-traffic student endpoints plus the flow-state
// surfaces shipped in RDY-034 (slices 1–3). Fails the run when p95 latency
// or failure rate violate the SLOs encoded at the top of this file.
//
// Scenarios (run in parallel):
//   1. session_poll              — sustained GET /api/sessions/{id}
//                                  (includes FlowState field per slice 2)
//   2. session_answer            — POST /api/sessions/{id}/answer
//                                  (actor emits [FLOW_STATE_TRANSITION])
//   3. photo_upload              — POST /api/photos/upload
//                                  (OCR cascade fallback path)
//   4. flow_state_assess_direct  — POST /api/sessions/flow-state/assess
//                                  (the standalone endpoint from slice 1)
//
// SLOs (all sustained-scenario p95s):
//   session_poll p95             <  500 ms  (read path, projection)
//   session_answer p95           < 1500 ms  (BKT + actor + event append)
//   flow_state_assess p95        <  250 ms  (pure in-memory compute)
//   photo_upload p95             < 3500 ms  (cascade + cloud fallbacks)
//   overall http_req_failed rate <  1%
//
// Usage:
//   # Against a local docker-compose stack:
//   k6 run -e CENA_STUDENT_URL=http://localhost:5081 \
//          -e CENA_STUDENT_TOKEN=$(cat .student-token) \
//          tests/load/e2e-student-journey.js
//
//   # Nightly CI invokes this from .github/workflows/e2e-load-nightly.yml
//   # against the ephemeral staging kind cluster. When secrets are absent
//   # the workflow still performs `k6 archive` to syntax-check the script.
//
// Secrets required (CI): CENA_STUDENT_URL, CENA_STUDENT_TOKEN, CENA_SESSION_ID
// =============================================================================

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';

const BASE_URL = __ENV.CENA_STUDENT_URL || 'http://localhost:5081';
const TOKEN = __ENV.CENA_STUDENT_TOKEN || 'dev-mock-token';
const SESSION_ID = __ENV.CENA_SESSION_ID || 'load-test-session-1';

const AUTH_HEADERS = {
  'Content-Type': 'application/json',
  Authorization: `Bearer ${TOKEN}`,
};

// Custom metrics so the threshold block can target each scenario cleanly.
const sessionPollDuration   = new Trend('cena_session_poll_ms', true);
const sessionAnswerDuration = new Trend('cena_session_answer_ms', true);
const flowStateDuration     = new Trend('cena_flow_state_ms', true);
const photoUploadDuration   = new Trend('cena_photo_upload_ms', true);
const flowStatePresence     = new Rate('cena_flow_state_present');   // slice 2 invariant
const flowStateCamelCase    = new Rate('cena_flow_state_camel_case'); // wire-format regression

export const options = {
  scenarios: {
    session_poll: {
      executor: 'constant-vus',
      exec: 'pollSession',
      vus: 50,
      duration: '2m',
      tags: { scenario: 'session_poll' },
    },
    session_answer: {
      executor: 'constant-vus',
      exec: 'submitAnswer',
      vus: 20,
      duration: '2m',
      tags: { scenario: 'session_answer' },
      startTime: '5s',
    },
    flow_state_assess_direct: {
      executor: 'constant-vus',
      exec: 'assessFlowState',
      vus: 30,
      duration: '2m',
      tags: { scenario: 'flow_state_assess' },
      startTime: '10s',
    },
    photo_upload: {
      executor: 'constant-vus',
      exec: 'uploadPhoto',
      vus: 5,
      duration: '2m',
      tags: { scenario: 'photo_upload' },
      startTime: '15s',
    },
  },
  thresholds: {
    'http_req_failed':                                             ['rate<0.01'],
    'cena_session_poll_ms':                                        ['p(95)<500'],
    'cena_session_answer_ms':                                      ['p(95)<1500'],
    'cena_flow_state_ms':                                          ['p(95)<250'],
    'cena_photo_upload_ms':                                        ['p(95)<3500'],
    'cena_flow_state_present':                                     ['rate>0.99'],
    'cena_flow_state_camel_case':                                  ['rate>0.99'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

// ─── Scenario: session_poll ──────────────────────────────────────────────
// Validates the slice-2 invariant: every GET /api/sessions/{id} response
// carries a FlowState field with the canonical camelCase state name.
export function pollSession() {
  const res = http.get(`${BASE_URL}/api/sessions/${SESSION_ID}`, {
    headers: AUTH_HEADERS,
    tags: { endpoint: 'session_detail' },
  });

  sessionPollDuration.add(res.timings.duration);

  const ok = check(res, {
    'status 200':             (r) => r.status === 200,
    'content-type json':      (r) => (r.headers['Content-Type'] || '').includes('application/json'),
  });

  if (ok && res.status === 200) {
    let body;
    try { body = res.json(); } catch (_e) { body = null; }
    if (body && body.flowState) {
      flowStatePresence.add(1);
      // camelCase regression: state must be one of the known tokens.
      const known = ['warming', 'approaching', 'inFlow', 'disrupted', 'fatigued'];
      flowStateCamelCase.add(known.includes(body.flowState.state) ? 1 : 0);
    } else {
      flowStatePresence.add(0);
    }
  }

  sleep(1 + Math.random() * 2);   // 1–3s think time
}

// ─── Scenario: session_answer ────────────────────────────────────────────
// Exercises the actor-side [FLOW_STATE_TRANSITION] emission (slice 3).
export function submitAnswer() {
  const payload = JSON.stringify({
    questionId:       `q-${Math.floor(Math.random() * 1000)}`,
    conceptId:        'calculus.derivatives.chain_rule',
    answer:           `x = ${Math.floor(Math.random() * 10)}`,
    responseTimeMs:   1000 + Math.floor(Math.random() * 3000),
    isCorrect:        Math.random() > 0.3,
    hintCountUsed:    Math.random() > 0.7 ? 1 : 0,
    backspaceCount:   Math.floor(Math.random() * 5),
    answerChangeCount: Math.floor(Math.random() * 3),
  });

  const res = http.post(`${BASE_URL}/api/sessions/${SESSION_ID}/answer`, payload, {
    headers: AUTH_HEADERS,
    tags: { endpoint: 'session_answer' },
  });

  sessionAnswerDuration.add(res.timings.duration);

  check(res, {
    'status 2xx':       (r) => r.status >= 200 && r.status < 300,
  });

  sleep(2 + Math.random() * 3);
}

// ─── Scenario: flow_state_assess_direct ──────────────────────────────────
// Slice-1 endpoint — pure in-memory compute, should be the fastest.
export function assessFlowState() {
  const payload = JSON.stringify({
    fatigueLevel:           Math.random() * 0.8,
    accuracyTrend:          Math.random() * 2 - 1,
    consecutiveCorrect:     Math.floor(Math.random() * 6),
    sessionDurationMinutes: Math.random() * 50,
    currentDifficulty:      Math.floor(Math.random() * 10) + 1,
  });

  const res = http.post(`${BASE_URL}/api/sessions/flow-state/assess`, payload, {
    headers: AUTH_HEADERS,
    tags: { endpoint: 'flow_state_assess' },
  });

  flowStateDuration.add(res.timings.duration);

  check(res, {
    'status 200':       (r) => r.status === 200,
    'has state':        (r) => {
      try { return typeof r.json('state') === 'string'; } catch (_e) { return false; }
    },
    'has action':       (r) => {
      try { return typeof r.json('recommendedAction') === 'string'; } catch (_e) { return false; }
    },
  });

  sleep(0.5);
}

// ─── Scenario: photo_upload ──────────────────────────────────────────────
// Exercises the cascade fallback path. Small synthetic PNG payload so we
// stay on the cheap local OCR layers and don't pull cloud creds.
export function uploadPhoto() {
  // 1x1 PNG (smallest valid payload that passes magic-byte check)
  const pngBytes = new Uint8Array([
    0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
    0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
    0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
    0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
    0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
    0x54, 0x08, 0x99, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
    0x00, 0x00, 0x03, 0x00, 0x01, 0x5B, 0xEC, 0x6B,
    0xC6, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
    0x44, 0xAE, 0x42, 0x60, 0x82,
  ]);

  const res = http.post(
    `${BASE_URL}/api/photos/upload`,
    { photo: http.file(pngBytes.buffer, 'test.png', 'image/png') },
    {
      headers: { Authorization: AUTH_HEADERS.Authorization },
      tags: { endpoint: 'photo_upload' },
    });

  photoUploadDuration.add(res.timings.duration);

  check(res, {
    // The cascade may return 422 low_confidence for a 1x1 png — that is
    // still a successful round-trip from a load perspective. 5xx is a fail.
    'status < 500':       (r) => r.status < 500,
  });

  sleep(5 + Math.random() * 5);   // photo uploads are low-frequency
}
