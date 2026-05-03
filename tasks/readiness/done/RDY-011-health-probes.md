# RDY-011: Health Probes Check Real Dependencies

- **Priority**: High — K8s can't route correctly without real health checks
- **Complexity**: Mid engineer
- **Source**: Expert panel audit — Dina (Architecture), Oren (API), Ran (Security)
- **Tier**: 2
- **Effort**: 2 days

## Problem

Student API and Admin API `/health/ready` endpoints return OK without checking PostgreSQL, Redis, or NATS connectivity. Kubernetes will route traffic to pods that can't reach their database.

The Actor Host correctly checks Proto.Actor cluster membership — but the API hosts that serve users check nothing.

## Scope

### 1. Student API readiness probe

Check: PostgreSQL (Marten), Redis, NATS connectivity. Return 503 if any critical dependency is down.

### 2. Admin API readiness probe

Check: PostgreSQL (Marten), Redis. Return 503 if any critical dependency is down.

### 3. Dependency prioritization

- PostgreSQL down → 503 (critical)
- Redis down → 503 (rate limiting disabled = security risk)
- NATS down → degraded but 200 (REST still works, real-time features disabled)

### 4. Configurable timeout

Health check queries should timeout at 3 seconds. Slow dependency = unhealthy.

## Files to Modify

- `src/api/Cena.Student.Api.Host/Program.cs` — add dependency health checks
- `src/api/Cena.Admin.Api/Program.cs` — add dependency health checks
- New: `src/shared/Cena.Infrastructure/Health/DependencyHealthChecks.cs`

## Acceptance Criteria

- [ ] Student API `/health/ready` checks PostgreSQL, Redis, NATS
- [ ] Admin API `/health/ready` checks PostgreSQL, Redis
- [ ] 503 returned when critical dependency is down
- [ ] Health check queries timeout at 3 seconds
- [ ] Liveness probe remains simple (process alive check)
