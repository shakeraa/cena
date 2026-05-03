# Exam-day capacity plan — multi-target compound calendar

**Source tasks**:
- [prr-053](../../../tasks/pre-release-review/TASK-PRR-053-exam-day-capacity-plan-bagrut-traffic-forecast.md)
  (Bagrut baseline).
- [prr-231](../../../tasks/pre-release-review/TASK-PRR-231-amend-capacity-plan-sat-pet.md)
  (amendment: SAT + PET 7-window compound calendar).

**Owners**: cena-sre (primary), cena-finops (cost review), cena-platform (scale-out levers).
**Depends on**: [`exam-day-slo.md`](../runbooks/exam-day-slo.md), [`ops/release/freeze-windows.yml`](../../../ops/release/freeze-windows.yml), [ADR-0050](../../adr/0050-multi-target-student-plan.md).

---

## 1. Motivation + scope

The pre-launch pre-release review (2026-04-20, persona-sre + persona-finops
cross-lens) identified that the platform's observed baseline (P95 ~180
concurrent student sessions across tenants) will spike 5–10× on exam-day
windows. Absent a concrete forecast and scale-out plan, auto-scaling alone
will lag the spike and burn the latency error budget in minutes.

This doc:

1. Models observed cohort sizes → peak concurrent-session forecast per
   window (§4).
2. Declares the scale-out policy (§5), provisioning triggers, and what
   ops runs manually vs what HPA does.
3. Anchors the 7-window compound calendar (§2) to the catalog so the
   forecast evolves deterministically when new exam families are added.

Per [prr-231](../../../tasks/pre-release-review/TASK-PRR-231-amend-capacity-plan-sat-pet.md)
this supersedes the original Bagrut-only prr-053 plan. The original task
is annotated with `Superseded-in-scope-by prr-231` in its body.

## 2. Compound calendar (7 windows + SAT quarterly)

Per [ADR-0050](../../adr/0050-multi-target-student-plan.md), a StudentPlan
is a list of ExamTargets. Because an individual student can carry targets
across multiple families, the peak load is not the sum of per-family
peaks — it is the union of active windows in the 14-day lookback.

| Window id | Family | Canonical month | Approx days | Notes |
|-----------|--------|-----------------|-------------|-------|
| BAGRUT-WINTER-A | Bagrut | Jan 28 | 3 | Winter moed for 3U/4U math re-takers. Smallest window. |
| BAGRUT-ENGLISH-WINTER | Bagrut | Jan (various modules) | 5 | English modules spread across Jan. Currently queued — will activate when `bagrut-english` catalog entries flip to `availability: launch`. |
| PET-SPRING | PET | mid-Apr | 2 | NITE quarterly. |
| BAGRUT-MATH-MOED-A | Bagrut | Jun 15 | 2 | Main summer spike; largest cohort. |
| BAGRUT-PHYS-CHEM-BIO | Bagrut | Jun 22 / Jun 29 / Jul 3 | 7 | Overlaps PET-SUMMER tail. Compound. |
| BAGRUT-MATH-MOED-B | Bagrut | Jul 20 | 2 | Re-takers. |
| PET-SUMMER | PET | mid-Jul | 2 | NITE quarterly. |
| PET-AUTUMN | PET | mid-Sep | 2 | NITE quarterly. |
| PET-WINTER | PET | mid-Dec | 2 | NITE quarterly. |
| SAT-QUARTERLY | SAT | Mar/May/Aug/Oct/Dec | 1 per sitting | SAT Math is currently `availability: roadmap` (not in `ops/release/freeze-windows.yml` yet). When it flips to `launch`, the generator auto-adds the quarterly anchor dates. |

The catalog is the source of truth; the compound calendar above is
illustrative. The canonical machine-readable form lives in
[`ops/release/freeze-windows.yml`](../../../ops/release/freeze-windows.yml),
regenerated from catalog on every commit.

### 2a. Why SAT+PET change the shape

Prior to prr-231, the plan assumed two June/July peaks with steep ramp-up
and ramp-down around Bagrut moed A / moed B. SAT + PET flatten the bottom
of the curve: we never drop to baseline-only for more than 8 weeks at a
time. Implications:

- Headroom multipliers in §4 are applied across the whole year, not only
  the summer window.
- Cold-start costs amortise better (fewer full scale-down events).
- The break-glass catalog-overlay lever ([prr-220](../../../tasks/pre-release-review/TASK-PRR-220-tenant-admin-catalog-overlay.md))
  becomes more important: a tenant can disable SAT for a specific window
  without changing the Bagrut-facing experience.

## 3. Baseline measurement + forecast inputs

### 3a. Observed baseline (pre-launch proxy)

Data sources:

