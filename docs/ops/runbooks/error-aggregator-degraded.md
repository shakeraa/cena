# Runbook — Error Aggregator Degraded (RDY-064)

**Applies to**: admin-api, student-api, actor-host
**Alert**: `cena.error_aggregator.backend != "sentry"` on a prod host
**Owner**: on-call engineer
**Related**: `tasks/readiness/RDY-064-error-aggregator.md`

## What this alert means

Every .NET host exposes its error-aggregator backend via `/health`. On a
production host that value should match what the deploy config pinned
(e.g. `sentry` or `appinsights`). If the health probe reports
`backend=null` on a production host, one of four things has happened:

1. The aggregator config was never set (`ErrorAggregator:Enabled=false`
   or absent) — most likely on a fresh deploy that inherited dev defaults.
2. The DSN / connection string is missing from the secret store, so the
   host fell back to the Null aggregator at startup.
3. The configured backend (e.g. `sentry`) is set but the concrete
   implementation is not yet wired in this build — the fallback
   `NullErrorAggregator` is in place per the RDY-064 ADR gate.
4. A bad config value was supplied (unknown backend name) — logged as a
   warning at startup; host ran with Null rather than throwing.

In all four cases, **the host is still serving traffic**. Unhandled
exceptions are still captured by `GlobalExceptionMiddleware`, logged
through Serilog, and surfaced as RFC 9457 responses to the caller. What
is missing is **grouped + alerted exception visibility** — on-call
cannot see "this new exception type fired 47× in the last 10 min" until
the aggregator is restored.

## Severity

- **Dev / staging**: SEV-3 — expected, no action required if config
  reflects intent (e.g. `Enabled=false` in dev).
- **Production**: SEV-2 — no loss of data, but incident response time
  increases until aggregator recovers.

## Immediate triage

1. Check the host's `/health` endpoint for the `errorAggregator` section:
   ```
   curl -s https://<host>/health | jq '.errorAggregator'
   ```
   Expected on prod: `{"backend":"sentry","enabled":true}` (once the
   RDY-064 ADR lands and the Sentry impl is wired). Until then: `null`
   with `enabled=false` is the documented baseline.

2. Check the host startup logs for the registration warning:
   ```
   kubectl logs <pod> | grep "ErrorAggregator:Backend"
   ```
   Look for `ErrorAggregator:Backend=... is configured but ...` or
   `... is not recognised. Falling back to NullErrorAggregator.`

3. Check the deploy config:
   ```
   kubectl get configmap cena-<host>-config -o yaml | grep -A5 ErrorAggregator
   ```
   And the secret:
   ```
   kubectl get secret cena-<host>-secrets -o yaml | grep ErrorAggregator__Dsn
   ```

## Remediation

### Case A — config drift

Set the missing values in the configmap / secret:

```yaml
# configmap
ErrorAggregator:
  Enabled: true
  Backend: sentry
  Environment: production
  Release: ${CENA_GIT_SHA}
```

```yaml
# secret
ErrorAggregator__Dsn: https://<project-key>@<sentry-host>/<project-id>
```

Roll the pods (`kubectl rollout restart deploy/cena-<host>`) and verify
the health probe reflects the new backend.

### Case B — DSN missing

Retrieve the DSN from the ops password store and add it to the secret
under `ErrorAggregator__Dsn`. Roll the pods.

### Case C — backend gated on ADR

If production config specifies `Backend: sentry` but the build does not
yet carry the Sentry SDK registration, the fallback logs:

```
ErrorAggregator:Backend=sentry is configured but the concrete implementation
is gated on RDY-064 ADR. Falling back to NullErrorAggregator.
```

This is expected prior to the ADR landing. Escalate: note the log line on
the incident ticket, but do not page. The data loss is "no grouped
aggregation" — standard Serilog logs + OTel traces remain intact.

### Case D — unknown backend value

Typo in the configmap. Fix and roll.

## What is NOT degraded

- Serilog logs flow normally to whatever sink the cluster configures.
- OpenTelemetry traces flow to the OTLP collector.
- `GlobalExceptionMiddleware` still maps exceptions to RFC 9457 responses.
- The `/health` endpoint still returns the per-dependency health view.
- The audit-event stream continues to capture admin actions.

## Post-incident

- If the recovery required a config change, verify `prod-env.yml` in
  `infra/deploy/` matches what you applied so the next deploy does not
  regress.
- File a follow-up on RDY-064 if you discovered a gap that the scaffold
  did not cover (e.g. a code path that calls `Console.Error.Write` and
  bypasses the aggregator). The architecture-test guard for that is
  listed as a §Acceptance criterion in the task doc.

## References

- Task: `tasks/readiness/RDY-064-error-aggregator.md`
- Scaffold code: `src/shared/Cena.Infrastructure/Observability/ErrorAggregator/`
- Related runbook: `docs/ops/runbooks/incident-response.md`
