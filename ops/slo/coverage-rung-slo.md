# Coverage Rung SLO (prr-210)

**Status**: Active ship-gate. Enforced in CI by
`scripts/shipgate/coverage-slo.mjs` on every pull request and push to `main`.

**Owner**: content-engineering + persona-educator review.
**Source of truth for N**: [`contracts/coverage/coverage-targets.yml`](../../contracts/coverage/coverage-targets.yml).
**Depends on**: [prr-201](../../tasks/pre-release-review/TASK-PRR-201-coverage-waterfall-orchestrator.md)
(waterfall + projection is the data source). [prr-200](../../tasks/pre-release-review/TASK-PRR-200-parametric-question-engine.md)
produces the variants this SLO counts.

---

## What the SLO guarantees

Every **active** cell in the coverage matrix carries at least **N** ready,
CAS-verified, author-owned variants at release time. A cell is the tuple

```
(topic, difficulty, methodology, track, questionType)
```

with language rolled into the question-type bucket (single matrix per
release; per-language surfaces are handled by translation QA).

ADR-0040 is load-bearing: a Halabi variant does not fill a Rabinovitch rung
and vice-versa. ADR-0043 is load-bearing: Ministry-sourced items do not count
toward coverage — only author-owned recreations that pass CAS.

---

## Default N by question-type rung

Opening position (subject to persona-educator review as evidence accrues):

| Question type       | Default N | Rationale |
|---------------------|-----------|-----------|
| `multiple-choice`   | **10**    | Cheap authoring; deep buffer keeps exposure-control rotations (engine doc §25) healthy. |
| `step-solver`       | **5**     | Authoring cost higher; buffer smaller but still above statistical noise floor. |
| `free-text`         | **5**     | Mirror step-solver default; most free-text items have a step-solver companion. |
| `fbd-construct`     | **3**     | Narrow topic subset (physics). Still enforced — every active cell must have ≥3. |
| Global fallback     | **5**     | Catches any cell whose question type is not categorised above. Deliberately small so a misconfigured cell cannot silently pass. |

Per-cell overrides live in `contracts/coverage/coverage-targets.yml` under
`cells[].min`. Cell-level overrides win over the default table.

## Active vs draft

- `active: true` → ship-gate **fails CI** if count < N.
- `active: false` → gate emits a report line but does **not** fail.

Use `active: false` to declare a cell that is still being onboarded. Flipping
a cell from draft → active is a decision-gated change that lands in a PR
along with evidence the cell now has ≥ N ready variants.

## What is *not* enforced by this SLO

1. **Translation parity.** Language versioning is the TranslationQA gate, not
   this one. This SLO measures the per-cell authoring backlog.
2. **Ministry-reference similarity.** That is ADR-0043 / prr-201 stage-2;
   similar items are already dropped before they reach the variant store.
3. **Student-visible mastery coverage.** That is the advancement gate, not
   content coverage.

---

## How it runs

### CI gate

`.github/workflows/shipgate.yml` invokes
`node scripts/shipgate/coverage-slo.mjs` after the other ship-gate scanners.
The script:

1. Loads `contracts/coverage/coverage-targets.yml` and validates its shape.
2. Loads `ops/reports/coverage-variants-snapshot.json` (the variant-store
   snapshot — committed by the nightly projection job; may be absent on a
   fresh clone).
3. Resolves required-N per cell using the precedence in
   `contracts/coverage/coverage-targets.yml`.
4. Writes the audit table to `ops/reports/coverage-rung-status.md`.
5. Exits `0` if every active cell has count ≥ N, else exits `1` with a
   diff listing the failing cells.

Exit codes:

| Exit | Meaning |
|------|---------|
| `0` | All active cells meet SLO (or only advisory cells under). |
| `1` | ≥1 active cell under SLO, OR fixture mode failed, OR snapshot malformed. |
| `2` | Missing `coverage-targets.yml` at startup. |
| `3` | Snapshot file present but unparseable JSON. |

### Snapshot format (`ops/reports/coverage-variants-snapshot.json`)

```json
{
  "schemaVersion": "1.0",
  "runAt": "2026-04-20T10:00:00Z",
  "source": "prr-201-projection",
  "cells": [
    {
      "topic": "algebra.linear_equations",
      "difficulty": "Easy",
      "methodology": "Halabi",
      "track": "FourUnit",
      "questionType": "multiple-choice",
      "language": "en",
      "variantCount": 12,
      "belowSlo": false,
      "curatorTaskId": null
    }
  ]
}
```

- `variantCount` is the number of author-owned CAS-verified ready variants
  for that cell, after dedupe.
- `belowSlo` is prr-201's computed flag (orchestrator marks a cell below-SLO
  when it returns `CuratorQueued`). The script recomputes against the live
  targets file — `belowSlo` in the snapshot is informational, the
  authoritative fail/pass comes from YAML-vs-count comparison.
- `curatorTaskId` is populated when a stage-3 curator task is open for the
  cell. The report surfaces these explicitly so humans know why a red cell
  is red.

Missing snapshot is treated as "variant count = 0 for every declared cell"
— so a clean checkout fails the gate loudly rather than silently passing.
Override with `--allow-empty` in the very first bootstrap run only.

## Hot-fix runbook (red cell before a release)

Triage order (cheapest → most expensive):

1. **Re-run the waterfall.** `dotnet run --project src/tools/Cena.Tools.QuestionGen
   -- --cell <address> --target <N>`. If the template slot space is now
   adequate, stage 1 fills the gap for free.
2. **Unblock stage 2.** If the cell is stuck because the isomorph generator
   is circuit-open, restore the upstream and let nightly retries fill in.
3. **Curator escalation.** If prr-201 already enqueued a curator task for
   the cell (the report's `Curator task` column is non-empty), ping the
   content lead to staff it against the SLO deadline.
4. **Author a new template (prr-200).** Only when the existing template
   can't reach N due to slot-space or pedagogical constraints. Add the
   template, re-run the waterfall, update the snapshot, re-run CI.

## Change-control

- Changing an `active: true` cell's `min` requires a PR that cites evidence
  the new N is sound (linked to an engine-doc section or persona-educator
  note).
- Flipping `active: false → true` requires the latest snapshot to already
  show count ≥ N.
- Retiring a cell (removing the entry) is allowed only when the content is
  being sunset and the `questions` read-model reflects it.
