# RDY-012: HTTP Client Circuit Breakers (LLM, OCR, Mathpix)

- **Priority**: High — single external API outage cascades to all users
- **Complexity**: Mid engineer — Polly policies
- **Source**: Expert panel audit — Dina (Architecture)
- **Tier**: 2
- **Effort**: 1 week

## Problem

`EmbeddingService`, `GeminiOcrClient`, `MathpixClient` are registered as `HttpClient` instances but have no Polly policies, no retries, no circuit breakers, no fallback. If any external API goes down or becomes slow, requests block indefinitely.

The platform already has circuit breaker patterns for Redis, SymPy, and LLM actors — but HTTP clients were missed.

## Scope

### 1. Add Polly policies to all external HTTP clients

For each client (`EmbeddingService`, `GeminiOcrClient`, `MathpixClient`, any LLM HTTP client):
- **Timeout**: 10 seconds per request
- **Retry**: 2 retries with exponential backoff (1s, 2s)
- **Circuit breaker**: Open after 3 failures in 30 seconds, half-open after 60 seconds
- **Fallback**: Return graceful error (not exception) when circuit is open

### 2. Emit circuit breaker metrics

- Export CB state (closed/open/half-open) via OpenTelemetry gauge
- Log state transitions with `[CIRCUIT_BREAKER]` structured tag

### 3. Register in health aggregator

- Health aggregator should poll HTTP client CB states (not just Redis CB)

## Files to Modify

- `src/api/Cena.Student.Api.Host/Program.cs` — add Polly policies on `AddHttpClient<T>()` calls
- `src/actors/Cena.Actors.Host/Program.cs` — same for actor-hosted HTTP clients
- New: `src/shared/Cena.Infrastructure/Resilience/HttpPolicies.cs` — shared policy factory

## Acceptance Criteria

- [ ] All external HTTP clients have timeout + retry + circuit breaker policies
- [ ] Circuit breaker state exported as OTel gauge
- [ ] CB state transitions logged with `[CIRCUIT_BREAKER]` tag
- [ ] Health aggregator monitors HTTP client CB states
- [ ] Fallback returns structured error (not exception) when circuit open
