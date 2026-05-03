# TASK-PRR-438: Fix Admin API Host swagger-gen SIGABRT (pre-existing)

**Priority**: P1 — blocks OpenAPI contract generation for Admin API
**Effort**: S (1-2 days)
**Lens consensus**: persona-sre
**Source docs**: Triage run 2026-04-23 confirmed the error is pre-existing on clean `origin/main`
**Assignee hint**: kimi-coder
**Tags**: source=admin-api-build-audit-2026-04-23, priority=p1, build, openapi, pre-existing-bug
**Status**: Ready
**Tier**: launch

---

## Why

`dotnet build src/api/Cena.Admin.Api.Host/Cena.Admin.Api.Host.csproj` fails its post-build `dotnet swagger tofile` step with exit code 134 (SIGABRT) on clean `origin/main`, independent of any in-flight feature code. Triage on 2026-04-23 ran the build with all untracked changes stashed — error still fires. Build summary: `21 Warning(s), 1 Error(s)`.

```
src/api/Cena.Admin.Api.Host/Cena.Admin.Api.Host.csproj(41,5):
  error MSB3073: The command "dotnet swagger tofile --output bin/Debug/net9.0/openapi.json
  .../Cena.Admin.Api.Host/bin/Debug/net9.0/Cena.Admin.Api.Host.dll v1" exited with code 134.
```

Exit 134 = SIGABRT = unhandled exception during the process that boots the Admin API to reflect routes for the OpenAPI JSON. Compilation succeeds; the post-build reflection step crashes. Likely culprits (ordered by probability):

1. An `IHostedService` registered in Program.cs tries to connect to Postgres/Redis/NATS at startup, fails, and aborts the process instead of degrading.
2. A DI resolution throws because a singleton factory touches a null dependency during Swagger's lightweight host boot.
3. A static initializer somewhere (Marten session factory, Serilog enricher, Firebase SDK) triggers an unrecoverable error when it sees no configured external service.

The downstream consequence: `bin/Debug/net9.0/openapi.json` is not generated → `swagger-codegen` / any client that consumes the OpenAPI contract is broken → CI-generated API clients for the Admin SPA silently use stale schemas.

Per memory "Senior Architect mindset" — this is exactly the kind of silent-deviation problem that rots the dev loop until nobody trusts the build anymore. It's Launch-gating because the OpenAPI contract is the source of truth for the Admin SPA.

## How

### Step 1: Reproduce with full stack trace

Run the swagger-gen command directly with a debugger-friendly environment:

```bash
dotnet build src/api/Cena.Admin.Api.Host/Cena.Admin.Api.Host.csproj
cd src/api/Cena.Admin.Api.Host
ASPNETCORE_ENVIRONMENT=SwaggerGen dotnet swagger tofile --output bin/Debug/net9.0/openapi.json bin/Debug/net9.0/Cena.Admin.Api.Host.dll v1 2>&1 | tee /tmp/swagger-gen-crash.log
```

Capture the full stack trace. Exit-134 on Unix = SIGABRT, usually from a managed exception that was rethrown out of a finalizer or from `Environment.FailFast`.

### Step 2: Identify the offending startup code

Candidate list — check each in Program.cs:

- `AddHostedService<*>` registrations (background workers).
- `AddSingleton<*>(sp => ...)` factories that do I/O.
- Firebase / Anthropic / Stripe SDK initialization that requires env vars missing in the swagger-gen context.
- Marten document-store initialization if it attempts schema migration on construction.
- Any `IStartupFilter` that contacts an external service.

### Step 3: Gate the offending code on swagger-gen

The correct fix is NOT to hide the crash. It's to recognize that the swagger-gen host is a design-time tool and gate background-service registration on an environment variable:

```csharp
var isSwaggerGen = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "SwaggerGen"
                || builder.Configuration.GetValue<bool>("SWAGGER_GEN", false);

if (!isSwaggerGen)
{
    builder.Services.AddHostedService<TheOffendingWorker>();
}
```

Then update the csproj post-build target to set the env var:

```xml
<Target Name="GenerateSwaggerJson" AfterTargets="Build">
  <Exec Command="dotnet swagger tofile --output $(OutputPath)openapi.json $(OutputPath)$(AssemblyName).dll v1"
        EnvironmentVariables="ASPNETCORE_ENVIRONMENT=SwaggerGen" />
</Target>
```

**Not a stub**: the worker still runs in Dev + Staging + Prod. It only skips during swagger-gen, which has no business starting background workers.

### Step 4: Fix root-cause degradation too

If the worker's startup is crashing because it can't reach a dependency, that's a second bug — in Dev without dependencies, the worker should log + retry + not `FailFast`. Add resilience at the worker level so Dev-without-deps doesn't crash the host either.

### Step 5: CI regression

Add a CI step that runs the full Admin API build and asserts `bin/Debug/net9.0/openapi.json` exists + is valid JSON. If a future PR reintroduces the swagger-gen crash, CI blocks it.

## Files

- `src/api/Cena.Admin.Api.Host/Program.cs` — gate offending hosted-service registration on `SwaggerGen` env check.
- `src/api/Cena.Admin.Api.Host/Cena.Admin.Api.Host.csproj` — add `EnvironmentVariables` attribute on the swagger `<Exec>` target.
- `src/api/Cena.Admin.Api.Host/Workers/<TheOffendingWorker>.cs` — add startup resilience (log + retry, don't FailFast).
- `.github/workflows/build.yml` or equivalent — add CI step asserting `openapi.json` exists + is valid after Admin API build.
- `docs/ops/runbooks/admin-api-build.md` — document the swagger-gen env gate.

## Definition of Done

- `dotnet build src/api/Cena.Admin.Api.Host/Cena.Admin.Api.Host.csproj` completes with 0 errors on clean checkout.
- `bin/Debug/net9.0/openapi.json` exists after build, is valid JSON, and contains expected route schema.
- Offending hosted service has been identified + documented in the task PR + gated correctly.
- Worker-level resilience exists so Dev-without-Postgres doesn't crash Program.cs host startup.
- CI regression check in place; proven by a fixture PR that reintroduces the crash and gets blocked.
- `dotnet build src/actors/Cena.Actors.sln` completes with 0 errors.

## Non-negotiable references

- Memory "Senior Architect mindset" — root cause (startup crash), not cosmetic.
- Memory "No stubs — production grade" — worker gated out of swagger-gen only, still runs in Dev/Staging/Prod.
- Memory "Full sln build gate" — this task is literally closing a sln-build-gate violation.
- Memory "Honest not complimentary" — document the root-cause worker explicitly in the PR.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + stack trace + root-cause identified>"`

## Related

- Triage that surfaced this: 2026-04-23 stash + clean-main build comparison.
- Builds against [EPIC-PRR-L](EPIC-PRR-L-observability-completion.md) indirectly — an openapi.json generator that's broken means Admin SPA ↔ Admin API contract drift.
