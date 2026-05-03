# Runbook — Exam-day SLO, change-freeze window, and break-glass

**Ticket source**: [prr-016](../../../tasks/pre-release-review/TASK-PRR-016-publish-exam-day-slo-change-freeze-window-in-cd.md)
(extended by [prr-053](../../../tasks/pre-release-review/TASK-PRR-053-exam-day-capacity-plan-bagrut-traffic-forecast.md)
and [prr-231](../../../tasks/pre-release-review/TASK-PRR-231-amend-capacity-plan-sat-pet.md)).
**Primary paging channel**: SRE PagerDuty → `#incident-exam-day`.
**Owners**: cena-sre (primary), cena-platform (config rollbacks), cena-product (student-comms lead).

---

## 1. Context

On an exam-day window (Bagrut, SAT, PET, and the PET-quarterly) student-facing
traffic spikes 5–10× platform baseline, with a long tail of parent + teacher
dashboard reads on the morning of the sitting. This runbook codifies:

- The SLO targets ops will defend during the window (§2).
- The CD change-freeze that blocks production merges during that window (§3).
- The break-glass procedure if the SLO burns faster than budget (§4).
- Recovery criteria and the post-window drill (§5–§6).

Windows are defined in [`ops/release/freeze-windows.yml`](../../../ops/release/freeze-windows.yml),
auto-generated from `contracts/exam-catalog/` sittings by
[`scripts/ops/generate-freeze-windows.mjs`](../../../scripts/ops/generate-freeze-windows.mjs).
Per [prr-231](../../../tasks/pre-release-review/TASK-PRR-231-amend-capacity-plan-sat-pet.md)
this is a compound 7-window calendar, not a 2-spike Bagrut-only calendar.

Compounding reason: the catalog families span three regulators (Ministry of
Education for Bagrut, College Board for SAT, NITE for PET). Each window below
is independently scheduled — they occasionally overlap (e.g. SAT May + Bagrut
Moed A prep ramp), and the capacity plan in
[`docs/ops/capacity/exam-day-capacity-plan.md`](../capacity/exam-day-capacity-plan.md)
models the overlap explicitly.

## 2. Exam-day SLOs

### 2a. Per-window SLO targets

These replace the usual SLO targets from the window `T-48h → T+6h` relative
to each sitting's `canonical_date` (UTC noon anchor; windows extend across
the sitting day in local time).

| SLO                                   | Normal | Exam-day | Basis |
|---------------------------------------|--------|----------|-------|
| Student `/api/session/**` p99 latency | 1200ms | **800ms** | Tighter — the lesson must feel snappy when the cohort is anxious. |
| Student `/api/plan/**` p95 latency    | 600ms  | **400ms** | Plan reads dominate traffic on exam morning. |
| Tutor-reply round trip p95            | 3000ms | **2500ms**| Cache-hit ratio must climb; fail-open path still meets this. |
| Platform error rate                   | 0.5%   | **0.1%**  | Any 5xx during the window is a page. |
| Availability of `/health/ready`       | 99.9%  | **99.95%**| Load-balancer probe must not flap. |
| Admin `/api/admin/**` p95 latency     | 800ms  | **1200ms**| **Loosened** — de-prioritised under load, per §4c freeze lever. |

Error-budget burn rate thresholds (multi-window, following Google SRE §4.4):

| Burn rate | Lookback | Action |
|-----------|----------|--------|
| > 2×      | 1h       | Page SRE primary; start §4 break-glass assessment. |
| > 14×     | 5m       | Page SRE primary + platform secondary immediately; apply fast levers in §4a-c without waiting. |

### 2b. Dashboards

- Grafana `cena-exam-day` — live SLO dashboard. Folder
  `ops/grafana/dashboards/` (added under prr-016 implementation).
- Prometheus alerts — [`ops/alerts/exam-day.yml`](../../../ops/alerts/exam-day.yml)
  (to be added; mirrors §2a thresholds).
- Synthetic probes (prr-039 pattern) cover student-login → first-question
  round-trip on each target family. Per prr-231 these now include SAT + PET
  endpoints.

## 3. CD change-freeze window

### 3a. Scope

During an active freeze window, the following are **blocked** on production:

- Any merge targeting `main` that modifies production runtime code or
  `deploy/helm/cena/values-production.yaml`.
- Manual `workflow_dispatch` of [`cd-deploy.yml`](../../../.github/workflows/cd-deploy.yml)
  against `environment: production`.
- Any catalog change that affects `catalog_version`
  ([`contracts/exam-catalog/catalog-meta.yml`](../../../contracts/exam-catalog/catalog-meta.yml)).

The following are **allowed** during a freeze:

- Staging deploys (`environment: staging`) — freeze only gates production.
- Content-only PRs that do not touch runtime code (copy, docs, fixtures).
- Hotfixes that the on-call SRE explicitly un-gates via the
  `exam-day/break-glass` label on the PR (§3c).

### 3b. How it's enforced

The `.github/workflows/exam-day-freeze.yml` workflow runs on `pull_request`
targeting `main` and on `workflow_dispatch` to `cd-deploy.yml`. It:

1. Loads `ops/release/freeze-windows.yml`.
2. Computes the current UTC time and checks whether it falls within any
   active window (window `start_utc` ≤ now < `end_utc`).
3. If a freeze is active AND the PR/dispatch is not labelled
   `exam-day/break-glass`, the check fails and the status posts a summary
   of the active window (which sitting, why frozen, when it ends).

The freeze-windows file is generated deterministically; manual edits are
forbidden — change the catalog instead.

### 3c. Break-glass for a required production change during a freeze

