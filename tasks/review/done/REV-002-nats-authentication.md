# REV-002: Add NATS Authentication, TLS, and Subject ACLs

**Priority:** P0 -- BLOCKER (student learning events flow over unauthenticated plaintext)
**Blocked by:** None (development environment, no external secrets manager required)
**Blocks:** Production deployment, FERPA compliance
**Estimated effort:** 3 days
**Source:** System Review 2026-03-28 -- Cyber Officer 2 (F-NATS-01/02/03), Solution Architect (NATS section)
**Related:** INF-011 (NATS Account-Based Authorization) -- this task is the minimum viable subset

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

NATS currently runs with zero authentication. Any process on the Docker network can connect to port 4222 and publish commands like `cena.session.start` or subscribe to `cena.events.>` -- reading all student learning events. The monitoring port 8222 also exposes server stats without auth. For an education platform processing student PII under FERPA, this is a critical compliance gap.

## Architect's Decision

Full account-based authorization (INF-011) is the production target. This task implements the **minimum viable security**: token-based authentication with per-service users and basic publish/subscribe permissions. This unblocks development while INF-011 builds the full account topology.

Use NATS static configuration (`nats.conf`) rather than JWT/NKey for development simplicity. Production will upgrade to decentralized auth per INF-011.

## Subtasks

### REV-002.1: Create NATS Server Configuration with Auth

**Files to create:**
- `config/nats/nats-dev.conf` -- development NATS server config with auth

**Config structure:**
```
# config/nats/nats-dev.conf
port: 4222
http_port: 8222

authorization {
  users = [
    { user: "actor-host",  password: "$NATS_ACTOR_PASSWORD",  permissions: {
      publish: ["cena.events.>", "cena.system.>"]
      subscribe: ["cena.session.>", "cena.mastery.>"]
    }}
    { user: "admin-api",   password: "$NATS_API_PASSWORD",    permissions: {
      publish: []
      subscribe: ["cena.events.>"]
    }}
    { user: "emulator",    password: "$NATS_EMU_PASSWORD",    permissions: {
      publish: ["cena.session.>", "cena.mastery.>", "cena.events.focus.>"]
      subscribe: ["cena.events.>"]
    }}
    { user: "nats-setup",  password: "$NATS_SETUP_PASSWORD",  permissions: {
      publish: ["$JS.API.>"]
      subscribe: ["$JS.API.>"]
    }}
  ]
}

jetstream {
  store_dir: /data
  max_mem: 256MB
  max_file: 10GB
}
```

**Acceptance:**
- [ ] NATS server starts with auth enabled
- [ ] Unauthenticated connection is rejected with `Authorization Violation`
- [ ] Each service user can only pub/sub on their permitted subjects
- [ ] Monitoring port 8222 is NOT exposed to host in docker-compose (internal only)

### REV-002.2: Update Docker Compose

**File to modify:** `docker-compose.yml`

**Changes:**
```yaml
nats:
  image: nats:2.10-alpine
  command: ["-c", "/etc/nats/nats-dev.conf"]
  volumes:
    - ./config/nats/nats-dev.conf:/etc/nats/nats-dev.conf:ro
    - nats_data:/data
  ports:
    - "4222:4222"
    # REMOVE: - "8222:8222"  (monitoring no longer host-exposed)
  environment:
    - NATS_ACTOR_PASSWORD=${NATS_ACTOR_PASSWORD:-dev_actor_pass}
    - NATS_API_PASSWORD=${NATS_API_PASSWORD:-dev_api_pass}
    - NATS_EMU_PASSWORD=${NATS_EMU_PASSWORD:-dev_emu_pass}
    - NATS_SETUP_PASSWORD=${NATS_SETUP_PASSWORD:-dev_setup_pass}
```

**Acceptance:**
- [ ] Port 8222 is no longer host-mapped
- [ ] NATS passwords injected via environment variables with dev defaults
- [ ] `docker compose up` works without manual config

### REV-002.3: Update .NET NATS Connections with Credentials

**Files to modify:**
- `src/actors/Cena.Actors.Host/Program.cs` -- NatsOpts for actor host
- `src/api/Cena.Api.Host/Program.cs` -- NatsOpts for admin API
- `src/emulator/Program.cs` -- NatsConnection for emulator
- `src/infra/docker/nats-setup.sh` -- setup script credentials

**Pattern:**
```csharp
// Actor Host
var natsUser = builder.Configuration["Nats:User"] ?? "actor-host";
var natsPass = builder.Configuration["Nats:Password"]
    ?? Environment.GetEnvironmentVariable("NATS_ACTOR_PASSWORD")
    ?? "dev_actor_pass";

var opts = new NatsOpts
{
    Url = natsUrl,
    Name = "cena-actors-host",
    AuthOpts = new NatsAuthOpts { Username = natsUser, Password = natsPass },
    MaxReconnectRetry = -1,         // REV-008 reconnection
    ReconnectWaitMs = 2000,
    ReconnectJitterMs = 1000,
};
```

**Acceptance:**
- [ ] Actor Host connects with `actor-host` user credentials
- [ ] Admin API connects with `admin-api` user credentials
- [ ] Emulator connects with `emulator` user credentials
- [ ] nats-setup.sh connects with `nats-setup` user credentials
- [ ] All connections fail fast with clear error if password is wrong
- [ ] `appsettings.json` does NOT contain plaintext passwords (use env vars)

### REV-002.4: Update nats-setup.sh to Use Credentials

**File to modify:** `src/infra/docker/nats-setup.sh`

**Change:** Replace `nats://nats:4222` with `nats://nats-setup:${NATS_SETUP_PASSWORD}@nats:4222`

**Acceptance:**
- [ ] `nats-setup` container creates all 9 JetStream streams with authentication
- [ ] Stream creation is idempotent (re-running does not fail)

### REV-002.5: Verify Subject Isolation

**Test script to create:** `scripts/test-nats-auth.sh`

```bash
#!/bin/bash
# Verify that emulator cannot publish to system subjects
nats pub cena.system.health.test "unauthorized" \
  --user emulator --password "$NATS_EMU_PASSWORD" \
  --server nats://localhost:4222 2>&1 | grep -q "Permissions Violation"
echo "PASS: Emulator cannot publish to system subjects"

# Verify that admin-api cannot publish commands
nats pub cena.session.start '{"test":true}' \
  --user admin-api --password "$NATS_API_PASSWORD" \
  --server nats://localhost:4222 2>&1 | grep -q "Permissions Violation"
echo "PASS: Admin API cannot publish session commands"
```

**Acceptance:**
- [ ] Emulator cannot publish to `cena.system.>` or `cena.events.>` (only commands)
- [ ] Admin API cannot publish to any subject (read-only subscriber)
- [ ] Actor Host cannot subscribe to command subjects it shouldn't process
