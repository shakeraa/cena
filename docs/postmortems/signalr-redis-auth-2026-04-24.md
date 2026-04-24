# Postmortem — Admin SignalR Redis backplane silently dropped auth password

- **Date**: 2026-04-24 ~08:17 UTC
- **Severity**: SEV-3 (dev-env only; production traffic unaffected, but
  same bug would have fired in any environment that splits
  `ConnectionStrings:Redis` from `Redis:Password`)
- **Duration**: latent (shipped 2026-03 with RDY-060 admin hub), surfaced
  today as a side effect of restarting the stack after an overnight
  Docker Desktop shutdown
- **Observer**: Shaker (user)

## What happened

Docker Desktop was shut down overnight (host sleep/shutdown). When the
stack was brought back up the next morning, the infra tier
(`cena-postgres`, `cena-redis`, `cena-nats`, `cena-emulator`) exited
with code 255, but the app tier (`admin-api`, `student-api`,
`actor-host`, `sympy-sidecar`, `postgres-backup`) was restarted by the
Docker Desktop auto-start and came up without dependencies.

Cascading startup failures masked a latent bug. Once the infra was
brought back up and the app containers restarted:

- `cena-student-api`, `cena-actor-host`, `cena-postgres-backup-1`,
  `cena-sympy-sidecar` all recovered cleanly.
- `cena-admin-api` logged:
  ```
  [08:17:37 ERR] Microsoft.AspNetCore.SignalR.StackExchangeRedis
    .RedisHubLifetimeManager: Error connecting to Redis.
  StackExchange.Redis.RedisConnectionException: ...
    NOAUTH Returned - connection has not yet authenticated
  ```

The main Redis `IConnectionMultiplexer` registered in
`Admin.Api.Host/Program.cs` connected fine — it reads
`Redis:Password` / `REDIS_PASSWORD` and sets `options.Password`
explicitly. But the SignalR backplane failed because it uses a
separate builder.

## Root cause

`Cena.Admin.Api.Host.Hubs.AdminSignalRConfiguration.BuildRedisConnectionString`
([src/api/Cena.Admin.Api.Host/Hubs/AdminSignalRConfiguration.cs:126](../../src/api/Cena.Admin.Api.Host/Hubs/AdminSignalRConfiguration.cs))
returned the `ConnectionStrings:Redis` value **verbatim** when set, and
only fell through to the REDIS_* env-var path when it was absent:

```csharp
var cs = configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(cs)) return cs;
// password merge only ran in the fallback branch below ...
```

Docker Compose provides these two settings separately:

```yaml
ConnectionStrings__Redis=cena-redis:6379
Redis__Password=${REDIS_PASSWORD:-cena_dev_redis}
```

So the method returned `"cena-redis:6379"` with no password, and
`AddStackExchangeRedis(cs, …)` passed that straight to
`ConfigurationOptions.Parse`. Redis has `requirepass` set, so
every backplane connect attempt hit `NOAUTH`.

The main app multiplexer at `Program.cs:197-215` does it correctly
— parses the connection string, then explicitly sets
`options.Password = configuration["Redis:Password"] ?? …`. The
SignalR helper was a duplicate path that forgot step two.

### Why it hadn't surfaced before

1. The admin SignalR hub only errors when it actually tries to connect
   (on first hub connection or on a background reconnect cycle). In
   dev, nobody had clicked a SignalR-backed admin page on a cold-boot
   stack with Redis auth enabled.
2. The stack is usually up continuously; today was the first full
   cold-boot of the week.
3. On earlier boots the admin-api was probably crashing on a
   Postgres/Marten dependency earlier in startup and never reached
   SignalR registration.

## Fix

Merged the password into the connection string when one is configured
separately, matching the main multiplexer's behaviour:

```csharp
var password = configuration["REDIS_PASSWORD"] ?? configuration["Redis:Password"];
var cs = configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(cs))
{
    if (string.IsNullOrEmpty(password) ||
        cs.Contains("password=", StringComparison.OrdinalIgnoreCase))
    {
        return cs;
    }
    return $"{cs},password={password}";
}
```

Docker Compose config is unchanged — the fix is in the builder.
`dotnet watch` hot-reloaded the change; admin-api reached `healthy` on
the next restart with zero `NOAUTH` errors on subsequent boots.

## Impact

- **User-facing**: none (dev env only).
- **Feature impact (if it had reached prod)**: SignalR events on the
  admin hub would have been single-replica only instead of
  Redis-broadcast. Horizontal-scaling admin-api replicas would have
  diverged in per-connection group membership. Clients would still
  connect; they'd just miss events dispatched via sibling replicas.
- **Data**: none.
- **Time lost**: ~10 minutes of debugging to find the code path.

## What worked

- The error message was specific enough (`NOAUTH Returned`) to
  short-circuit guessing and point directly at auth.
- `dotnet watch` hot-reloaded the fix without a container rebuild.
- Single-replica in-process SignalR fallback was intended for the
  *absent-config* case, not the *misconfigured* case — so the
  behaviour was a hard error rather than a silent degradation. Easier
  to catch.

## What didn't

- The `BuildRedisConnectionString` helper duplicated logic that already
  existed in `Program.cs` for the main multiplexer, but only
  implemented half of it (no password merge). Two connection paths
  maintained independently is a structural smell.
- No integration test exercises the admin SignalR hub against a
  password-protected Redis in CI. The existing tests use
  password-less dev defaults.

## Action items

1. ✅ Fix `BuildRedisConnectionString` to merge `Redis:Password`
   (shipped this session).
2. ☐ Extract Redis connection-string building into a single helper in
   `Cena.Infrastructure.Configuration` (mirror of
   `CenaConnectionStrings.GetRedis` but with password merging), and
   have both the main multiplexer and the SignalR backplane call it.
   Eliminates the duplicate-path class of bug.
3. ☐ Add an integration test that boots admin-api against a Redis
   container with `requirepass` set and asserts the SignalR backplane
   connects. CI currently uses password-less Redis fallback.
4. ☐ Sweep for other Redis consumers that parse `ConnectionStrings:Redis`
   directly without merging the password (see `grep -rn
   'GetConnectionString.*Redis' src/`).

## Related

- RDY-060 (admin SignalR hub + NATS bridge + Redis backplane, the
  feature that introduced the helper).
- `docs/postmortems/mt-events-seq-collision-2026-04-19.md` (different
  root cause; listed only because it's the other recent incident
  triggered by a Docker Desktop hang / cold boot).