Only use this for a change that cannot wait until the window closes
(live-site incident, security CVE with known exploitation). Process:

1. On-call SRE primary approves verbally in `#incident-exam-day`.
2. Apply the `exam-day/break-glass` label to the PR, with a comment linking
   to the incident doc.
3. Merge and deploy; the freeze workflow will record the break-glass in the
   run summary.
4. File a post-incident review within 48h that documents the deploy reason
   and whether the freeze policy needs adjustment.

The label is an advisory opt-out, not a security control — it is monitored
and audited. Repeated break-glass without post-mortem is a policy breach.

## 4. Break-glass procedure (SLO burning faster than budget)

These actions are in order of escalating impact. Always confirm the alert
is real (§4a) before touching production.

### 4a. Confirm the alert is real

- [ ] The alert source (Prometheus) agrees with the Grafana dashboard.
- [ ] Synthetic probes from at least two probe regions agree (probe may be
      local to a single region; platform may be global).
- [ ] The `cena_http_requests_total` 5xx rate aligns with the burn-rate
      alert — not a single noisy endpoint.

If any of the above is false, treat as a probe/metric false positive and
downgrade to P2.

### 4b. Fast lever: disable non-essential Tier-3 LLM consumption

During a freeze window, question-generation and explanation-L3 are
non-essential (content bank is pre-warmed by `default_lead_days` in each
catalog entry). Disable via:

```
POST /api/admin/feature-flags/freeze
{ "features": ["question-generation", "explanation-l3"] }
```

This is idempotent; running it twice is harmless. Re-enables via the
unfreeze endpoint. See [`llm-vendor-outage.md`](llm-vendor-outage.md) §4c.

### 4c. Fast lever: de-prioritise admin dashboard load

Admin p95 latency SLO is already loosened (§2a). If admin reads are
crowding out student reads, activate the admin-degraded banner:

```
POST /api/admin/banner/activate
{ "surface": "admin", "copy_id": "admin-degraded-exam-day" }
```

Pre-approved copy (ship-gate clean, no time-pressure framing): "Some
admin reports may load slowly while students are active. Full detail is
restored shortly."

### 4d. Scale lever: force a scale-out tier bump

If §4b and §4c don't bend the burn-rate curve within 10 minutes, bump
student pods one tier beyond the exam-day auto-scale ceiling. See
[`docs/ops/capacity/exam-day-capacity-plan.md`](../capacity/exam-day-capacity-plan.md)
§5 for per-tier manual scale commands. Never bump below normal minimum;
never skip the gradual rollout guard.

### 4e. Last-resort lever: tenant-admin forced catalog overlay

Per [prr-220](../../../tasks/pre-release-review/TASK-PRR-220-tenant-admin-catalog-overlay.md),
a tenant admin can temporarily remove a family (e.g. "disable SAT for
tenant `inst-123` until T+6h"). Use this only when:

- A single tenant is generating the load spike.
- Removing that target/family protects all other tenants.
- The tenant admin is on the bridge and signs off.

Document the overlay in the incident channel, and schedule the overlay to
self-revert when the window closes (overlays carry a `valid_until`).

## 5. Recovery criteria

The window is considered "recovered" (and the incident can be downgraded)
when **all** of the following hold for 30 consecutive minutes:

- All SLOs from §2a back within exam-day target.
- Burn-rate alerts clear (both 1h and 5m windows green).
- Synthetic probes green across all exam families.
- No active feature-flag freeze from §4b remaining.

Once the sitting's `end_utc` passes, close the incident. The freeze window
auto-ends; no manual unfreeze needed.

## 6. Post-window drill (every exam-day)

Within 48h of a window closing:

1. Publish a short cost + latency report to `ops/reports/exam-day-<date>.md`
   — peak concurrent sessions, cost delta vs forecast, any SLO burn.
2. Diff forecast vs actual peak concurrent sessions. If actual > 1.2 ×
   forecast or < 0.8 × forecast, update the forecast in
   [`docs/ops/capacity/exam-day-capacity-plan.md`](../capacity/exam-day-capacity-plan.md) §4.
3. Note any break-glass activations and whether the §4 ordering needs
   tweaking.
4. Confirm the next window's freeze is still scheduled correctly in
   [`ops/release/freeze-windows.yml`](../../../ops/release/freeze-windows.yml).

## 7. Related

- [prr-016](../../../tasks/pre-release-review/TASK-PRR-016-publish-exam-day-slo-change-freeze-window-in-cd.md) — source task
- [prr-053](../../../tasks/pre-release-review/TASK-PRR-053-exam-day-capacity-plan-bagrut-traffic-forecast.md) — capacity plan (Bagrut baseline)
- [prr-231](../../../tasks/pre-release-review/TASK-PRR-231-amend-capacity-plan-sat-pet.md) — SAT+PET capacity amendment
- [prr-220](../../../tasks/pre-release-review/TASK-PRR-220-tenant-admin-catalog-overlay.md) — catalog overlay (§4e break-glass lever)
- [prr-039](../../../tasks/pre-release-review/TASK-PRR-039-synthetic-probe-coverage.md) — synthetic probe coverage
- [`ADR-0050`](../../adr/0050-multi-target-student-plan.md) — multi-target plan (why 7-window compound calendar)
- [`llm-vendor-outage.md`](llm-vendor-outage.md) — cascade lever used in §4b
- [`ops/release/freeze-windows.yml`](../../../ops/release/freeze-windows.yml) — generated freeze windows
- [`.github/workflows/exam-day-freeze.yml`](../../../.github/workflows/exam-day-freeze.yml) — CD gate
