# ACT-009: Per-Model LLM Circuit Breakers

**Priority:** P1 — blocks LLM resilience
**Blocked by:** ACT-001 (Cluster Bootstrap), LLM-001 (ACL)
**Estimated effort:** 1 day
**Contract:** `contracts/actors/supervision_strategies.cs` (ActorCircuitBreaker)

---

## Context

Each LLM model (Kimi K2 Turbo, Kimi K2 0905, Kimi K2.5, Claude Sonnet, Claude Opus) gets an independent circuit breaker. A Kimi outage must not cascade to Claude. Thresholds: 5 failures in 30 seconds -> open for 60 seconds. Half-open allows one probe call.

## Subtasks

### ACT-009.1: Circuit Breaker Registry

**Files to create/modify:**
- `src/Cena.Actors/Infrastructure/CircuitBreakerRegistry.cs`

**Acceptance:**
- [ ] One `ActorCircuitBreaker` instance per `ModelTier` (5 total)
- [ ] Registry injectable via DI
- [ ] Each breaker independently configurable (threshold, window, open duration)
- [ ] State queryable: `GetCircuitState(ModelTier) -> Closed|Open|HalfOpen`

**Test:**
```csharp
[Fact]
public void Registry_HasIndependentBreakers()
{
    _registry.GetBreaker(ModelTier.KimiFast).RecordFailure(); // Trip Kimi
    Assert.Equal(CircuitState.Closed, _registry.GetBreaker(ModelTier.Sonnet).State); // Claude unaffected
}
```

---

### ACT-009.2: Integration with LLM Router

**Files to create/modify:**
- `src/llm_acl/routing/circuit_breaker_router.py`

**Acceptance:**
- [ ] Router checks circuit state before routing to model
- [ ] Open circuit -> skip to next model in fallback chain
- [ ] All circuits open -> raise `CircuitOpenError`
- [ ] Half-open -> allow one probe request, close on success, reopen on failure

**Test:**
```python
def test_router_skips_open_circuit():
    registry = CircuitBreakerRegistry()
    registry.trip("kimi_standard")
    config = router.route(TaskType.ERROR_CLASSIFICATION)
    assert config.model_tier != ModelTier.KIMI_STANDARD
```

---

### ACT-009.3: Telemetry + Auto-Recovery

**Files to create/modify:**
- `src/Cena.Actors/Infrastructure/CircuitBreakerRegistry.cs` (metrics)

**Acceptance:**
- [ ] Metrics: `cena.circuit_breaker.trips_total`, `cena.circuit_breaker.rejections_total`, `cena.circuit_breaker.resets_total`
- [ ] Alert: circuit open > 5 minutes -> WARNING to Slack
- [ ] Dashboard: circuit state per model as color-coded indicator

**Test:**
```csharp
[Fact]
public void Metrics_RecordTrip()
{
    var breaker = new ActorCircuitBreaker("test", _logger);
    for (int i = 0; i < 5; i++) breaker.RecordFailure();
    Assert.Equal(CircuitState.Open, breaker.State);
    // Verify metric was emitted
}
```

---

## Rollback Criteria
- Disable circuit breakers, allow all requests through (risk: cascading failure)

## Definition of Done
- [ ] 5 independent circuit breakers operational
- [ ] Kimi failure does not affect Claude
- [ ] Metrics exported to Grafana
- [ ] PR reviewed by architect
