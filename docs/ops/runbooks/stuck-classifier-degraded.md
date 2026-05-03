# Runbook: Stuck-Type Classifier Degraded

**Scope**: `Cena.Actors.Diagnosis.HybridStuckClassifier` and its dependencies
(Claude Haiku LLM, Marten persistence, Redis cost-circuit-breaker).

**Audience**: on-call engineer. Goal: restore hint-ladder quality without
manual intervention on the student-facing hot path.

## Severity tiers

| Severity | Signal | User impact |
|---|---|---|
| **SEV-3 (alert)** | `cena_stuck_persist_failure_total` > 5/min for 5m | Diagnoses still returned live; analytics + admin dashboards degraded |
| **SEV-2 (page)** | `cena_stuck_diagnoses_total{source="LlmError"}` > 30% of total for 10m | Hybrid degrades to heuristic-only; quality drops but students still get hints |
| **SEV-1 (page)** | `cena_stuck_diagnose_latency_ms` p99 > 2000ms for 10m **AND** hint-ladder latency budget breached | Student hints are slow; visible UX impact |

## Quick disable (the "kill switch")

The classifier is gated by `Cena:StuckClassifier:Enabled`. Flip it to
`false` in whatever config-delivery mechanism the environment uses
(Helm values, ConfigMap patch, SSM Parameter Store, `appsettings.json`
override — whichever applies).

On `IOptionsMonitor` reload, `HybridStuckClassifier.DiagnoseAsync`
immediately begins returning `StuckDiagnosis.Unknown` with
`Source = None`. Callers fall back to the existing level-based hint
ladder within a single tick.

No process restart required. Verify with:

```bash
curl -s "$METRICS_ENDPOINT" | grep cena_stuck_diagnoses_total
# Expect: the `source="None"` series growing, others flat.
```

## Common failure modes

### 1. LLM returning 5xx / rate-limited

- **Symptom**: `cena_stuck_diagnoses_total{source="LlmError"}` spikes.
- **Cause**: Anthropic API outage, quota exhausted, or network issue.
- **Auto-mitigation**: Hybrid composer already falls back to the
  (possibly weak) heuristic result — no student-facing outage.
- **Action**:
  1. Check Anthropic status page.
  2. If localized to Cena's key: check cost-circuit-breaker status:
     `curl -s $ADMIN_API/_internal/cost-breaker/status`.
  3. If breaker is open because of legitimate spend: raise the daily
     cap temporarily or flip `Enabled=false` and wait for the reset.

### 2. LLM returning parse-failures (`ErrorCode=parse_failure`)

- **Symptom**: `cena_stuck_diagnoses_total{source="LlmError"}` with
  `parse_failure` log reason.
- **Cause**: Anthropic silently shipped a model update that changed
  output formatting.
- **Action**:
  1. Flip `Enabled=false` immediately — the classifier will degrade to
     heuristic-only, student UX unaffected.
  2. Inspect recent LLM outputs via debug logs (scrubbed of PII by
     TutorPromptScrubber earlier in the pipeline).
  3. Update `ClaudeStuckClassifierLlm.SystemPrompt` to re-tighten the
     response format; bump `ClassifierVersion` so persisted-diagnosis
     partitioning cleanly separates old/new outputs.
  4. Ship and re-enable.

### 3. Persistence failures (Marten / Postgres)

- **Symptom**: `cena_stuck_persist_failure_total` climbing.
- **Cause**: Postgres outage, disk full, schema out of sync.
- **Student-facing impact**: NONE — persistence is best-effort by
  design. The classifier's live output is still returned to the
  hint-ladder. Only analytics + admin dashboards are affected.
- **Action**:
  1. Follow the standard Postgres-degraded runbook (separate doc).
  2. Once Postgres recovers, no back-fill is needed — session-scoped
     diagnoses are transient by design.

### 4. High latency without obvious cause

- **Symptom**: p99 latency > 2s, no specific error surge.
- **Likely cause**: prompt-cache cold-start after a redeploy or config
  change.
- **Action**:
  1. Wait 5-10 minutes for cache warm.
  2. If still high: verify `Cena:StuckClassifier:LlmModel` is still
     pinned to Haiku (not accidentally Sonnet).
  3. If still high: flip `Enabled=false`, file an incident, investigate.

### 5. Classifier saying "Unknown" too often

- **Symptom**: `cena_stuck_low_confidence_total` > 50% of total over
  1h in production.
- **Not an outage** — this is a quality signal, not an availability
  signal. But worth investigating:
  1. Inspect the stuck-type distribution via admin dashboard. If one
     category dominates, heuristic rules may need tuning.
  2. Inspect the `SourceReasonCode` histogram. "heuristic.no_rule_fired"
     dominance means rules are too narrow; "hybrid.disagree.*"
     dominance means heuristic and LLM keep disagreeing (prompt drift
     or taxonomy confusion).
  3. File a RDY-063 follow-up task with distribution snapshot — not an
     oncall action.

## Re-enabling after incident

1. Confirm the underlying issue is fixed.
2. Flip `Cena:StuckClassifier:Enabled=true` via config.
3. Watch `cena_stuck_diagnoses_total{source="Llm"}` return to normal
   ratio within 60s.
4. Spot-check admin dashboard: "Top items by stuck-type" query
   resolves in <2s.
5. Close incident.

## What NOT to do

- **Don't** clear `StuckDiagnosisDocument` rows to "start fresh". The
  30-day TTL handles cleanup. Manual deletion breaks historical
  analytics windows.
- **Don't** rotate `AnonSalt` outside a planned window. Salt rotation
  intentionally breaks cross-session anonymizer linkability and
  effectively "forgets" all outstanding diagnoses. That's a feature,
  not a fix.
- **Don't** hot-patch the system prompt without bumping
  `ClassifierVersion`. Mixed-version results in the same partition
  pollute analytics.

## Related

- ADR-0036: Stuck-Type Ontology
- ADR-0003: Misconception session-scope (governs retention)
- Task: [RDY-063](../../../tasks/readiness/RDY-063-stuck-type-classifier.md)
- Parent: [RDY-062](../../../tasks/readiness/RDY-062-live-assistance-teacher-first-ai-fallback.md) (paused; depends on this)