- Synthetic load tests from prr-039 probe suite.
- Per-tenant enrollment counts from the admin Marten projection
  (`institute_enrollment_counts` read-model).
- Catalog selection distribution from the prr-218 StudentPlan aggregate
  — i.e. of the 100 pilot students, how many carry which ExamTarget.

### 3b. Per-family cohort inputs (pilot-era estimates)

These numbers will be superseded by production measurements after the
first full window — update §4 after the post-window drill.

| Family            | Cohort size (pilot) | Expected production | Session duration mean | Session duration p95 |
|-------------------|--------------------:|--------------------:|----------------------:|---------------------:|
| Bagrut math       | 60                  | 600                 | 22 min                | 45 min               |
| Bagrut phys/chem  | 15                  | 180                 | 25 min                | 50 min               |
| Bagrut hebrew/lit | 30                  | 420                 | 18 min                | 35 min               |
| PET quantitative  | 8                   | 240                 | 30 min                | 55 min               |
| SAT math          | 0 (pre-launch)      | 80 (roadmap)        | 25 min (estimated)    | 50 min (estimated)   |

### 3c. Per-window concurrency distribution

Empirical observation (pilot): on exam-day morning, 62% of a family's
cohort is active in a 2-hour peak window; the rest is smeared across T-24h
review sessions.

Peak concurrent sessions = cohort × 0.62 × session_concurrency_factor
(0.35 for 22-min mean, 0.45 for 30-min mean).

## 4. 95th-percentile concurrent-session forecast

Applied to expected production cohort. Headroom multiplier = 1.5× applied
on top of forecast (see §4a for rationale).

| Window | P50 concurrent | P95 concurrent | Forecast w/ headroom | Previous (prr-053 Bagrut-only) |
|--------|----------------|----------------|----------------------|--------------------------------|
| BAGRUT-WINTER-A | 45 | 95 | **143** | 145 |
| PET-SPRING | 38 | 75 | **113** | (n/a) |
| BAGRUT-MATH-MOED-A | 260 | 520 | **780** | 780 |
| BAGRUT-PHYS-CHEM-BIO compound | 180 | 380 | **570** | 520 |
| BAGRUT-MATH-MOED-B | 85 | 180 | **270** | 270 |
| PET-SUMMER | 42 | 85 | **128** | (n/a) |
| PET-AUTUMN | 42 | 85 | **128** | (n/a) |
| PET-WINTER | 42 | 85 | **128** | (n/a) |
| SAT-QUARTERLY (Mar) | 12 | 25 | **38** | (n/a) |
| SAT-QUARTERLY (May) | 12 | 25 | **38** | (n/a) |

### 4a. Headroom multiplier = 1.5× rationale

- 1.2× — measurement error (pilot cohort is small; the 62% peak
  concentration could swing to 70%).
- 1.25× — co-occurrence: students with cross-family targets (ADR-0050)
  may lock in study sessions the morning-before across windows.
- 0.8× — smoothing: cohort is not perfectly synchronised at T-24h.
- Net: ~1.5×.

If a window observes actual > 1.2 × forecast, update both the forecast and
this multiplier in the post-window drill.

### 4b. Component-level load

Given the forecast, component-level provisioning:

| Component                 | MOED-A forecast | Normal day | Scale factor | Binding resource |
|---------------------------|----------------:|-----------:|-------------:|------------------|
| `cena-student` pods       | 18              | 4          | 4.5×         | HPA on CPU + custom metric |
| `cena-admin` pods         | 3               | 2          | 1.5×         | HPA on CPU (relaxed during freeze) |
| Redis `session-cache`     | 6 GB (3 shards) | 2 GB       | 3×           | memory |
| Postgres `cena-primary`   | 8 vCPU          | 4 vCPU     | 2×           | CPU + conn-pool |
| Marten event-store        | 6 vCPU          | 3 vCPU     | 2×           | I/O |
| LLM vendor quota          | 2× tier-3 RPM   | baseline   | 2×           | vendor throttle |

### 4c. Cost envelope

- Normal day: ~$180/day platform.
- BAGRUT-MATH-MOED-A window (3 days): ~$720/day platform = $2,160 total.
- Incremental cost for PET sittings: ~$130/day × 2 days × 4/year = $1,040.
- Incremental cost for SAT (roadmap): ~$40/day × 1 day × 5/year = $200.

Costs above platform-only (LLM costs tracked separately under
[prr-047](../../../tasks/pre-release-review/TASK-PRR-047-prompt-cache-slo.md)
and [prr-084](../../../tasks/pre-release-review/TASK-PRR-084-llm-cost-breach-runbook.md)).

## 5. Scale-out policy

### 5a. Tiered auto-scaling

