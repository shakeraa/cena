# REV-010: Remove Hardcoded Credentials & Establish Configuration Pattern

**Priority:** P1 -- HIGH (dev passwords compiled into binaries, `Include Error Detail=true` leaks SQL)
**Blocked by:** None
**Blocks:** Production configuration
**Estimated effort:** 4 hours
**Source:** System Review 2026-03-28 -- Cyber Officer 1 (Finding 1), Backend Senior (I1), DevOps Engineer (Finding #4)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

The fallback connection string `Host=localhost;Port=5433;Database=cena;Username=cena;Password=cena_dev_password;Include Error Detail=true` appears as an inline `??` default in 5 source files. This means:
- The password is compiled into release binaries
- `Include Error Detail=true` leaks SQL query details in error responses
- Violates CLAUDE.md: "NEVER hardcode API keys, secrets, or credentials in source files"

## Architect's Decision

1. **Extract to a single constant** in shared infrastructure for development use only
2. **Fail fast in non-development** if connection string is not provided via configuration
3. **Remove `Include Error Detail=true`** from all connection strings; only enable it via `appsettings.Development.json`
4. **Create `.env.example`** documenting all required environment variables

## Subtasks

### REV-010.1: Create Shared Configuration Helper

**File to create:** `src/shared/Cena.Infrastructure/Configuration/CenaConnectionStrings.cs`

```csharp
namespace Cena.Infrastructure.Configuration;

public static class CenaConnectionStrings
{
    /// <summary>
    /// Resolves PostgreSQL connection string from configuration.
    /// Falls back to dev default ONLY in Development environment.
    /// Throws in non-Development if not configured.
    /// </summary>
    public static string GetPostgres(IConfiguration config, IHostEnvironment env)
    {
        var connectionString = config.GetConnectionString("PostgreSQL")
            ?? config["ConnectionStrings:Marten"]
            ?? Environment.GetEnvironmentVariable("CENA_POSTGRES_CONNECTION");

        if (connectionString is not null)
            return connectionString;

        if (env.IsDevelopment())
            return "Host=localhost;Port=5433;Database=cena;Username=cena;Password=cena_dev_password";

        throw new InvalidOperationException(
            "PostgreSQL connection string not configured. " +
            "Set ConnectionStrings:PostgreSQL in appsettings or CENA_POSTGRES_CONNECTION env var.");
    }

    public static string GetRedis(IConfiguration config, IHostEnvironment env)
    {
        var connectionString = config.GetConnectionString("Redis")
            ?? Environment.GetEnvironmentVariable("CENA_REDIS_CONNECTION");

        if (connectionString is not null)
            return connectionString;

        if (env.IsDevelopment())
            return "localhost:6380,abortConnect=false,connectRetry=3";

        throw new InvalidOperationException(
            "Redis connection string not configured. " +
            "Set ConnectionStrings:Redis in appsettings or CENA_REDIS_CONNECTION env var.");
    }
}
```

### REV-010.2: Replace All Inline Fallback Strings

**Files to modify:**
- `src/actors/Cena.Actors.Host/Program.cs` (line 55)
- `src/api/Cena.Api.Host/Program.cs` (line 27)
- `src/api/Cena.Admin.Api/EmbeddingAdminService.cs` (line 32)
- `src/actors/Cena.Actors/Services/EmbeddingService.cs` (line 137)
- `src/actors/Cena.Actors/Services/PgVectorMigrationService.cs` (line 29)

**Pattern:**
```csharp
// BEFORE
var connectionString = builder.Configuration.GetConnectionString("Marten")
    ?? "Host=localhost;Port=5433;Database=cena;Username=cena;Password=cena_dev_password;Include Error Detail=true";

// AFTER
var connectionString = CenaConnectionStrings.GetPostgres(builder.Configuration, builder.Environment);
```

**Acceptance:**
- [ ] `grep -r "cena_dev_password" src/` returns zero results in non-test `.cs` files
- [ ] `grep -r "Include Error Detail=true" src/` returns zero results
- [ ] All 5 files use `CenaConnectionStrings.GetPostgres()`
- [ ] Application throws clear error in Staging/Production if connection string missing

### REV-010.3: Move Error Detail to Development-Only Config

**File to modify:** `src/actors/Cena.Actors.Host/appsettings.Development.json`

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Port=5433;Database=cena;Username=cena;Password=cena_dev_password;Include Error Detail=true"
  }
}
```

**File to modify:** `src/actors/Cena.Actors.Host/appsettings.json`

Remove the connection string from the base config (it will be required from environment in non-dev).

**Acceptance:**
- [ ] `Include Error Detail=true` only exists in `appsettings.Development.json`
- [ ] Base `appsettings.json` does not contain connection strings with passwords

### REV-010.4: Create .env.example

**File to create:** `.env.example`

```bash
# Cena Platform -- Required Environment Variables
# Copy to .env and fill in values. NEVER commit .env to git.

# PostgreSQL (Marten Event Store)
CENA_POSTGRES_CONNECTION=Host=localhost;Port=5433;Database=cena;Username=cena;Password=CHANGE_ME

# Redis
CENA_REDIS_CONNECTION=localhost:6380,abortConnect=false,connectRetry=3

# NATS
NATS_ACTOR_PASSWORD=CHANGE_ME
NATS_API_PASSWORD=CHANGE_ME
NATS_EMU_PASSWORD=CHANGE_ME

# Firebase
FIREBASE_SERVICE_ACCOUNT_KEY_PATH=/path/to/service-account-key.json

# Proto.Actor Cluster (production only)
# CLUSTER_PROVIDER=kubernetes
# CLUSTER_CONSUL_ADDRESS=http://consul:8500
```

**Acceptance:**
- [ ] `.env.example` documents all required variables
- [ ] `.env.example` contains NO real passwords (all say `CHANGE_ME`)
- [ ] `.env` is in `.gitignore`
