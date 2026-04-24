# INF-021: PostgreSQL Read Replica Readiness

**Priority:** P2 — needed when admin dashboard load impacts actor event writes
**Blocked by:** INF-002 (RDS setup)
**Estimated effort:** 2 days

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

Every Marten `QuerySession()` in admin services (40+ locations) hits the primary PostgreSQL instance. Actor event writes (`LightweightSession` + `SaveChangesAsync`) compete for the same connection pool. Under peak load (200 students + 5 admins running dashboards), read queries add latency to the write path.

PostgreSQL streaming replication + Marten's read/write session routing can separate these workloads cleanly.

## Subtasks

### INF-021.1: Connection String Configuration for Replica

**Files:**
- `src/shared/Cena.Infrastructure/Configuration/CenaConnectionStrings.cs` — add `GetPostgresReadReplica()`
- `src/shared/Cena.Infrastructure/Configuration/CenaDataSourceFactory.cs` — add `AddCenaReadOnlyDataSource()`
- `appsettings.json` (both hosts) — add `ConnectionStrings:PostgreSQLReadReplica`

**Acceptance:**
- [ ] `GetPostgresReadReplica()` returns replica connection string (falls back to primary in dev)
- [ ] `AddCenaReadOnlyDataSource()` registers a second `NpgsqlDataSource` with `[FromKeyedServices("readonly")]`
- [ ] Replica pool: max 20 connections (read-only queries are lighter)
- [ ] If no replica configured: silently use primary (no code change needed in services)

### INF-021.2: Marten Read Session Routing

**Files:**
- `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` — configure read-only data source
- `src/api/Cena.Admin.Api/` — all admin services use `QuerySession` from read replica

**Acceptance:**
- [ ] Marten `StoreOptions.Advanced.DefaultTenantUsageEnabled` configured for dual data source
- [ ] `QuerySession()` routes to read replica (connection string based)
- [ ] `LightweightSession()` (writes) routes to primary
- [ ] Admin endpoints verified working against replica with <100ms replication lag
- [ ] If replica is down: Marten falls back to primary gracefully

### INF-021.3: pgvector Search on Replica

**Files:**
- `src/actors/Cena.Actors/Services/EmbeddingService.cs` — use readonly data source for search queries
- `src/api/Cena.Admin.Api/EmbeddingAdminService.cs` — use readonly data source

**Acceptance:**
- [ ] Vector similarity search (`SearchByVectorAsync`) reads from replica
- [ ] Embedding inserts (`StoreEmbeddingAsync`) write to primary
- [ ] Admin corpus stats queries read from replica

### INF-021.4: Terraform RDS Read Replica

**Files:**
- `infra/terraform/modules/rds/read-replica.tf` — new: RDS read replica

**Acceptance:**
- [ ] Same instance class as primary (or one tier smaller for cost)
- [ ] Same VPC, different AZ for redundancy
- [ ] Replication lag monitoring alarm: alert if lag > 5 seconds
- [ ] Security group: allows access from ECS tasks only

## Definition of Done
- [ ] `dotnet build` + `dotnet test` pass
- [ ] Admin dashboard queries route to replica (verified via pg_stat_activity)
- [ ] Actor event writes unaffected by admin query load
- [ ] Fallback to primary works when replica is unavailable
