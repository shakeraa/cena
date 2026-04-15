// =============================================================================
// Cena Platform — CAS Gate Load Test (RDY-051, RDY-036 §10)
//
// k6 script: 100 concurrent authoring requests against the Admin API's
// question-create endpoint, asserts p95 < 3s and failure rate < 1% while
// the CAS gate is in Enforce mode.
//
// Usage:
//   # Local docker-compose stack running Admin API + SymPy sidecar:
//   k6 run -e CENA_ADMIN_URL=http://localhost:5080 \
//          -e CENA_ADMIN_TOKEN=$(cat .admin-token) \
//          tests/load/cas-gate-load.js
//
//   # Nightly CI:
//   .github/workflows/backend-nightly.yml invokes this script against the
//   ephemeral staging kind cluster.
//
// This is the baseline scaffold; full tuning + per-subject mix is RDY-051.
// =============================================================================

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';

export const options = {
  scenarios: {
    sustained_authoring: {
      executor: 'constant-vus',
      vus: 100,
      duration: '2m',
    },
  },
  thresholds: {
    // RDY-036 §10 contract
    'http_req_duration{scenario:sustained_authoring}': ['p(95)<3000'],
    'checks': ['rate>0.99'],
    'http_req_failed': ['rate<0.01'],
  },
};

const ADMIN_URL = __ENV.CENA_ADMIN_URL || 'http://localhost:5080';
const TOKEN = __ENV.CENA_ADMIN_TOKEN || '';

const casLatency = new Trend('cena_cas_gate_latency_ms');
const casRejectRate = new Rate('cena_cas_gate_reject_rate');

// Minimal authored-question fixture. Each iteration perturbs the correct
// answer with a unique counter so the CAS idempotency cache doesn't
// short-circuit the entire load test.
function buildQuestion(iter) {
  return JSON.stringify({
    sourceType: 'authored',
    subject: 'math',
    topic: 'algebra',
    grade: '5 Units',
    bloomsLevel: 3,
    difficulty: 0.4,
    language: 'en',
    stem: `Solve 2x + ${iter % 97} = ${(iter % 97) + 4}`,
    stemHtml: null,
    options: [
      { label: 'A', text: '2', isCorrect: true },
      { label: 'B', text: '3', isCorrect: false },
      { label: 'C', text: '4', isCorrect: false },
      { label: 'D', text: '5', isCorrect: false },
    ],
    conceptIds: [],
  });
}

export default function () {
  const iter = __ITER;
  const res = http.post(
    `${ADMIN_URL}/api/admin/questions`,
    buildQuestion(iter),
    {
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${TOKEN}`,
      },
      timeout: '10s',
    },
  );

  casLatency.add(res.timings.duration);
  casRejectRate.add(res.status === 400 || res.status === 409);

  check(res, {
    'status accepted or rejected cleanly (not 5xx)': (r) => r.status < 500,
    'latency under 3s': (r) => r.timings.duration < 3000,
  });

  sleep(0.2);
}
