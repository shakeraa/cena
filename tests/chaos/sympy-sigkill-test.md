# SymPy Sidecar SIGKILL Chaos Test — Runbook (RDY-052)

This doc is the scaffold for `tests/chaos/SymPyKillTest.cs` (not yet a full C# integration test — that is tracked in RDY-052 to avoid bloating `Cena.Actors.Tests`).

## Scenario

1. Start the local docker-compose stack (Admin API + SymPy sidecar + NATS + Redis + Postgres).
2. Drive 50 concurrent `POST /api/admin/questions` requests through a client loop.
3. Mid-stream (after ~15 seconds), SIGKILL the SymPy sidecar container:
   ```bash
   docker exec cena-sympy-sidecar kill -9 1 || docker kill cena-sympy-sidecar
   ```
4. Continue firing requests.

## Expected behaviour

- Within **10 seconds** of the SIGKILL, the CAS circuit breaker opens (`cena_cas_circuit_breaker_state{engine="sympy"} = 2`).
- All in-flight requests that arrived after the breaker opened must return **200 OK** with `CasGateOutcome.CircuitOpen` + `QuestionCasBinding.Status = Unverifiable`.
- **Zero 5xx responses** — the breaker short-circuits before the upstream timeout.
- No `QuestionState` stream is persisted as `Verified` without a passing gate (ADR-0002).

## Verification commands

```bash
# Breaker state over time
curl -s http://localhost:9090/api/v1/query_range?query=cena_cas_circuit_breaker_state \
  --data-urlencode "start=..." --data-urlencode "end=..." --data-urlencode "step=5s"

# Outcome mix (must show CircuitOpen dominating after the kill)
curl -s http://localhost:9090/api/v1/query \
  --data-urlencode 'query=sum by (result)(rate(cena_cas_verification_total[30s]))'
```

## Recovery check

5. Restart the sidecar:
   ```bash
   docker-compose restart sympy-sidecar
   ```
6. Breaker must transition closed within 2× `HalfOpenAfter` windows.
7. New authoring requests must produce `CasGateOutcome.Verified` bindings again.

## Promotion to `.github/workflows/backend-nightly.yml`

Nightly CI should:
1. Bring up docker-compose.
2. Run this scenario.
3. Parse Prometheus metrics for the assertions above.
4. Fail the workflow if any assertion misses.

A full C# integration test wrapping the above (`SymPyKillTest.cs`) remains the RDY-052 deliverable.
