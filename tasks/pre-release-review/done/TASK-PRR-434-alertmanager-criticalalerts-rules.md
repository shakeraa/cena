# TASK-PRR-434: Alertmanager + the 6 `CriticalAlert` records as live Prometheus rules

**Priority**: P0
**Effort**: M (1-2 weeks)
**Lens consensus**: persona-sre
**Source docs**: [src/shared/Cena.Infrastructure/Observability/ObservabilityConfiguration.cs:42-82](../../src/shared/Cena.Infrastructure/Observability/ObservabilityConfiguration.cs#L42-L82) — the 6 `CriticalAlert` records
**Assignee hint**: kimi-coder + SRE review
**Tags**: source=observability-audit-2026-04-22, epic=epic-prr-l, priority=p0, alerting, sre
**Status**: Blocked on [PRR-433](TASK-PRR-433-prometheus-scrape-completeness.md) (alerts need real scrape targets to have real expressions)
**Tier**: launch
**Epic**: [EPIC-PRR-L](EPIC-PRR-L-observability-completion.md)

---

## Why

[ObservabilityConfiguration.cs](../../src/shared/Cena.Infrastructure/Observability/ObservabilityConfiguration.cs) defines 6 `CriticalAlert` records:

| AlertId | Description | Severity | Window |
|---|---|---|---|
| `ALERT-CAS-001` | SymPy sidecar down | critical | 2 min |
| `ALERT-COST-001` | Daily LLM cost exceeded | warning | 1 hr |
| `ALERT-ERR-001` | API error rate > 5% | critical | 5 min |
| `ALERT-QUEUE-001` | NATS queue depth > 100 | warning | 5 min |
| `ALERT-EXAM-001` | Exam integrity violation | critical | 1 min |
| `ALERT-CSAM-001` | CSAM detection | critical | 30 sec |

These are C# records. They describe *what* should alert and *when*. **They do not fire.** There is no `alerts.yml`, no Alertmanager, no notification route. On-call is blind unless someone manually watches a dashboard tile. That is the 03:00-Bagrut-morning failure mode: student-api starts erroring at 5.8%, the alert condition is "error rate > 5%", and nothing happens because the rule only lives in C# and the alert-manager service doesn't exist.

The root cause is that alerts were designed-as-code (the right instinct) but never wired through to the runtime pipeline. This task closes the loop: every code-defined alert becomes a live rule, and a reconciliation test prevents the code and the rules from drifting again.

## How

### Alertmanager deployment

Add Alertmanager service to `config/docker-compose.observability.yml`:

- Image: `prom/alertmanager:latest`.
- Config mounted from `config/alertmanager.yml`.
- Expose port 9093 for UI + silence management.
- Persistent volume for silences + notification log.

Configure Prometheus to send alerts to Alertmanager:

```yaml
# config/prometheus.yml (addition)
alerting:
  alertmanagers:
    - static_configs:
        - targets: ["alertmanager:9093"]
rule_files:
  - "alerts.yml"
```

### Alerting rules file

`config/alerts.yml` — one PromQL rule per `CriticalAlert` record:

```yaml
groups:
  - name: cena-critical
    interval: 15s
    rules:
      - alert: CasSidecarDown
        expr: up{service="sympy-sidecar"} == 0
        for: 2m
        labels:
          severity: critical
          alert_id: ALERT-CAS-001
        annotations:
          summary: "SymPy sidecar unreachable"
          description: "Step verification degraded. Runbook: docs/ops/runbooks/cas-sidecar-outage.md"
      - alert: DailyLlmCostExceeded
        expr: sum(rate(cena_llm_cost_usd_total[1h])) * 24 > $DAILY_BUDGET
        for: 1h
        labels:
          severity: warning
          alert_id: ALERT-COST-001
        # ... (similar for the other 4)
```

Each rule has `alert_id` matching the C# `CriticalAlert.AlertId`. That's the reconciliation key.

### Notification channels

`config/alertmanager.yml`:

- **Primary**: Slack webhook to `#cena-alerts` channel. All critical alerts route here immediately; warning alerts route during business hours + escalate to critical after 30 min.
- **Fallback**: email to `oncall@cena.local`.
- **PagerDuty**: explicitly NOT at Launch (no formal on-call rotation yet). Leave the Alertmanager route stub with a comment.
- Group alerts by `alert_id` + `service` to avoid notification storms during cascading failures.
- Inhibit rules: `ALERT-ERR-001` inhibits `ALERT-QUEUE-001` for the same service (if the whole service is down, queue depth is a symptom, not a new signal).

### Reconciliation test

Add `src/actors/Cena.Actors.Tests/Architecture/CriticalAlertsHaveRulesTest.cs`:

- Reads `CriticalAlerts.All` from C# (via reflection, no direct dependency on the test assembly).
- Parses `config/alerts.yml` via a YAML deserializer.
- Asserts every `AlertId` in the C# list has a corresponding `alert_id` in the YAML rule set.
- Asserts severity matches.
- Asserts `for:` duration ≥ `CriticalAlert.EvaluationWindow`.
- Fails the build if any `CriticalAlert` record is added without a rule, or vice versa.

This is the structural guarantee that the drift we have today cannot re-occur.

### Self-test alert

Add a 7th alert, `ALERT-SELFTEST-001`, that fires when a magic metric `cena_alert_selftest_active` is set to 1. The metric is exposed by a single admin endpoint `POST /api/admin/alerts/selftest` (ops-only, auth-gated). Ops runs it quarterly to prove the chain works. A CI job runs it in staging weekly. If Slack doesn't see it within 2 minutes, the notification pipeline is broken.

### Runbook

Every alert gets a runbook. `docs/ops/runbooks/alerts/<alert-id>.md`. Alertmanager annotation links to it. A template covers: what the alert means, what to check first, known false-positives, who to escalate to.

## Files

- `config/alertmanager.yml` (new).
- `config/alerts.yml` (new) — 6 rules mirroring `CriticalAlerts` + 1 self-test.
- `config/prometheus.yml` — add `alerting:` block + `rule_files:` reference.
- `config/docker-compose.observability.yml` — add `alertmanager` service.
- `src/actors/Cena.Actors.Tests/Architecture/CriticalAlertsHaveRulesTest.cs` (new).
- `src/api/Cena.Admin.Api.Host/Endpoints/AlertSelfTestEndpoint.cs` (new; auth-gated).
- `src/shared/Cena.Infrastructure/Observability/AlertSelfTestMetric.cs` (new).
- `docs/ops/runbooks/alerts/ALERT-CAS-001.md` through `ALERT-CSAM-001.md` + self-test (7 new runbook files).
- `docs/ops/runbooks/observability-operations.md` — extend with "silence an alert" + "add a new alert" sections.

## Definition of Done

- `docker-compose -f config/docker-compose.observability.yml up` boots Prometheus + Alertmanager healthy.
- `curl http://alertmanager:9093/api/v2/status` returns healthy with 0 active alerts after startup.
- Reconciliation test passes: `dotnet test --filter CriticalAlertsHaveRulesTest` is green.
- Self-test alert: triggering `POST /api/admin/alerts/selftest` in Dev results in a Slack notification within 2 minutes. Verified by screenshot in PR.
- Each of the 6 `CriticalAlert` records has a runbook at `docs/ops/runbooks/alerts/<alert-id>.md` + an Alertmanager annotation linking to it.
- Inhibit rules tested: manually firing both `ALERT-ERR-001` + `ALERT-QUEUE-001` simultaneously results in one notification, not two.
- Full `Cena.Actors.sln` builds cleanly.
- No new test-only code paths in production source.

## Non-negotiable references

- Memory "No stubs — production grade" — no test-only alerts; self-test routes through the real pipeline.
- Memory "Senior Architect mindset" — reconciliation prevents drift.
- Memory "Honest not complimentary" — if an alert fires, the runbook must describe what to actually do, not hand-wave.
- Memory "Full sln build gate".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + self-test Slack screenshot URL + reconciliation-test output>"`

## Related

- EPIC-PRR-L.
- PRR-433 — scrape completeness (provides metrics the rules read).
- PRR-435 — OTel Collector pipelines (alerts may read OTLP-sourced metrics once that pipeline lands).
- PRR-436 — dashboards surface the alert state visually.