Helm values (`deploy/helm/cena/values-production.yaml`) carry two scaling
profiles — `default` and `exam-day`. The exam-day profile is activated by
the freeze-window workflow (prr-016) pre-announcing the next window to the
cluster operator 24h before `start_utc`.

| Profile    | Student min | Student max | Admin min | Admin max | Redis GB |
|------------|------------:|------------:|----------:|----------:|---------:|
| default    | 4           | 12          | 2         | 6         | 2        |
| exam-day   | 8           | 24          | 2         | 4 (capped)| 6        |

HPA metric: weighted CPU (70%) + custom `cena_session_active` (30%).
Target utilisation during exam-day profile is 55% (normal 70%) — extra
headroom for traffic bursts.

### 5b. Manual scale-out levers (operator-initiated)

If auto-scaling lags (common at the 10-min mark of a spike), operators
have three levers in order of impact:

1. **Pre-scale before anchor.** `kubectl scale deploy/cena-student --replicas=12`
   run T-30m against the anchor.
2. **Force profile swap.** `helm upgrade cena deploy/helm/cena
   --set scalingProfile=exam-day-burst`. Adds +50% pods over the exam-day
   profile ceiling.
3. **Catalog overlay.** Per [prr-220](../../../tasks/pre-release-review/TASK-PRR-220-tenant-admin-catalog-overlay.md),
   disable a specific family for a specific tenant if the load is
   tenant-concentrated. Last-resort lever.

### 5c. Scale-down policy

Downsize is automatic, starting at `end_utc + 2h` to avoid thrashing on
tail traffic. Student pods return to `default.min` over a 4h cooldown.
Redis shards remain warm for 24h (cheap to keep, expensive to re-prime).

## 6. Synthetic probe + load-test coverage

Per prr-231, the prr-039 synthetic probe suite now covers:

- Bagrut math 3U/4U/5U first-question round-trip.
- Bagrut civics/history/tanakh/literature first-question round-trip.
- PET quantitative first-question round-trip.
- IB Math HL AA (pilot).
- Placeholder probe for SAT — re-enabled when SAT catalog flips to `launch`.

Pre-window load test (run T-7d against staging):

- Ramp to 1.0× forecast over 10 min → hold 30 min.
- Ramp to 1.2× forecast over 5 min → hold 10 min.
- Spike to 2.0× forecast over 1 min → hold 3 min (chaos).

Pass gates: all SLOs in `exam-day-slo.md` §2a remain within exam-day
target during the 1.0× and 1.2× holds; `exam-day` SLOs can be loosened to
`normal` during the 2.0× spike (operators will be actively responding).

## 7. Tenant-isolation observations (ADR-0001)

The concurrent-session count is aggregated across tenants. Two invariants
hold:

1. A single tenant cannot starve others — HPA-scaled pods serve all
   tenants, and Redis sharding is per-tenant by key prefix.
2. The break-glass overlay (prr-220) scoping is tenant-local — disabling
   a family in one tenant never affects others.

If a single tenant is > 50% of the forecast, the SRE lead receives a
pre-window note to validate rehearsal with that tenant admin.

## 8. Break-glass ordering

Referenced from [`exam-day-slo.md`](../runbooks/exam-day-slo.md) §4.
Repeated here for quick-access:

1. §4b — disable non-essential Tier-3 LLM (question-gen, explanation-L3).
2. §4c — admin dashboard de-prioritisation (banner + relaxed SLO).
3. §4d — manual scale bump (this plan, §5b).
4. §4e — tenant-admin catalog overlay (last resort, single tenant).

## 9. Open questions + follow-ups

- Refine SAT cohort estimate once SAT family flips to `availability: launch`.
  Target: 30 days of production data + re-run §4.
- Study whether BAGRUT-ENGLISH winter modules merit a dedicated window id
  (currently folded into BAGRUT-ENGLISH-WINTER). Depends on catalog flip
  date.
- Coordinate with finops on cost-reservations for Q3 SAT ramp-up
  (prr-231).

## 10. Related

- [`exam-day-slo.md`](../runbooks/exam-day-slo.md) — SLOs + break-glass
- [prr-039](../../../tasks/pre-release-review/TASK-PRR-039-synthetic-probe-coverage.md) — synthetic probes
- [prr-047](../../../tasks/pre-release-review/TASK-PRR-047-prompt-cache-slo.md) — prompt cache (upstream cost driver)
- [prr-220](../../../tasks/pre-release-review/TASK-PRR-220-tenant-admin-catalog-overlay.md) — catalog overlay (break-glass lever)
- [ADR-0050](../../adr/0050-multi-target-student-plan.md) — multi-target model
- [`ops/release/freeze-windows.yml`](../../../ops/release/freeze-windows.yml) — generated compound calendar
