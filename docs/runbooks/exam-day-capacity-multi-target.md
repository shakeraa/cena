# Runbook — Exam-day capacity, multi-target extension

**Source task**: [prr-231](../../tasks/pre-release-review/TASK-PRR-231-amend-capacity-plan-sat-pet.md)
**Amends**: [prr-053](../../tasks/pre-release-review/TASK-PRR-053-exam-day-capacity-plan-bagrut-traffic-forecast.md)
**Primary owner**: cena-sre

This runbook is the multi-target companion to the main capacity plan
([`docs/ops/capacity/exam-day-capacity-plan.md`](../ops/capacity/exam-day-capacity-plan.md))
and the main SLO/freeze runbook
([`docs/ops/runbooks/exam-day-slo.md`](../ops/runbooks/exam-day-slo.md)).
It is kept deliberately short: everything substantive is in those two
documents. This file exists because prr-231 specified a distinct runbook
path, and because SRE prefers a single-page quick-reference when paging on
a non-Bagrut family window.

---

## 1. Quick reference — which window am I in?

```bash
# From anywhere in the repo
node -e "
  const fs = require('fs');
  const yaml = fs.readFileSync('ops/release/freeze-windows.yml', 'utf8');
  const now = new Date().toISOString();
  console.log('Current UTC:', now);
  console.log('Freeze file:', yaml.split('\n').slice(0, 6).join('\n'));
"
```

Or check the Action: the `Exam-day change-freeze gate` workflow posts a
summary on every PR noting whether a window is active.

## 2. Per-family break-glass differences

The main runbook's levers apply across all families. A few family-specific
notes:

### 2a. Bagrut windows

- LLM freeze (`llm-vendor-outage.md §4c`) targets `question-generation` and
  `explanation-l3`. During Bagrut, both are safe to freeze: the Bagrut
  content bank is pre-warmed (`default_lead_days: 180`).
- Math-specific: CAS verification (ADR-0002) is local SymPy; it is never
  affected by LLM outages. Student practice-mode still works.

### 2b. PET windows

- PET content bank has `default_lead_days: 90` — less pre-warming headroom
  than Bagrut. Prefer the admin-de-prioritisation lever
  (`exam-day-slo.md §4c`) before freezing LLM paths.
- PET students tend to spike 8–12h earlier than the T-24h peak for Bagrut;
  make sure the `exam-day` Helm profile is activated T-48h, not T-24h.

### 2c. SAT windows (when SAT flips to `launch`)

- SAT cohort is small — absolute load is low, but a single SAT window
  can mask itself (background noise). Keep synthetic probes green even
  when the forecast looks trivial.
- SAT is `passback_eligible: false`; no Ministry passback path to worry
  about during the window.

## 3. Coordinating with freeze-gate (prr-016)

The freeze window covers all families listed in
`ops/release/freeze-windows.yml`. When prr-231 extended the calendar to
SAT + PET, the generator (`scripts/ops/generate-freeze-windows.mjs`)
picked up those sittings automatically. Confirm after every catalog flip:

```bash
node scripts/ops/generate-freeze-windows.mjs
git diff ops/release/freeze-windows.yml
```

A flip that should have added a window but didn't is a generator bug —
file a blocker on prr-231.

## 4. Post-window drill checklist

For each sitting that closes:

- [ ] Export per-family concurrent-session peak from Grafana.
- [ ] Compare actual vs forecast in
      [`docs/ops/capacity/exam-day-capacity-plan.md`](../ops/capacity/exam-day-capacity-plan.md) §4.
- [ ] If drift > 20%, open a follow-up task to update the forecast table.
- [ ] File a `ops/reports/exam-day-<family>-<date>.md` short note.

## 5. Related

- [`docs/ops/runbooks/exam-day-slo.md`](../ops/runbooks/exam-day-slo.md) — full SLO + break-glass
- [`docs/ops/capacity/exam-day-capacity-plan.md`](../ops/capacity/exam-day-capacity-plan.md) — capacity plan
- [`scripts/ops/generate-freeze-windows.mjs`](../../scripts/ops/generate-freeze-windows.mjs) — generator
- [`ops/release/freeze-windows.yml`](../../ops/release/freeze-windows.yml) — generated freeze windows
