# INF-002: RDS PostgreSQL 16 — Multi-AZ, Snapshots, Secrets Manager Connection

**Priority:** P0 — blocks all persistence
**Blocked by:** INF-001 (VPC)
**Estimated effort:** 2 days
**Contract:** `contracts/data/marten-event-store.cs` (PostgreSQL-backed event store)

---

## Context

Marten event store requires PostgreSQL 16+ with JSONB support. Production uses Multi-AZ RDS for high availability. Connection credentials stored in AWS Secrets Manager, rotated every 90 days, injected into ECS tasks via environment variables.

## Subtasks

### INF-002.1: Terraform RDS Module

**Files to create/modify:**
- `infra/terraform/modules/rds/main.tf`
- `infra/terraform/modules/rds/variables.tf`
- `infra/terraform/modules/rds/outputs.tf`

**Acceptance:**
- [ ] PostgreSQL 16.x, `db.r6g.large` (prod), `db.t4g.micro` (dev)
- [ ] Multi-AZ enabled in prod, single-AZ in dev/staging
- [ ] Storage: 100GB gp3, auto-scaling to 500GB
- [ ] Automated backups: 7-day retention, daily snapshots at 03:00 UTC
- [ ] Point-in-time recovery enabled
- [ ] Encryption at rest via AWS KMS (customer-managed key)
- [ ] Security group: allow port 5432 from ECS tasks only
- [ ] Parameter group: `max_connections=200`, `shared_buffers=2GB`, `work_mem=64MB`
- [ ] No public accessibility

**Test:**
```bash
terraform plan -target=module.rds
# Assert: 0 errors
aws rds describe-db-instances --db-instance-identifier cena-prod   | jq '.DBInstances[0].MultiAZ'
# Assert: true
```

**Edge cases:**
- Multi-AZ failover during active sessions -> connection pool detects, retries within 30s
- Storage approaching limit -> CloudWatch alarm at 80%

---

### INF-002.2: Secrets Manager Integration

**Files to create/modify:**
- `infra/terraform/modules/rds/secrets.tf`
- `src/Cena.Actors.Host/Program.cs` — read connection string from Secrets Manager

**Acceptance:**
- [ ] Secret: `cena/rds/credentials` containing `{ host, port, dbname, username, password }`
- [ ] Automatic rotation every 90 days via Lambda rotator
- [ ] ECS task IAM role has `secretsmanager:GetSecretValue` permission
- [ ] Connection string built at startup from secret values, not hardcoded
- [ ] Secret rotation triggers connection pool refresh (no downtime)

**Test:**
```csharp
[Fact]
public async Task ConnectionString_ReadsFromSecretsManager()
{
    var connStr = await _secretsManager.GetConnectionString();
    Assert.Contains("Host=", connStr);
    Assert.DoesNotContain("password=hardcoded", connStr);
}
```

---

### INF-002.3: Marten Schema Migration

**Files to create/modify:**
- `src/Cena.Data/EventStore/MartenConfiguration.cs`
- `scripts/infra/migrate_marten.sh`

**Acceptance:**
- [ ] Marten auto-creates schema on first run (development mode)
- [ ] Production: explicit migration script (`weasel` CLI)
- [ ] Event store tables: `mt_events`, `mt_streams`, snapshot tables
- [ ] Indexes optimized for: stream lookup, event type filtering, timestamp range
- [ ] Migration tested against empty database and existing database with data

**Test:**
```bash
dotnet run --project src/Cena.Data -- migrate
# Assert: exit code 0, schema matches expected version
```

---

## Rollback Criteria
- Restore from automated snapshot (< 5 min RPO)
- Point-in-time recovery for data corruption

## Definition of Done
- [ ] RDS instance running in staging with Multi-AZ
- [ ] Secrets Manager credential rotation tested
- [ ] Marten connects and creates schema successfully
- [ ] `dotnet test --filter "Category=Database"` -> 0 failures
- [ ] PR reviewed by architect
