# 2026-04-30 — PRR-322: Mock-exam run cost telemetry

## What ships

- New Marten doc `Cena.Infrastructure.Documents.MockExamRunCost` keyed on
  runId. Indexed on `ExamCode` and `ComputedAt` (both used by the admin
  dashboard's filter/rollup queries).
- New configuration section `Cena:MockExamCostRates` (DI'd via
  `IOptions<MockExamCostRateConfig>`).
- 3 new admin GETs under `/api/admin/mock-exam-runs/cost` (auth:
  ModeratorOrAbove).
- 1 new admin SPA page at `apps/mock-exam-runs/cost`.
- 1 new OpenTelemetry counter `cena_mock_exam_run_cost_usd_total` tagged
  `examCode` + `studentTenant`.

## Migration steps

### 1. Schema rebuild — INERT for existing deployments

Marten auto-creates the new `mt_doc_mockexamruncost` table on first read or
write. No manual schema action required for the doc itself.

If the host runs Marten in `Schema.AutoCreate.None` mode (production),
trigger the schema patch at deploy time:

```bash
docker exec cena-admin-api dotnet Cena.Admin.Api.Host.dll --apply-changes
```

The patch is **additive** (one new table). No data backfill — historical
runs predate the cost-attribution hook and have no measurable cost
attribution; they appear absent from the dashboard, which is honest.

### 2. Configure rates

Default rates (2026-Q2 estimates) live in `MockExamCostRateConfig`:

| Stream | Default | Source |
|---|---|---|
| CAS    | $0.0001 / call            | SymPy sidecar amortized container CPU |
| LLM in | $0.0008 / 1k tokens       | Anthropic Haiku 3.5 input ($0.80/M) |
| LLM out| $0.0040 / 1k tokens       | Anthropic Haiku 3.5 output ($4.00/M) |
| OCR    | $0.0050 / call            | Gemini Vision + Doctr fan-out average |

Override per environment in `appsettings.{env}.json`:

```json
{
  "Cena": {
    "MockExamCostRates": {
      "CasUsdPerCall":           0.0001,
      "LlmInputUsdPer1kTokens":  0.0008,
      "LlmOutputUsdPer1kTokens": 0.0040,
      "OcrUsdPerCall":           0.0050
    }
  }
}
```

### 3. Reconciliation cadence

**Monthly** — operator pulls the trailing 30-day per-run totals from
`/api/admin/mock-exam-runs/cost/daily?days=30`, sums by stream, compares
to vendor invoices (Anthropic, Google Cloud Vision). If realized cost
drifts > 10% from projected cost on any single stream, update
`MockExamCostRateConfig` rates in `appsettings.{env}.json` and redeploy.
The historical doc rows keep the OLD USD totals (audit-friendly); only
new rows reflect the corrected rate.

### 4. Replay rates (rare, only if audit requires)

If a vendor retroactively corrects pricing or ops needs a historical
re-projection at new rates:

1. Query the raw COUNTS (not USDs) from the doc:
   `select Id, CasCallsCount, LlmTokensInput, LlmTokensOutput, OcrCallsCount from mt_doc_mockexamruncost`
2. Multiply against the new rate config off-line.
3. Persist as a NEW doc type (not overwrite — original remains the audit
   record).

Tooling for step 2 is a follow-up (PRR-322f-replay-script — not yet filed
as no audit has demanded it).

## What is intentionally absent (filed as follow-ups)

| ID | Description | Rationale for deferral |
|---|---|---|
| `PRR-322f-llm-attribution` | Wire LLM token counter on tutor-mid-exam + post-grade-explanation paths | No LLM call sites on the mock-exam path today; primitive without consumer |
| `PRR-322f-ocr-attribution` | Wire OCR call counter on photo-input mock-exam ingestion path | Photo-input mock-exam ingestion doesn't exist yet |
| `PRR-322f-integration-test` | Live cena-postgres integration test asserting MockExamRunCost doc materializes after a real Submit | Test fixture pattern needs more setup than fits this branch; backend behaviour pinned by unit tests + route-smoke + e2e via the SPA page |
| `PRR-322f-multi-tenant-rollup` | Across-institute totals on the dashboard | Gated on ADR-0001 Phase 1 (multi-institute), in flight separately |
| `PRR-322f-cost-alarms` | Per-tenant daily-cost alarm thresholds in Grafana | Needs ops to pick the numbers; tracked separately with panel screenshots |
| `PRR-322f-replay-script` | Off-line replay tool for retroactive rate changes | No audit has demanded it yet |

## Roll-back

`git revert <merge-sha>` removes the registration call sites; the doc
table can be dropped with `drop table mt_doc_mockexamruncost` after
revert. No data dependency on the doc — it's read-only telemetry.
