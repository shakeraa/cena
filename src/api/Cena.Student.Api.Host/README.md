# Cena.Student.Api.Host

Student-facing API host for the Cena adaptive learning platform.

## Scope

This host will eventually own:
- **Student REST API endpoints** — session lifecycle, progress tracking, concept exploration
- **SignalR real-time hub** — tutoring conversations, live session events
- **Student analytics** — focus scores, mastery tracking, engagement metrics

## Current Status

**DB-06 Phase 1 (Scaffold)** — Skeleton project structure only.

- ✅ Health check endpoints (`/health/live`, `/health/ready`)
- ✅ CORS configuration
- ✅ Serilog logging
- ⏳ REST endpoints (migrate from `Cena.Api.Host` in DB-06b)
- ⏳ SignalR hub wiring (migrate from `Cena.Api.Host` in DB-06b)
- ⏳ Firebase auth middleware
- ⏳ Rate limiting
- ⏳ Marten/PostgreSQL integration
- ⏳ Redis connection
- ⏳ NATS event subscriber

## Phase 2 (DB-06b) TODO

1. Migrate student endpoints from `Cena.Api.Host`:
   - `MapSessionEndpoints()` → student session lifecycle
   - `MapStudentAnalyticsEndpoints()` → progress/analytics
   - `MapCenaHub()` → SignalR tutoring hub

2. Add middleware pipeline:
   - Firebase auth + authorization
   - Token revocation
   - FERPA audit logging
   - Rate limiting
   - Correlation ID
   - Global exception handler

3. Wire infrastructure:
   - Marten document store
   - Redis connection multiplexer
   - NATS event bus

4. Configuration:
   - CORS allowed origins (student web app)
   - Rate limit policies
   - OpenTelemetry exporters

## References

- Task tracking: DB-06b (endpoint migration)
- Original host: `../Cena.Api.Host/Program.cs`
- Contracts library: `../Cena.Api.Contracts/`
