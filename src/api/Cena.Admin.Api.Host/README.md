# Cena.Admin.Api.Host

Admin-facing API host for the Cena adaptive learning platform.

## Scope

This host will eventually own:
- **Admin REST API endpoints** — user/role management, content moderation, system settings
- **Content ingestion pipeline** — OCR, normalization, quality gating
- **Analytics dashboards** — student insights, focus tracking, mastery reporting
- **Question bank management** — CRUD operations, versioning, publishing

## Current Status

**DB-06 Phase 1 (Scaffold)** — Skeleton project structure only.

- ✅ Health check endpoints (`/health/live`, `/health/ready`)
- ✅ CORS configuration
- ✅ Serilog logging
- ⏳ REST endpoints (migrate from `Cena.Api.Host` in DB-06b)
- ⏳ Admin service registration (`AddCenaAdminServices()`)
- ⏳ Firebase auth middleware
- ⏳ Rate limiting
- ⏳ Marten/PostgreSQL integration
- ⏳ Redis connection
- ⏳ NATS event subscriber

## Phase 2 (DB-06b) TODO

1. Migrate admin endpoints from `Cena.Api.Host`:
   - `MapCenaAdminEndpoints()` — all admin REST endpoints
   - `MapComplianceEndpoints()` — FERPA compliance

2. Add service registration:
   - Wire `Cena.Admin.Api` project reference
   - `builder.Services.AddCenaAdminServices()`
   - `IFirebaseAdminService` singleton

3. Add middleware pipeline:
   - Firebase auth + authorization
   - Token revocation
   - FERPA audit logging
   - Rate limiting
   - Correlation ID
   - Global exception handler
   - Concurrency conflict handler

4. Wire infrastructure:
   - Marten document store
   - Redis connection multiplexer
   - NATS event bus + subscriber
   - OpenTelemetry tracing/metrics

5. Configuration:
   - CORS allowed origins (admin web app)
   - Rate limit policies (general, AI, destructive)
   - Prometheus scraping endpoint

## References

- Task tracking: DB-06b (endpoint migration)
- Original host: `../Cena.Api.Host/Program.cs`
- Admin API library: `../Cena.Admin.Api/`
- Contracts library: `../Cena.Api.Contracts/`
