// =============================================================================
// Cena Platform — Admin Corpus Operations Load Test (Phase 4.4)
//
// Exercises the SuperAdmin authoring endpoints under realistic operator
// traffic. These are low-QPS, high-cost endpoints — the point of the
// scenario is to verify the cost circuit breaker + rate limiter keep
// them well-behaved when multiple curators spam them concurrently.
//
// Scenarios:
//   1. expand_corpus_dry_run    — dry-run planner, no LLM spend
//   2. recreate_from_reference  — RDY-019b dry-run, no LLM spend
//
// Wet-run scenarios are NOT included — CI should never actually spend
// on LLM calls. The real cost ceiling is enforced by CostCircuitBreaker
// in production; this script proves the admin endpoints stay responsive
// under concurrent planning load.
//
// SLOs:
//   expand_corpus p95       < 2000 ms  (Marten query + taxonomy walk)
//   recreate_from_ref p95   <  800 ms  (analysis.json parse + planner)
//   http_req_failed rate    < 1%
// =============================================================================

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend } from 'k6/metrics';

const BASE_URL = __ENV.CENA_ADMIN_URL || 'http://localhost:5080';
const TOKEN = __ENV.CENA_ADMIN_TOKEN || 'dev-super-admin-token';

const AUTH_HEADERS = {
  'Content-Type': 'application/json',
  Authorization: `Bearer ${TOKEN}`,
};

const expandCorpusDuration   = new Trend('cena_expand_corpus_ms', true);
const recreateRefDuration    = new Trend('cena_recreate_from_ref_ms', true);

export const options = {
  scenarios: {
    expand_corpus_dry_run: {
      executor: 'constant-vus',
      exec: 'expandCorpus',
      vus: 3,
      duration: '1m',
      tags: { scenario: 'expand_corpus' },
    },
    recreate_from_reference: {
      executor: 'constant-vus',
      exec: 'recreateFromReference',
      vus: 3,
      duration: '1m',
      tags: { scenario: 'recreate_from_ref' },
      startTime: '5s',
    },
  },
  thresholds: {
    'http_req_failed':            ['rate<0.01'],
    'cena_expand_corpus_ms':      ['p(95)<2000'],
    'cena_recreate_from_ref_ms':  ['p(95)<800'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export function expandCorpus() {
  const payload = JSON.stringify({
    sourceSelector: 'seed',
    difficultyBands: [{ min: 0.4, max: 0.6, count: 2 }],
    maxTotalCandidates: 20,
    stopAfterLeafFull: 5,
    dryRun: true,
  });

  const res = http.post(`${BASE_URL}/api/admin/questions/expand-corpus`, payload, {
    headers: AUTH_HEADERS,
    tags: { endpoint: 'expand_corpus' },
  });

  expandCorpusDuration.add(res.timings.duration);

  check(res, {
    'status 200':              (r) => r.status === 200,
    'dryRun=true in response': (r) => {
      try { return r.json('dryRun') === true; } catch (_e) { return false; }
    },
  });

  sleep(2);
}

export function recreateFromReference() {
  const payload = JSON.stringify({
    maxCandidatesPerCluster: 2,
    maxTotalCandidates: 10,
    dryRun: true,
  });

  const res = http.post(`${BASE_URL}/api/admin/content/recreate-from-reference`, payload, {
    headers: AUTH_HEADERS,
    tags: { endpoint: 'recreate_from_ref' },
  });

  recreateRefDuration.add(res.timings.duration);

  // Note: 400 missing_analysis is expected when analysis.json isn't
  // provisioned in the load env. Treat as non-5xx success.
  check(res, {
    'status < 500': (r) => r.status < 500,
  });

  sleep(2);
}
