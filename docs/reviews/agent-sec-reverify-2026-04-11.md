---
agent: sec
lens: Security, Auth, Infra & Observability
run: cena-review-v2-reverify
date: 2026-04-11
worker: claude-subagent-sec
base_commit: cc3f70230f610d8564037779a68fb724de627d27
prior_findings_highest: FIND-sec-007
new_findings_start_at: FIND-sec-008
---

# Agent `sec` — Re-verification Findings (2026-04-11)

## Scope

Re-audit the Cena .NET stack (Admin API host, Student API host, Actor host)
for: missing `[Authorize]`/`RequireAuthorization` on state-mutating endpoints,
tenant-id sourced from outside a verified JWT claim, Firebase ID-token
verification, SignalR hub auth, hard-coded secrets, CORS wildcards, missing
rate limits on AI/LLM endpoints, raw-SQL injection vectors, **observability
on the error paths of every closed v1 finding**, and the REV-001 Firebase
service-account key rotation status.

Per the v2 prompt the seven previously closed `FIND-sec-001..007` findings
are NOT re-discovered (preflight already verified them).

## Live curl evidence — BLOCKED, downgraded to static-only

The Admin and Student API hosts could not be brought up against the local
infra:

```text
$ lsof -i -P -n | grep LISTEN | grep -E ':(5050|5051|5052|5119|4222|5432|5433|6380)'
(no matches for 4222 NATS, 5432/5433 Postgres, 6380 Redis)

$ docker compose ps          # in /Users/shaker/edu-apps/cena
NAME      IMAGE     COMMAND   SERVICE   CREATED   STATUS    PORTS
(empty — stack not running)

$ DOTNET_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5052 \
    dotnet bin/Debug/net9.0/Cena.Admin.Api.Host.dll
[INF] NATS.Client.Core.INatsConnection: Configuring NATS connection to nats://localhost:4222 as cena_api_user
[ERR] Microsoft.Extensions.Hosting.Internal.Host: BackgroundService failed
NATS.Client.Core.NatsException: can not connect uris: nats://localhost:4222
...
Unhandled exception. System.Threading.Tasks.TaskCanceledException
   at Cena.Admin.Api.CulturalContextSeeder.StartAsync(...)  # Postgres also down
```

Postgres + NATS + Redis are all unreachable on this workstation. The build
itself succeeded (`dotnet build` returns `0 Error(s)`), so the binaries are
sound — the Admin and Student API hosts physically refuse to boot without
infra because the seeder + the `NatsEventSubscriber` BackgroundService both
hard-fail on first connect. Root commit `cc3f702`.

**Coordinator note**: this matches the Phase 0 preflight observation that
hosts cannot be probed live until `docker compose up` is run. All findings
below are evidenced by `grep` + file:line reads on `cc3f702`. Per the v2
prompt, `grep` output with file:line is an accepted evidence type.

## Observability matrix (v2 expanded scope)

For every closed v1 sec finding, does the fix path emit a structured log,
expose a metric, or have an alerting rule for the regression class?

| FIND-sec-* | Bug class             | Structured log on err path | Custom metric | Alert rule |
|------------|-----------------------|----------------------------|---------------|------------|
| sec-001    | Raw SQL interpolation in LeaderboardService | Yes — `_logger.LogError(ex, "Error querying ... leaderboard")` at `src/shared/Cena.Infrastructure/Gamification/LeaderboardService.cs:114,164,214,252,270,290,314` | None — no SQLi-specific counter | None — no rule for "leaderboard SQL parse failure" |
| sec-002    | `AllowAnyOrigin` scaffold hosts | n/a — fix removed the wildcard. CI gate not present. | None | None — no alert if `AllowAnyOrigin` re-introduced |
| sec-003    | NATS dev-password fallback in non-Dev | Indirect — `CenaNatsOptions.GetApiAuth` throws InvalidOperationException in non-Dev (`src/shared/Cena.Infrastructure/Configuration/CenaNatsOptions.cs:55-58`) which surfaces through `ApplicationStarted` startup failure logging. **Actor host bypasses this helper — see FIND-sec-009.** | None | None |
| sec-004    | PiiDestructuringPolicy missing | n/a — once registered, the policy runs on every Serilog destructure. No metric. | None | None — no alert if a future host is added without `Destructure.With<PiiDestructuringPolicy>()` |
| sec-005    | FocusAnalyticsService cross-tenant read | Indirect — `TenantScope.GetSchoolFilter` throws `UnauthorizedAccessException` when the school_id claim is missing (`src/shared/Cena.Infrastructure/Tenancy/TenantScope.cs:30-32`) which surfaces in the global exception middleware. No specific log line for "cross-tenant attempt blocked." | None | None — no alert on `UnauthorizedAccessException` rate |
| sec-006    | Hardcoded dev passwords in appsettings.json | n/a — passwords removed; comments at `src/api/Cena.Admin.Api.Host/appsettings.json:5` and `src/api/Cena.Student.Api.Host/appsettings.json:5` | None | None — no CI gate to grep for `"password"` in appsettings |
| sec-007    | ApplicationStarted hook missing | Yes — `app.Lifetime.ApplicationStarted.Register(async () => { try { … } catch (Exception ex) { logger.LogCritical(ex, "Admin API Host startup failed — triggering graceful shutdown"); … } });` at `src/api/Cena.Admin.Api.Host/Program.cs:248-269` and `src/api/Cena.Student.Api.Host/Program.cs:344-364` | None — startup duration not metered | None — no alert if `LogCritical` "startup failed" emitted |

**Observability gap** (filed below as **FIND-sec-014**): structured error logs
exist for the recent fix surfaces, but there are **zero custom metrics** on
the security-critical paths and **zero alerting rules** keyed to the
specific regression classes. The Cena stack is wired to Prometheus +
OpenTelemetry — the infrastructure is in place — but only `AiGenerationService`
(`src/api/Cena.Admin.Api/AiGenerationService.cs:243-256`) creates custom
counters/histograms. Every other security-relevant fix would re-regress
silently in prod.

```text
$ rg -n 'Meter\b|CreateCounter|CreateHistogram' src/api/ src/shared/Cena.Infrastructure/
src/api/Cena.Admin.Api/AiGenerationService.cs:243        IMeterFactory meterFactory)
src/api/Cena.Admin.Api/AiGenerationService.cs:249        _requestDuration = meter.CreateHistogram<double>(
src/api/Cena.Admin.Api/AiGenerationService.cs:253        _tokensTotal = meter.CreateCounter<long>(
src/api/Cena.Admin.Api/AiGenerationService.cs:256        _costUsd = meter.CreateCounter<double>(
# (only the admin AI gen service — no metrics on tutor LLM, no metrics
#  on auth failures, no metrics on tenant-isolation rejection)
```

## REV-001 Firebase service-account key rotation

Known pending item from user memory. Verified-still-pending. The Firebase
admin credentials are loaded from `Firebase:CredentialsPath` /
`GOOGLE_APPLICATION_CREDENTIALS` in `src/shared/Cena.Infrastructure/Firebase/FirebaseAdminService.cs`
and `Firebase:ProjectId = "cena-platform"` is hard-pinned in
`src/api/Cena.Admin.Api.Host/appsettings.json:13` and
`src/api/Cena.Student.Api.Host/appsettings.json` — both correct. The key
rotation itself is a GCP console action, not a code change, and is tracked
under REV-001. **Not enqueued — explicit reference only, per task brief.**

---

## Findings

### Summary

| Severity | Count | IDs |
|---|---|---|
| P0 | 4 | FIND-sec-008, FIND-sec-009, FIND-sec-010, FIND-sec-011 |
| P1 | 4 | FIND-sec-012, FIND-sec-013, FIND-sec-014, FIND-sec-015 |
| P2 | 2 | FIND-sec-016, FIND-sec-017 |
| P3 | 0 | — |

Two of the P0s (sec-009 and sec-011) reference verified-fixed v1 findings
and qualify as **partial regressions** because the fix did not propagate to
every host or service that needed it.

---

- id: FIND-sec-008
  severity: p0
  category: security
  file: src/api/Cena.Admin.Api/AdminUserService.cs
  line: 135-283
  related_prior_finding: null
  framework: null
  evidence:
    - type: grep
      content: |
        $ rg -n 'public async Task.*Async\(string id' src/api/Cena.Admin.Api/AdminUserService.cs
        135:    public async Task<AdminUserDto?> GetUserAsync(string id)
        177:    public async Task<AdminUserDto> UpdateUserAsync(string id, UpdateUserRequest request)
        215:    public async Task SoftDeleteUserAsync(string id)
        239:    public async Task SuspendUserAsync(string id, string reason)
        263:    public async Task ActivateUserAsync(string id)
        # — none take a ClaimsPrincipal, none cross-check user.School against caller school
    - type: file-extract
      content: |
        # Endpoints — src/api/Cena.Admin.Api/AdminUserEndpoints.cs:18-21
        var group = app.MapGroup("/api/admin/users")
            .WithTags("Admin Users")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)   # AdminOnly = ["ADMIN", "SUPER_ADMIN"]
            .RequireRateLimiting("api");

        # CenaAuthPolicies.cs:22 — AdminRoles is global, not school-scoped
        private static readonly string[] AdminRoles = ["ADMIN", "SUPER_ADMIN"];

        # Service — src/api/Cena.Admin.Api/AdminUserService.cs:135-140
        public async Task<AdminUserDto?> GetUserAsync(string id)
        {
            await using var session = _store.QuerySession();
            var user = await session.LoadAsync<AdminUser>(id);
            return user is { SoftDeleted: false } ? AdminUserDto.From(user) : null;
        }
        # No school check anywhere — any AdminOnly caller can fetch any user.

        # SoftDeleteUserAsync (215), SuspendUserAsync (239), ActivateUserAsync (263),
        # UpdateUserAsync (177), ForcePasswordResetAsync (514) — same pattern.
        # GetSessionsAsync (458), RevokeSessionAsync (499), GetActivityAsync (406) — same pattern.

  finding: |
    The entire admin user-management surface (`/api/admin/users/{id}` GET, PUT,
    DELETE; `/api/admin/users/{id}/suspend`, `/activate`, `/force-reset`,
    `/sessions/{sid}` DELETE, `/api-keys/{keyId}` DELETE, `/security`,
    `/activity`, `/sessions`) is reachable by any caller with the `ADMIN` role
    on ANY school, without checking that the target user `id` belongs to the
    caller's school. A school-A admin can read PII for, modify, suspend,
    delete, force-password-reset, revoke API keys for, and revoke active
    sessions of users in any other school. The list endpoint at the same
    group level (`GET /api/admin/users/`) DOES filter by school via
    `IAdminUserService.ListUsersAsync(...ClaimsPrincipal user)` (verified at
    `AdminUserService.cs` line 380 `GetStatsAsync` does the same), so the
    omission on the per-id endpoints is asymmetric and almost certainly
    accidental. ResourceOwnershipGuard.cs:56-61 explicitly comments
    "ADMIN/MODERATOR... no per-student ownership check is needed here.
    School-level scoping is handled by TenantScope.GetSchoolFilter()." That
    promise is broken on every method in this file.

  root_cause: |
    `IAdminUserService` was authored before the tenant-scope helper was
    introduced and was not refactored as part of REV-014 (when TenantScope
    landed). The methods take a single `string id` parameter and assume a
    higher layer enforces ownership; the higher layer (the endpoint group)
    relies on `RequireAuthorization(AdminOnly)` which only checks role, not
    school. Result: a category of P0 cross-tenant writes that cannot be
    triggered without a stolen/issued ADMIN token, but the moment any school
    has more than one admin or any admin is rotated, the exposure is real.

  proposed_fix: |
    1. Add a `ClaimsPrincipal caller` parameter to every per-id method on
       `IAdminUserService` and `AdminUserService`: `GetUserAsync`,
       `UpdateUserAsync`, `SoftDeleteUserAsync`, `SuspendUserAsync`,
       `ActivateUserAsync`, `ForcePasswordResetAsync`, `RevokeApiKeyAsync`,
       `GetSessionsAsync`, `RevokeSessionAsync`, `GetActivityAsync`.
    2. At the top of each, call
       `var schoolId = TenantScope.GetSchoolFilter(caller);` and after the
       Marten `LoadAsync<AdminUser>(id)`, reject with 404 (NOT 403 — a 403
       leaks existence) when `schoolId is not null && user.School != schoolId`.
    3. Plumb `ctx.User` through every endpoint handler in `AdminUserEndpoints.cs`.
    4. Add a regression test in `src/api/Cena.Admin.Api.Tests/AdminUserServiceTests.cs`:
       a. Seed two AdminUser docs in different schools.
       b. Build a ClaimsPrincipal with `role=ADMIN` and `school_id=school-A`.
       c. Assert that calling `GetUserAsync(school-B-user-id, principal)`
          returns null.
       d. Assert the same for the other 8 methods.

  test_required: |
    `AdminUserServiceTenantScopingTests.cs` covering all 9 per-id methods,
    each with a positive (same-school) and negative (cross-school) case,
    plus a SUPER_ADMIN case proving the unrestricted path still works.

  task_body: |
    # FIND-sec-008 (P0): cross-tenant write surface in AdminUserService

    **Files**:
      - src/api/Cena.Admin.Api/AdminUserService.cs (130-560)
      - src/api/Cena.Admin.Api/AdminUserEndpoints.cs (47-213)
      - src/api/Cena.Admin.Api.Tests/ (new file: AdminUserServiceTenantScopingTests.cs)

    **Goal**: enforce that an ADMIN-role caller can only read/write
    AdminUser documents that belong to their own `school_id` claim. Match
    the existing pattern in `ListUsersAsync`/`GetStatsAsync` which already
    do this correctly. SUPER_ADMIN keeps unrestricted access.

    **Scope**:
      1. Refactor `IAdminUserService` to add `ClaimsPrincipal caller` to:
         GetUserAsync, UpdateUserAsync, SoftDeleteUserAsync, SuspendUserAsync,
         ActivateUserAsync, ForcePasswordResetAsync, RevokeApiKeyAsync,
         GetSessionsAsync, RevokeSessionAsync, GetActivityAsync.
      2. Each method:
         - Calls `TenantScope.GetSchoolFilter(caller)`.
         - Loads the AdminUser doc.
         - If `schoolId is not null && user.School != schoolId` — return null
           (read methods) or throw `KeyNotFoundException` (write methods).
           Both surface as 404 to the caller, preserving existence-leak
           defence.
      3. Update every endpoint in AdminUserEndpoints.cs to pass `ctx.User`.
      4. Update the existing `RestoreUserAsync` and any other admin-only
         user-mutating method on `IFirebaseAdminService` calls inside
         `AdminUserService` to short-circuit if the doc is cross-tenant.

    **Definition of Done**:
      - [ ] All 9 per-id methods take a ClaimsPrincipal and apply
            TenantScope.GetSchoolFilter
      - [ ] No method on AdminUserService loads an AdminUser by id without
            a tenant check (`grep -n 'LoadAsync<AdminUser>' src/api/Cena.Admin.Api/AdminUserService.cs`
            shows every line guarded)
      - [ ] AdminUserEndpoints.cs passes ctx.User to every per-id call
      - [ ] New `AdminUserServiceTenantScopingTests.cs` covers ADMIN/ADMIN
            cross-school (denied), ADMIN/ADMIN same-school (allowed),
            SUPER_ADMIN/* (allowed) for all 9 methods
      - [ ] `dotnet test src/api/Cena.Admin.Api.Tests` green
      - [ ] Branch: `<worker>/<task-id>-sec-008-admin-user-tenant-scope`
      - [ ] Push branch; do not merge

    **Reporting**: complete with `--result` describing the 9 method changes,
    test count delta, and any callers in non-test code that had to change.

---

- id: FIND-sec-009
  severity: p0
  category: security
  file: src/actors/Cena.Actors.Host/Program.cs
  line: 119-122
  related_prior_finding: FIND-sec-003
  framework: null
  evidence:
    - type: grep
      content: |
        $ rg -n 'dev_actor_pass|dev_api_pass|GetApiAuth' src/actors/Cena.Actors.Host/Program.cs src/api/Cena.Admin.Api.Host/Program.cs src/api/Cena.Student.Api.Host/Program.cs
        src/actors/Cena.Actors.Host/Program.cs:119:    var natsUser = builder.Configuration["Nats:User"] ?? "actor-host";
        src/actors/Cena.Actors.Host/Program.cs:120:    var natsPass = builder.Configuration["Nats:Password"]
        src/actors/Cena.Actors.Host/Program.cs:121:        ?? Environment.GetEnvironmentVariable("NATS_ACTOR_PASSWORD")
        src/actors/Cena.Actors.Host/Program.cs:122:        ?? "dev_actor_pass";
        src/api/Cena.Admin.Api.Host/Program.cs:73:    var (natsUser, natsPass) = CenaNatsOptions.GetApiAuth(builder.Configuration, builder.Environment);
        src/api/Cena.Student.Api.Host/Program.cs:78:    var (natsUser, natsPass) = CenaNatsOptions.GetApiAuth(builder.Configuration, builder.Environment);
    - type: file-extract
      content: |
        # CenaNatsOptions.cs:46-58 — the helper that the Admin and Student
        # hosts use, which throws fail-fast in non-Development if creds are
        # missing.
        if (env.IsDevelopment())
        {
            return (
                username ?? "cena_api_user",
                password ?? "dev_api_pass"
            );
        }
        throw new InvalidOperationException(
            "NATS API credentials not configured. " +
            "Set NATS:ApiUsername and NATS:ApiPassword in appsettings, " +
            "or NATS_API_USERNAME and NATS_API_PASSWORD environment variables.");

  finding: |
    The fix for FIND-sec-003 (preflight verdict: verified-fixed) introduced
    `CenaNatsOptions.GetApiAuth` which throws fail-fast in non-Development
    when NATS credentials are missing. The Admin host (Program.cs:73) and the
    Student host (Program.cs:78) both use this helper. **The Actor host
    does not.** It still inlines the dangerous pattern: a hard-coded
    fallback (`?? "dev_actor_pass"`) with no environment gate at line 122.
    A non-Development deployment of the Actor host that forgets to set
    `Nats:Password` or `NATS_ACTOR_PASSWORD` will silently ship to production
    using the hard-coded literal, which is the exact regression class
    FIND-sec-003 was supposed to close. This is a partial-fix regression:
    the helper exists but the third host bypasses it.

  root_cause: |
    FIND-sec-003 was scoped to the Admin and Student API hosts; the Actor
    host's NATS connection was edited at the same time but the inline
    fallback was left in place. There is no equivalent
    `CenaNatsOptions.GetActorAuth` helper, and CI does not grep
    `src/actors/**/Program.cs` for hard-coded dev passwords.

  proposed_fix: |
    1. Add `CenaNatsOptions.GetActorAuth(IConfiguration, IHostEnvironment)`
       mirroring `GetApiAuth` but with default username `"actor-host"` and
       env vars `NATS_ACTOR_USERNAME` / `NATS_ACTOR_PASSWORD`.
    2. Replace lines 119-122 in
       `src/actors/Cena.Actors.Host/Program.cs` with
       `var (natsUser, natsPass) = CenaNatsOptions.GetActorAuth(builder.Configuration, builder.Environment);`
    3. Add a regression test in
       `src/shared/Cena.Infrastructure.Tests/Configuration/CenaNatsOptionsTests.cs`
       asserting `GetActorAuth` throws in non-Dev and returns the dev pair
       in Dev.
    4. Add a CI grep gate (GitHub Actions step) that fails if any
       `src/**/Program.cs` contains `"dev_actor_pass"`, `"dev_api_pass"`,
       `"cena_dev_redis"` outside the helper file.

  test_required: |
    `CenaNatsOptionsTests.GetActorAuth_NonDev_ThrowsWhenCredsMissing` and
    `CenaNatsOptionsTests.GetActorAuth_Dev_ReturnsDefaults`. Plus a
    repository-level test or grep gate that scans Program.cs files for the
    raw literal.

  task_body: |
    # FIND-sec-009 (P0, regression of FIND-sec-003): Actor host NATS dev-password fallback

    **Files**:
      - src/actors/Cena.Actors.Host/Program.cs (line 119-122)
      - src/shared/Cena.Infrastructure/Configuration/CenaNatsOptions.cs (add GetActorAuth)
      - src/shared/Cena.Infrastructure.Tests/Configuration/CenaNatsOptionsTests.cs (add tests)

    **Goal**: extend the FIND-sec-003 fix to the Actor host. The Actor host
    must NOT ship to non-Development with a hard-coded NATS password.

    **Scope**:
      1. Extract Actor host NATS auth into `CenaNatsOptions.GetActorAuth`
         (mirror `GetApiAuth` exactly: dev fallback inside `IsDevelopment`,
         throw in non-dev). Default user: `"actor-host"`. Env vars:
         `NATS_ACTOR_USERNAME` / `NATS_ACTOR_PASSWORD`. Config keys:
         `Nats:User` / `Nats:Password` (legacy) and
         `NATS:ActorUsername` / `NATS:ActorPassword` (preferred).
      2. Replace the inline fallback at `src/actors/Cena.Actors.Host/Program.cs:119-122`.
      3. Add CenaNatsOptionsTests for the new helper.
      4. Recommend (not blocking) a CI gate that greps every `Program.cs`
         under `src/**` for the raw literals.

    **Definition of Done**:
      - [ ] `rg -n 'dev_actor_pass' src/actors/` returns no results
      - [ ] `rg -n 'CenaNatsOptions.GetActorAuth' src/actors/` shows the call site
      - [ ] CenaNatsOptionsTests includes 2 new tests for GetActorAuth
      - [ ] `dotnet test src/shared/Cena.Infrastructure.Tests` green
      - [ ] Branch: `<worker>/<task-id>-sec-009-actor-host-nats-dev-fallback`

    **Files to read first**:
      - src/shared/Cena.Infrastructure/Configuration/CenaNatsOptions.cs
      - src/shared/Cena.Infrastructure.Tests/Configuration/CenaNatsOptionsTests.cs
      - src/actors/Cena.Actors.Host/Program.cs (especially the NATS section)

---

- id: FIND-sec-010
  severity: p0
  category: security
  file: src/api/Cena.Admin.Api/AdminRoleService.cs
  line: 164-196
  related_prior_finding: null
  framework: null
  evidence:
    - type: file-extract
      content: |
        # AdminRoleEndpoints.cs:106-128 — endpoint policy
        app.MapPost("/api/admin/users/{id}/role", async (string id, AssignRoleRequest request, IAdminRoleService service) =>
        {
            try
            {
                await service.AssignRoleToUserAsync(id, request);
                return Results.NoContent();
            }
            ...
        })
        .WithTags("Admin Roles")
        .WithName("AssignRoleToUser")
        .RequireAuthorization(CenaAuthPolicies.AdminOnly);   # AdminOnly = ADMIN or SUPER_ADMIN

        # AdminRoleService.cs:164-196 — service body
        public async Task AssignRoleToUserAsync(string userId, AssignRoleRequest request)
        {
            if (!Enum.TryParse<CenaRole>(request.Role, true, out var newRole))
                throw new ArgumentException($"Invalid role: {request.Role}");
            await using var session = _store.LightweightSession();
            var user = await session.LoadAsync<AdminUser>(userId)
                ?? throw new KeyNotFoundException($"User '{userId}' not found");

            // Safety: cannot remove last SUPER_ADMIN
            if (user.Role == CenaRole.SUPER_ADMIN && newRole != CenaRole.SUPER_ADMIN)
            {
                var superAdminCount = await session.Query<AdminUser>()
                    .Where(u => !u.SoftDeleted && u.Role == CenaRole.SUPER_ADMIN)
                    .CountAsync();
                if (superAdminCount <= 1)
                    throw new InvalidOperationException("Cannot remove the last SUPER_ADMIN");
            }

            var updated = user with { Role = newRole };
            session.Store(updated);
            await session.SaveChangesAsync();

            await _firebase.SetCustomClaimsAsync(userId, new Dictionary<string, object>
            {
                ["role"] = newRole.ToString(),
                ["school_id"] = user.School ?? "",
                ["locale"] = user.Locale
            });
            ...
        }

  finding: |
    `POST /api/admin/users/{id}/role` is reachable by any caller with the
    `ADMIN` policy (school-A admin), with **no check on the caller's school
    against the target user's school**, and **no check that the new role is
    not a privilege escalation**. The only safety guard is "you cannot
    remove the last SUPER_ADMIN" — but there is no guard against
    *creating* a new SUPER_ADMIN. A school-A admin can:
      1. Enumerate user IDs via the (also broken — see FIND-sec-008)
         `GET /api/admin/users/{id}` endpoint, or via Firebase if they have
         the email.
      2. POST `{role: "SUPER_ADMIN"}` to their own UID, or to any other
         user's UID — including a sibling-school admin.
      3. Firebase custom claims are then rewritten with `role: SUPER_ADMIN`
         and the actor host honours that role on the next ID-token refresh.
    This is full vertical privilege escalation reachable from any
    school-scoped admin account.

  root_cause: |
    `AssignRoleToUserAsync` was authored as a back-office utility under the
    assumption that `AdminOnly` was already a strong gate. It is not — the
    policy admits both ADMIN and SUPER_ADMIN, and there is no per-role
    "you can only assign roles less than your own" rule. The endpoint also
    fails to call `TenantScope.GetSchoolFilter(caller)` so cross-school
    assignment is silently allowed.

  proposed_fix: |
    1. Tighten the endpoint policy from `AdminOnly` to `SuperAdminOnly`.
       Promotion to / demotion from SUPER_ADMIN should always have been a
       SUPER_ADMIN-only action. Plain school admins should not be able to
       hand out the SUPER_ADMIN role at all.
    2. Add a `ClaimsPrincipal caller` parameter to
       `IAdminRoleService.AssignRoleToUserAsync`. Inside:
       a. Read `var schoolId = TenantScope.GetSchoolFilter(caller);`.
       b. Read `var callerRole = caller.FindFirstValue(ClaimTypes.Role) ?? caller.FindFirstValue("role");`.
       c. After loading the AdminUser, return 404 if `schoolId != null &&
          user.School != schoolId`.
       d. Reject (`InvalidOperationException` mapped to 403) if
          `callerRole != "SUPER_ADMIN"` AND `newRole == CenaRole.SUPER_ADMIN`.
       e. Reject if `callerRole == "ADMIN"` AND `newRole == CenaRole.ADMIN`
          AND the target is in a different school (reinforces step c).
    3. Add an audit-log emit (StudentRecordAccessLog or a new
       PrivilegedActionLog) so role changes are queryable in the FERPA
       audit endpoint.
    4. Add three regression tests:
       - school-A admin assigning SUPER_ADMIN to themselves → 403
       - school-A admin assigning ADMIN to a school-B user → 404
       - SUPER_ADMIN assigning any role anywhere → 200

  test_required: |
    `AdminRoleServicePrivilegeEscalationTests.cs` with the three cases
    above, plus a positive case (SUPER_ADMIN promoting a school admin) to
    prove the legitimate path still works.

  task_body: |
    # FIND-sec-010 (P0): Privilege escalation via /api/admin/users/{id}/role

    **Files**:
      - src/api/Cena.Admin.Api/AdminRoleEndpoints.cs (line 105-128)
      - src/api/Cena.Admin.Api/AdminRoleService.cs (line 164-196)
      - src/api/Cena.Admin.Api.Tests/ (new file: AdminRoleServicePrivilegeEscalationTests.cs)

    **Goal**: prevent any caller without the SUPER_ADMIN role from issuing
    the SUPER_ADMIN role, and prevent any school admin from assigning
    roles to users outside their own school.

    **Scope**:
      1. Change endpoint policy from `AdminOnly` to `SuperAdminOnly` at
         `AdminRoleEndpoints.cs:128`. Verify no other call site assumed
         the looser policy.
      2. Add `ClaimsPrincipal caller` to `IAdminRoleService.AssignRoleToUserAsync`
         and the implementation. Apply tenant + role-step checks:
         - same-school enforcement via TenantScope
         - SUPER_ADMIN-only gate on assigning SUPER_ADMIN
      3. Emit a StudentRecordAccessLog row (`category=privileged_action`)
         on every successful role change so the FERPA audit endpoint can
         surface the change.
      4. Add three new tests in
         `AdminRoleServicePrivilegeEscalationTests.cs`.

    **Definition of Done**:
      - [ ] `RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)` is on the
            role-assignment endpoint
      - [ ] `AssignRoleToUserAsync` rejects the three illegal cases above
      - [ ] Tests in AdminRoleServicePrivilegeEscalationTests.cs pass
      - [ ] StudentRecordAccessLog row is emitted on success (verified by
            test)
      - [ ] `dotnet test src/api/Cena.Admin.Api.Tests` green
      - [ ] Branch: `<worker>/<task-id>-sec-010-role-assignment-escalation`

---

- id: FIND-sec-011
  severity: p0
  category: security
  file: src/api/Cena.Admin.Api/MasteryTrackingService.cs, src/api/Cena.Admin.Api/MessagingAdminService.cs, src/api/Cena.Admin.Api/GdprEndpoints.cs
  line: MasteryTrackingService.cs:74,180,189; MessagingAdminService.cs:34,79,138; GdprEndpoints.cs:34,42,53,67,84,100
  related_prior_finding: FIND-sec-005
  framework: null
  evidence:
    - type: file-extract
      content: |
        # MasteryTrackingService.cs:74-98 — GetClassMasteryAsync ignores caller
        public async Task<ClassMasteryResponse?> GetClassMasteryAsync(string classId)
        {
            await using var session = _store.QuerySession();
            var latest = await session.Query<ClassMasteryRollupDocument>()
                .Where(r => r.ClassId == classId)
                .OrderByDescending(r => r.Date).Take(1)
                .ToListAsync();
            ...
            var snapshots = await session.Query<StudentProfileSnapshot>()
                .Where(s => s.SchoolId == rollup.SchoolId)   # rollup.SchoolId, not caller's
                .ToListAsync();
            ...
        }
        # No ClaimsPrincipal parameter, no TenantScope call. A school-A
        # moderator can fetch the full per-student snapshot list for a
        # school-B class.

        # MasteryTrackingService.cs:180-198 — Methodology overrides ignore caller
        public async Task<IReadOnlyList<MethodologyOverrideDocument>> GetStudentOverridesAsync(string studentId)
        {
            await using var session = _store.QuerySession();
            return await session.Query<MethodologyOverrideDocument>()
                .Where(o => o.StudentId == studentId)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
        }
        public async Task<bool> RemoveOverrideAsync(string studentId, string overrideId)
        {
            ...
            session.Delete<MethodologyOverrideDocument>(overrideId);
            await session.SaveChangesAsync();
            ...
        }
        # /api/admin/mastery/students/{studentId}/methodology-overrides
        # /api/admin/mastery/students/{studentId}/methodology-overrides/{overrideId} (DELETE)
        # — both reachable cross-school by ModeratorOrAbove.

        # MessagingAdminService.cs:34-77 — GetThreadsAsync, GetThreadDetailAsync,
        # GetContactsAsync — none take a ClaimsPrincipal. Endpoint group is
        # ModeratorOrAbove. /api/admin/messaging/threads dumps the thread
        # list across every school, with participant IDs and last-message
        # previews. /api/admin/messaging/threads/{id} dumps the full message
        # body history of any thread by id, no school check.

        # GdprEndpoints.cs:34-108 — every consent / export / erasure route
        # takes only `string studentId` and never compares to the caller's
        # school. AdminOnly policy. A school-A admin can:
        #   - GET  /api/admin/gdpr/consents/{studentId-of-school-B}
        #   - POST /api/admin/gdpr/consents (any studentId)
        #   - DELETE /api/admin/gdpr/consents/{studentId}/{type}
        #   - POST /api/admin/gdpr/export/{studentId-of-school-B}     # full PII export
        #   - POST /api/admin/gdpr/erasure/{studentId-of-school-B}    # destroys data

  finding: |
    FIND-sec-005 (preflight verified-fixed) successfully closed the cross-
    tenant read holes in `FocusAnalyticsService` — every method now calls
    `TenantScope.GetSchoolFilter(user)`. The same audit was NOT applied to
    three other admin services that handle equivalent (or more sensitive)
    student data:

    1. **MasteryTrackingService** — `GetClassMasteryAsync(classId)`,
       `GetStudentOverridesAsync(studentId)`, `RemoveOverrideAsync(studentId,
       overrideId)` all execute against any classId/studentId without
       verifying the caller's school.

    2. **MessagingAdminService** — `GetThreadsAsync`, `GetThreadDetailAsync`,
       `GetContactsAsync` query global ThreadSummary and AdminUser
       collections. A moderator at any school sees every messaging thread
       across the platform, every participant id/email, and the full text
       of every conversation by id.

    3. **GdprEndpoints** — every consent, export, and erasure route is
       AdminOnly with no per-id tenant check. The export route returns the
       full StudentProfileSnapshot (every PII field). The erasure route
       *destroys* data. A school-A admin can erase a school-B student's
       data with one call.

    These were not in the v1 sec lens scope (only FocusAnalyticsService was
    cited) but they are the same bug class and exist on the same code path
    pattern that FIND-sec-005 was supposed to extinguish. Filed as a
    partial regression of FIND-sec-005 because the fix did not propagate
    across the equivalence class.

  root_cause: |
    FIND-sec-005's fix was scoped to the literal file the prior finding
    named, not to "every admin service that touches per-student data."
    There is no architectural rule (interface, source generator, or test)
    that requires every service method taking a `studentId` or `classId`
    to also take a `ClaimsPrincipal` and call `TenantScope.GetSchoolFilter`.

  proposed_fix: |
    1. Add ClaimsPrincipal to all per-id methods on
       `IMasteryTrackingService`, `IMessagingAdminService`,
       `IGdprConsentManager`, `IRightToErasureService`, and
       `StudentDataExporter.Export`.
    2. In each method, fetch the doc, compare `doc.SchoolId` (or the
       student's school via a cheap lookup) to
       `TenantScope.GetSchoolFilter(caller)`, return null/throw on
       mismatch.
    3. For GdprEndpoints, write a new helper
       `GdprResourceGuard.VerifyStudentBelongsToCallerSchool(student, caller)`
       and call it at the top of every route handler.
    4. Write a generic regression test
       `CrossTenantWriteEnforcementTests.cs` that for each affected
       method, instantiates two seeded users (school-A admin, school-B
       student), calls the method, and asserts 404/throw.
    5. Add an architecture test (NetArchTest or hand-rolled) asserting
       every public method on a class ending in "AdminService" that takes
       a `studentId`/`classId` parameter ALSO takes a ClaimsPrincipal.

  test_required: |
    `CrossTenantWriteEnforcementTests.cs` with at minimum:
    - MasteryTrackingService.GetClassMasteryAsync cross-school → null
    - MasteryTrackingService.GetStudentOverridesAsync cross-school → empty
    - MasteryTrackingService.RemoveOverrideAsync cross-school → false
    - MessagingAdminService.GetThreadDetailAsync cross-school → null
    - GdprEndpoints export route cross-school → 404
    - GdprEndpoints erasure route cross-school → 404
    Plus same-school positive cases for each.

  task_body: |
    # FIND-sec-011 (P0, partial regression of FIND-sec-005): cross-tenant reads & destructive writes in Mastery / Messaging / GDPR services

    **Files**:
      - src/api/Cena.Admin.Api/MasteryTrackingService.cs (74-198)
      - src/api/Cena.Admin.Api/AdminApiEndpoints.cs (211-274 — wire ClaimsPrincipal)
      - src/api/Cena.Admin.Api/MessagingAdminService.cs (34-169)
      - src/api/Cena.Admin.Api/MessagingAdminEndpoints.cs (33-62)
      - src/api/Cena.Admin.Api/GdprEndpoints.cs (28-110)
      - src/shared/Cena.Infrastructure/Compliance/GdprConsentManager.cs (and IRightToErasureService impl)
      - src/api/Cena.Admin.Api.Tests/ (new file: CrossTenantWriteEnforcementTests.cs)

    **Goal**: extend the FIND-sec-005 contract to every admin service that
    reads or writes per-student / per-class data. No method may dereference
    a student/class id without verifying that the caller's school matches.
    The right-to-erasure route must NOT be reachable cross-school under
    any non-SUPER_ADMIN role.

    **Scope**:
      1. Mastery: add ClaimsPrincipal to `GetClassMasteryAsync`,
         `GetStudentOverridesAsync`, `RemoveOverrideAsync` (and any other
         per-id method I missed). Each fetches the doc, then enforces
         `caller school == doc school` or returns null/false. Use the
         existing `GetStudentMasteryAsync` (line 62) as the reference
         pattern — it already does this.
      2. Messaging: add ClaimsPrincipal to GetThreadsAsync, GetThreadDetailAsync,
         GetContactsAsync. Threads/contacts must filter by the caller's
         school via the participant's `school_id` (for student
         participants) or the AdminUser.School field (for admin
         participants). For non-SUPER_ADMIN, drop any thread that has no
         participant in the caller's school.
      3. GDPR: rewrite GdprEndpoints to load the AdminUser/Student profile
         first and then call a new
         `GdprResourceGuard.VerifyStudentBelongsToCallerSchool(student, caller)`
         which throws 404 on mismatch. Apply to all 6 routes.
      4. Cross-tenant test file as listed under `test_required`.

    **Definition of Done**:
      - [ ] Every public method on MasteryTrackingService, MessagingAdminService,
            GdprConsentManager, RightToErasureService, StudentDataExporter
            taking a studentId/classId also takes a ClaimsPrincipal
      - [ ] All endpoints in AdminApiEndpoints.cs / MessagingAdminEndpoints.cs /
            GdprEndpoints.cs pass ctx.User to those methods
      - [ ] CrossTenantWriteEnforcementTests.cs has at least 12 tests
            (6 negative cross-school + 6 positive same-school)
      - [ ] `dotnet test src/api/Cena.Admin.Api.Tests` green
      - [ ] Branch: `<worker>/<task-id>-sec-011-mastery-messaging-gdpr-tenant-scope`

---

- id: FIND-sec-012
  severity: p1
  category: security
  file: src/actors/Cena.Actors.Host/Program.cs
  line: 419-458
  related_prior_finding: null
  framework: null
  evidence:
    - type: file-extract
      content: |
        # Actor Host Program.cs:419-458 — anonymous endpoint dumps PII
        app.MapGet("/api/actors/stats", (Cena.Actors.Bus.NatsBusRouter router) =>
        {
            var actors = router.ActiveActors.Values
                .OrderByDescending(a => a.LastActivity)
                .Select(a => new
                {
                    studentId = a.StudentId,
                    sessionId = a.SessionId,
                    messagesProcessed = a.MessagesProcessed,
                    totalAttempts = a.TotalAttempts,
                    correctAttempts = a.CorrectAttempts,
                    accuracy = ...,
                    lastActivity = a.LastActivity,
                    activatedAt = a.ActivatedAt,
                    uptimeSeconds = ...,
                    status = a.Status
                });
            return Results.Ok(new
            {
                ...
                recentErrors = router.RecentErrors.Take(20).Select(e => new
                {
                    e.Timestamp,
                    e.Category,
                    e.Subject,
                    e.Message,
                    e.StudentId        # ← studentId per error
                }),
                activeActorCount = ...,
                actors = actors
            });
        }).WithName("GetActorStats");
        # Internal endpoint — called by SystemMonitoringService health probes.
        # Dashboard UI access is gated by the Admin API's own auth layer.

        # Compare to /api/actors/diag at line 477 which DOES gate:
        }).RequireAuthorization(CenaAuthPolicies.SuperAdminOnly).WithName("ClusterDiagnostic");
        # And /api/actors/warmup at line 499:
        }).RequireAuthorization(CenaAuthPolicies.SuperAdminOnly).WithName("WarmUpActors");

        # Plus: no AddCors / UseCors anywhere on the actor host
        $ rg -n 'AddCors|UseCors' src/actors/Cena.Actors.Host/Program.cs
        (no matches)

  finding: |
    `GET /api/actors/stats` on the Actor host (port 5119 by default) is
    anonymous — no `.RequireAuthorization()`. Its response payload includes
    every active actor's `studentId` and `sessionId` plus per-student
    activity counters and the last 20 errors WITH `e.StudentId`. The
    inline comment at line 460 says "Internal endpoint — called by
    SystemMonitoringService health probes. Dashboard UI access is gated
    by the Admin API's own auth layer." but the route metadata itself is
    not gated. The two sibling endpoints `/api/actors/diag` and
    `/api/actors/warmup` both call `RequireAuthorization(SuperAdminOnly)`,
    so the omission is asymmetric and almost certainly accidental. The
    Actor host also has NO CORS configuration at all (no `AddCors`, no
    `UseCors`), so the only defence in production is network firewall.
    studentId is identifying for minors (children's-data sensitivity)
    even though it is not literally an email.

  root_cause: |
    The endpoint was added before the Actor host gained `UseAuthentication`
    /`UseAuthorization` (lines 502-503) and was never re-audited when the
    middleware was wired in. The "internal-only" comment substitutes for a
    real authorization rule.

  proposed_fix: |
    1. Add `.RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)` to the
       `/api/actors/stats` endpoint at line 458. Match the pattern at lines
       477 and 499.
    2. Reconsider whether `e.StudentId` should be in the response at all —
       a counter of recent errors per category does not need a per-student
       id. Replace with a SHA-256 prefix (the same `EmailHasher` pattern
       FIND-ux-006b uses for password-reset logs).
    3. Add `app.UseCors()` with a narrow allow-list (`http://localhost:5174`
       in dev; configurable in prod) before `UseAuthentication` so the
       Actor host follows the same middleware shape as the Admin and
       Student API hosts.
    4. Add a regression test in
       `src/actors/Cena.Actors.Tests/Host/ActorHostEndpointAuthTests.cs`
       (new file) asserting `/api/actors/stats` returns 401 to an
       anonymous caller and 403 to a non-SUPER_ADMIN caller.

  test_required: |
    `ActorHostEndpointAuthTests.cs` covering anonymous, MODERATOR, ADMIN,
    SUPER_ADMIN against `/api/actors/stats`. Use a WebApplicationFactory
    + a fake JwtBearer scheme.

  task_body: |
    # FIND-sec-012 (P1): Actor host /api/actors/stats anonymous + leaks studentId

    **Files**:
      - src/actors/Cena.Actors.Host/Program.cs (419-458)
      - src/actors/Cena.Actors.Tests/Host/ActorHostEndpointAuthTests.cs (new)

    **Goal**: gate `/api/actors/stats` behind SuperAdminOnly and stop
    embedding raw studentId values in the response payload. Add CORS
    to the Actor host so it cannot be reached cross-origin.

    **Scope**:
      1. Append `.RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)`
         to the MapGet at line 419-458.
      2. Replace `studentId = a.StudentId` and `e.StudentId` with
         `studentIdHash = EmailHasher.Hash(a.StudentId)`. Same for the
         recentErrors block.
      3. Add `builder.Services.AddCors(...)` configuring a narrow
         allow-list and call `app.UseCors()` before `UseAuthentication()`.
      4. New ActorHostEndpointAuthTests covering anonymous + 3 roles.

    **Definition of Done**:
      - [ ] `/api/actors/stats` requires SuperAdminOnly
      - [ ] Response contains no raw studentId or sessionId; hashed values
            only
      - [ ] CORS configured on Actor host
      - [ ] Tests assert anonymous → 401, MODERATOR/ADMIN → 403,
            SUPER_ADMIN → 200
      - [ ] Branch: `<worker>/<task-id>-sec-012-actor-stats-auth`

---

- id: FIND-sec-013
  severity: p1
  category: security
  file: src/api/Cena.Admin.Api.Host/Endpoints/ContentEndpoints.cs
  line: 61-86
  related_prior_finding: null
  framework: null
  evidence:
    - type: file-extract
      content: |
        # ContentEndpoints.cs:61-86
        group.MapGet("/questions/{id}/explanation", async (
            string id, IDocumentStore store, HttpContext httpContext) =>
        {
            await using var session = store.QuerySession();
            var question = await session.Query<QuestionState>()
                .FirstOrDefaultAsync(q => q.Id == id);   # ← no Status filter

            if (question is null)
                return Results.NotFound(new { error = $"Question {id} not found" });
            ...
            return Results.Ok(new
            {
                questionId = question.Id,
                explanation = question.Explanation ?? "",
                aiPrompt = question.AiProvenance?.PromptText,   # ← LLM system prompt leaked
                version = question.EventVersion
            });
        });

        # Contrast: /api/content/questions/{id} (line 27) DOES filter for
        # Status == Published; the explanation route does not.

  finding: |
    `GET /api/content/questions/{id}/explanation` returns `aiPrompt =
    question.AiProvenance?.PromptText`, which is the **system prompt** the
    LLM used to generate the question. Two problems:
    1. The route does NOT filter for `Status == QuestionLifecycleStatus.Published`
       (compare to line 31 of the same file which does), so it returns
       explanations and prompts for draft / pending-review / rejected
       questions too. Anyone with a valid student token can probe
       arbitrary question IDs and read the prompt for a question that
       hasn't been approved.
    2. Exposing the system prompt to learners enables prompt-injection
       experiments and undermines the value of AI-authored content (the
       prompt IS the IP). The admin question-bank surface
       (`/api/admin/questions/{id}` calling `IQuestionBankService.GetQuestionAsync`)
       returns this same field, but it is gated to ModeratorOrAbove —
       which is the right audience.
    3. The endpoint is reachable by any authenticated student (group is
       `RequireAuthorization()` with no role policy) so the prompt
       leakage applies to children, which is exactly the user class the
       data is supposed to be served TO, not authored BY.

  root_cause: |
    `aiPrompt` was added when the explanation contract was extended for
    moderator review and never gated when the same DTO was reused for the
    student-facing route. The status filter on the same route was also
    missed when the sibling `/questions/{id}` route gained it.

  proposed_fix: |
    1. Add `&& q.Status == QuestionLifecycleStatus.Published` to the
       LINQ filter at line 67.
    2. Remove `aiPrompt = question.AiProvenance?.PromptText` from the
       student-facing response. Keep it on the admin
       `/api/admin/questions/{id}` route.
    3. Add a regression test in
       `src/api/Cena.Admin.Api.Tests/ContentEndpointsTests.cs` (or new
       file) seeding a draft + a published question and asserting:
       - draft id → 404
       - published id → 200 with no `aiPrompt` key

  test_required: |
    `ContentEndpointsExplanationTests.cs` — draft, published, missing,
    deprecated cases; assert response shape never contains `aiPrompt`.

  task_body: |
    # FIND-sec-013 (P1): Student content endpoint leaks LLM system prompt + serves draft questions

    **Files**:
      - src/api/Cena.Admin.Api.Host/Endpoints/ContentEndpoints.cs (61-86)
      - src/api/Cena.Admin.Api.Tests/ContentEndpointsExplanationTests.cs (new)

    **Goal**: stop the student-facing /api/content/questions/{id}/explanation
    route from returning unpublished questions or the LLM prompt that
    generated them.

    **Scope**:
      1. Add `&& q.Status == QuestionLifecycleStatus.Published` to the
         LINQ at line 67.
      2. Drop the `aiPrompt` field from the response shape.
      3. New ContentEndpointsExplanationTests with the four cases.

    **Definition of Done**:
      - [ ] `rg -n 'aiPrompt' src/api/Cena.Admin.Api.Host/Endpoints/ContentEndpoints.cs`
            returns no matches
      - [ ] LINQ filter on the explanation route includes Published gate
      - [ ] New test file passes
      - [ ] Branch: `<worker>/<task-id>-sec-013-content-explanation-leak`

---

- id: FIND-sec-014
  severity: p1
  category: observability
  file: src/api/Cena.Admin.Api.Host/Program.cs, src/api/Cena.Student.Api.Host/Program.cs, src/actors/Cena.Actors.Host/Program.cs
  line: meters block in each
  related_prior_finding: null
  framework: null
  evidence:
    - type: grep
      content: |
        $ rg -n 'Meter\b|CreateCounter|CreateHistogram|CreateUpDown' src/api/ src/shared/Cena.Infrastructure/
        src/api/Cena.Admin.Api/AiGenerationService.cs:243        IMeterFactory meterFactory)
        src/api/Cena.Admin.Api/AiGenerationService.cs:249        _requestDuration = meter.CreateHistogram<double>(
        src/api/Cena.Admin.Api/AiGenerationService.cs:253        _tokensTotal = meter.CreateCounter<long>(
        src/api/Cena.Admin.Api/AiGenerationService.cs:256        _costUsd = meter.CreateCounter<double>(
        # — only the admin AI gen service. Zero custom metrics on:
        #   - tutor LLM (TutorEndpoints, ClaudeTutorLlmService)
        #   - leaderboard SQL parse failures (FIND-sec-001 regression class)
        #   - cross-tenant attempt rejections (FIND-sec-005 regression class)
        #   - Firebase ID-token verification failures
        #   - SignalR connection auth rejections
        #   - rate-limit rejections per policy

  finding: |
    Cena's three .NET hosts wire OpenTelemetry with Prometheus
    + OTLP exporters (the infrastructure is in place — see
    `Cena.Admin.Api.Host/Program.cs:158-173`,
    `Cena.Student.Api.Host/Program.cs:228-243`, and the
    `Cena.Actors.Host/Program.cs` Meter list at lines ~340-360). Auto
    instrumentation collects request count and latency histograms via
    `AddAspNetCoreInstrumentation()`. But there are **no custom counters
    or histograms** on any of the security-critical paths that the v1
    sec lens fixed:
    - Leaderboard SQL parse / query failures (FIND-sec-001)
    - Tenant-isolation rejections (FIND-sec-005, sec-008, sec-011)
    - Firebase ID-token verification failures
    - SignalR connection auth rejections
    - Rate-limit rejections per policy
    - Tutor LLM call count, error rate, token usage per student
    - Privileged action audit (role assignments, GDPR exports, erasures)

    Combined with no alerting rules (no Prometheus alertmanager file in
    repo, `rg -n 'alertmanager|prom_rules' config/` returns empty), the
    consequence is that any silent re-regression of FIND-sec-001..007 in
    production would not surface until a child or a regulator notices.
    The v2 prompt's expanded sec mandate explicitly calls this out.

  root_cause: |
    Observability was added at the framework level (auto instrumentation)
    but not at the domain level. There is no convention for "every fix
    gets a metric." The only domain-level metrics are on AiGenerationService
    because cost tracking forced them.

  proposed_fix: |
    Add a `Cena.Infrastructure.Observability.SecurityMetrics` static class
    that registers a single `Meter("cena.security")` with the following
    instruments and is wired into all 3 hosts:

      - Counter `cena.security.tenant_rejection.count`
        (tags: `service`, `endpoint`, `caller_role`)
        — incremented every time TenantScope.GetSchoolFilter throws or a
        cross-school check returns 404
      - Counter `cena.security.auth_rejection.count`
        (tags: `service`, `reason`={ token_invalid, token_expired,
        token_missing, signature_failed })
      - Counter `cena.security.rate_limit_rejection.count`
        (tags: `policy_name`)
      - Counter `cena.security.privileged_action.count`
        (tags: `action`={ assign_role, gdpr_export, gdpr_erasure,
        suspend_user, force_reset, revoke_session })
      - Histogram `cena.security.firebase_token_validation.duration_ms`
        (used to spot Firebase JWKS network slowness in prod)
      - Counter `cena.security.signalr_connect_rejection.count`

    Plus a Prometheus alerting rules YAML under
    `config/prom-rules/cena-security.yml` with at minimum:
      - `cena.security.tenant_rejection.count rate > 5/min for 5m`
      - `cena.security.auth_rejection.count rate > 50/min for 5m`
      - `cena.security.privileged_action.count{action="assign_role"} > 0`
        (paged immediately — privilege changes should be rare and
        deliberate)

  test_required: |
    Per-instrument unit test in
    `Cena.Infrastructure.Tests/Observability/SecurityMetricsTests.cs`
    asserting that every cross-tenant rejection from FIND-sec-008/011
    increments the counter exactly once with the right tags. Wire the
    test using `MeterListener` (the same approach AiGenerationServiceTests
    uses).

  task_body: |
    # FIND-sec-014 (P1): No custom metrics on the security-critical fix paths

    **Files**:
      - src/shared/Cena.Infrastructure/Observability/SecurityMetrics.cs (new)
      - src/shared/Cena.Infrastructure/Tenancy/TenantScope.cs (call counter on throw)
      - src/api/Cena.Admin.Api.Host/Program.cs (register meter)
      - src/api/Cena.Student.Api.Host/Program.cs (register meter)
      - src/actors/Cena.Actors.Host/Program.cs (register meter)
      - config/prom-rules/cena-security.yml (new)
      - src/shared/Cena.Infrastructure.Tests/Observability/SecurityMetricsTests.cs (new)

    **Goal**: ensure every security-critical regression class introduced
    by FIND-sec-001..011 has a metric and an alerting rule, so a silent
    re-regression in prod is detectable inside one alerting window.

    **Scope**: see proposed_fix above.

    **Definition of Done**:
      - [ ] `Meter("cena.security")` registered in all 3 hosts
      - [ ] All 6 instruments emit on the right code paths (verified by
            tests)
      - [ ] Prometheus rules YAML committed and referenced from
            docker-compose observability config
      - [ ] `dotnet test src/shared/Cena.Infrastructure.Tests` green
      - [ ] Branch: `<worker>/<task-id>-sec-014-security-metrics`

---

- id: FIND-sec-015
  severity: p1
  category: cost
  file: src/api/Cena.Student.Api.Host/Program.cs, src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs
  line: Program.cs:181-186, TutorEndpoints.cs:39-46
  related_prior_finding: null
  framework: null
  evidence:
    - type: file-extract
      content: |
        # Student host Program.cs:181-186 — only per-user limit
        options.AddFixedWindowLimiter("tutor", opt =>
        {
            opt.PermitLimit = 10;
            opt.Window = TimeSpan.FromMinutes(1);
            opt.QueueLimit = 0;
        });

        # TutorEndpoints.cs:39-46 — every tutor call uses "tutor" policy
        group.MapPost("/threads/{threadId}/messages", SendMessage)
            .WithName("SendTutorMessage")
            .RequireRateLimiting("tutor");
        group.MapPost("/threads/{threadId}/stream", StreamMessage)
            .WithName("StreamTutorMessage")
            .RequireRateLimiting("tutor"); // 10 messages/min/student

  finding: |
    The "tutor" rate-limit policy is per-user (10 messages/min/student) but
    there is **no global cap** and **no per-tenant cap**. With 10K active
    students at peak, the platform can fire 100K LLM calls/minute against
    Claude. At Anthropic's published Sonnet pricing (~$3/M input tokens,
    ~$15/M output, conservative ~1.5K-token tutor turn) that is roughly
    $4-7/minute and the cost scales linearly with student count. The
    AI rate limiter on the admin host (`AddFixedWindowLimiter("ai", ...)`)
    has the same per-user-only design. There is no cost-aware global
    breaker, no token budget per tenant, and no automatic shedding when
    the per-day budget is hit. Cena Tier 3 (LLM router) does not exist as
    a budget enforcement layer.

  root_cause: |
    Rate limiting was added per-user as a UX-friendly cap (don't
    hammer the model from one student) without considering the cost
    surface in aggregate. There is no `IAiBudgetService` and no
    Redis-backed token bucket per tenant.

  proposed_fix: |
    1. Add a global "tutor-global" fixed-window limiter at the host level
       (not per-user) with a configurable PermitLimit
       (`Cena:LlmBudget:GlobalTutorPerMinute`, default 1000).
    2. Add a per-tenant "tutor-tenant" sliding-window limiter partitioned
       by `school_id` claim, default 200/min.
    3. Plumb both limiters via `RequireRateLimiting` chain on the tutor
       endpoints — ASP.NET Core's RateLimiterMiddleware composes them in
       sequence.
    4. Add a Redis-backed daily token-budget service:
       `IAiTokenBudgetService.TryReserveAsync(tenantId, estimatedTokens)`
       that increments a `cena:llm:budget:{tenant}:{yyyymmdd}` key against
       a configurable cap. Fail the request with 429 + a clear message if
       the cap is hit.
    5. Wire the existing `AiGenerationService._tokensTotal` and
       `_costUsd` counters from FIND-sec-014 into the tutor path so cost
       is tracked per student / per tenant in Prometheus.

  test_required: |
    Integration test in `src/api/Cena.Admin.Api.Tests/AiBudgetTests.cs`
    seeding 10K virtual student tokens, hitting the tutor endpoint, and
    asserting:
    - `tutor` per-user limit triggers at the 11th call from one student
    - `tutor-global` limit triggers at the 1001st call (or whatever the
      configured cap is)
    - `tutor-tenant` limit triggers at the 201st call from one school
    - Daily token budget exhaustion returns 429 + a typed error body

  task_body: |
    # FIND-sec-015 (P1): No global / per-tenant cap on AI tutor cost

    **Files**:
      - src/api/Cena.Student.Api.Host/Program.cs (rate limiter section)
      - src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs
      - src/shared/Cena.Infrastructure/Ai/AiTokenBudgetService.cs (new)
      - src/api/Cena.Admin.Api.Tests/AiBudgetTests.cs (new)
      - appsettings.json (new Cena:LlmBudget section)

    **Goal**: bound LLM cost per minute and per day at the global and
    per-tenant level. Per-user limits remain as UX guardrails.

    **Scope**: see proposed_fix above. Coordinate with FIND-sec-014 so
    metrics are wired into the same Meter.

    **Definition of Done**:
      - [ ] tutor-global and tutor-tenant policies registered in
            Program.cs and chained on the tutor endpoints
      - [ ] AiTokenBudgetService registered as singleton, backed by Redis
      - [ ] Tutor SendMessage / StreamMessage call
            TryReserveAsync before the LLM call
      - [ ] AiBudgetTests covers the 4 cases above
      - [ ] Branch: `<worker>/<task-id>-sec-015-ai-cost-budgeting`

---

- id: FIND-sec-016
  severity: p2
  category: security
  file: src/api/Cena.Admin.Api/IngestionPipelineService.cs
  line: 214-238
  related_prior_finding: null
  framework: null
  evidence:
    - type: file-extract
      content: |
        # IngestionPipelineService.cs:214-238 — uploader identity is hardcoded
        public async Task<UploadFileResponse> UploadFromRequestAsync(HttpRequest request)
        {
            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            ...
            using var stream = file.OpenReadStream();
            var result = await _orchestrator.ProcessFileAsync(new IngestionRequest(
                FileStream: stream,
                Filename: file.FileName,
                ContentType: file.ContentType ?? "application/octet-stream",
                SourceType: "upload",
                SourceUrl: null,
                SubmittedBy: "admin"             # ← string literal, not the caller
            ));
            ...
        }

  finding: |
    Every uploaded ingestion file is attributed to the literal string
    `"admin"` regardless of who actually uploaded it. The audit trail
    therefore cannot answer "who uploaded this file?" for
    incident-response or content-moderation rollbacks. This is a
    label-drift / observability bug rather than a direct security hole,
    but it cripples after-the-fact attribution which the FERPA audit
    requires for any educational record write.

  root_cause: |
    The handler does not have an `HttpContext` parameter and does not
    extract `ctx.User.FindFirstValue("sub")` to populate `SubmittedBy`.
    Same pattern in `IngestCloudDirectoryAsync` (`SubmittedBy = "admin-cloud-dir"`)
    is at least more specific but still not per-user.

  proposed_fix: |
    Pass `HttpRequest` (which already arrives) → grab the `HttpContext`
    via `request.HttpContext` → set `SubmittedBy =
    httpContext.User.FindFirstValue("sub") ?? "anonymous"`. Apply the
    same change to the cloud-dir ingest method.

  test_required: |
    `IngestionPipelineSubmittedByTests.cs` asserting that `SubmittedBy`
    matches the caller's `sub` claim after upload.

  task_body: |
    # FIND-sec-016 (P2): IngestionPipelineService hardcodes SubmittedBy="admin"

    **Files**:
      - src/api/Cena.Admin.Api/IngestionPipelineService.cs (214-238, plus
        IngestionPipelineCloudDir.cs around line 170)
      - src/api/Cena.Admin.Api.Tests/IngestionPipelineSubmittedByTests.cs (new)

    **Goal**: capture the actual uploading user id in the audit trail.

    **Scope**:
      1. Add an `HttpContext` parameter (or read `request.HttpContext`)
         and set `SubmittedBy = ctx.User.FindFirstValue("sub") ?? "anonymous"`.
      2. Same for IngestCloudDirectoryAsync. The cloud-dir batch is
         triggered by an admin too — read the caller from the endpoint
         (currently the endpoint doesn't pass any context, so add it).
      3. Test asserts SubmittedBy matches caller sub.

    **Definition of Done**:
      - [ ] `rg -n 'SubmittedBy = "admin"' src/api/Cena.Admin.Api/IngestionPipeline*.cs`
            returns nothing
      - [ ] Test passes
      - [ ] Branch: `<worker>/<task-id>-sec-016-ingestion-submittedby`

---

- id: FIND-sec-017
  severity: p2
  category: security
  file: src/api/Cena.Admin.Api.Host/Endpoints/ClassroomEndpoints.cs
  line: 19-65
  related_prior_finding: null
  framework: null
  evidence:
    - type: file-extract
      content: |
        # ClassroomEndpoints.cs:31-65 — POST /api/classrooms/join
        # mounted on the ADMIN host but used by STUDENTS to join
        var classroom = await session.Query<ClassroomDocument>()
            .FirstOrDefaultAsync(c => c.JoinCode.ToLower() == request.Code.ToLower() && c.IsActive);

        # Plus: the route is mounted on the Admin host:
        # Cena.Admin.Api.Host/Program.cs:231 → app.MapClassroomEndpoints();
        # but it is the only Student-facing route on the Admin host. The
        # Student host has no equivalent.

  finding: |
    `POST /api/classrooms/join` accepts a join code and returns the
    matching classroom + teacher name. The auth check (`RequireAuthorization()`
    + `ResourceOwnershipGuard.VerifyStudentAccess`) only verifies the
    caller is a student, not that they were invited to the class. There
    is also no rate limiter on the route, so a student can brute-force
    join codes — Cena join codes are typically 6 alphanumeric chars
    (~2 billion combinations, but admins commonly use words or short
    sequences, narrowing the practical search space). Each successful
    enumeration leaks the teacher's name. The route is also misplaced
    on the Admin host (a student-facing endpoint mounted on the admin
    surface — arch concern, cross-link to FIND-arch-* if filed).

  root_cause: |
    The endpoint was added during STB-00b on the legacy `Cena.Api.Host`
    and migrated to the Admin host as part of DB-06b without
    re-evaluating either its host placement or its abuse surface.

  proposed_fix: |
    1. Move the endpoint to the Student API host
       (`src/api/Cena.Student.Api.Host/Endpoints/ClassroomEndpoints.cs`)
       and remove the registration from the Admin host's Program.cs.
    2. Add `.RequireRateLimiting("classroom-join")` (define a new
       fixed-window 5/min/student policy in Student host Program.cs).
    3. Lock-out (return 429 with `Retry-After`) for 15 minutes after
       N consecutive failed lookups from the same user, tracked in
       Redis.
    4. Add a regression test asserting brute-force is rate-limited.

  test_required: |
    `ClassroomJoinRateLimitTests.cs` — fire 6 invalid codes, assert the
    6th returns 429.

  task_body: |
    # FIND-sec-017 (P2): /api/classrooms/join brute-forceable + on wrong host

    **Files**:
      - src/api/Cena.Admin.Api.Host/Endpoints/ClassroomEndpoints.cs (delete)
      - src/api/Cena.Admin.Api.Host/Program.cs (line 231 — remove)
      - src/api/Cena.Student.Api.Host/Endpoints/ClassroomEndpoints.cs (new — relocated)
      - src/api/Cena.Student.Api.Host/Program.cs (add classroom-join rate limit)
      - src/api/Cena.Admin.Api.Tests/ClassroomJoinRateLimitTests.cs (new)

    **Goal**: relocate the student-facing classroom-join route to the
    Student host and add rate limiting + lock-out to prevent join-code
    brute-forcing.

    **Scope**: see proposed_fix.

    **Definition of Done**:
      - [ ] Route only registered on Student host
      - [ ] classroom-join rate-limit policy applied
      - [ ] Lock-out kicks in after N failures
      - [ ] Branch: `<worker>/<task-id>-sec-017-classroom-join-bruteforce`

---

## Cross-references and notes

- **FIND-sec-008, sec-010, sec-011** all share a root cause: services that
  take `string id` parameters do not also take a `ClaimsPrincipal` and
  do not call `TenantScope.GetSchoolFilter`. Recommend a single coordinator
  decision: introduce an architecture test that fails CI when a public
  method on `*AdminService` / `*ConsentManager` / `*ErasureService` taking
  a studentId/classId/userId parameter does not also take a ClaimsPrincipal.
  This would prevent the entire equivalence class from re-emerging.

- **FIND-sec-009** is filed as a partial regression of FIND-sec-003. The
  preflight verdict on sec-003 was correct for the two named hosts; the
  Actor host's NATS connection was simply outside the original task
  scope. Coordinator should retag sec-003 as "regressed" in the
  cross-link section of the merged report.

- **FIND-sec-011** is filed as a partial regression of FIND-sec-005. Same
  reason: the FocusAnalyticsService fix was correct, the equivalence
  class was not extended.

- **REV-001** Firebase service-account key rotation — verified pending,
  not enqueued, referenced only.

- **No fake-fixes detected.** Every preflight `verified-fixed` finding is
  legitimately fixed in the literal scope it claimed. The new findings
  here are either expansions of the equivalence class, or entirely new
  surfaces that the v1 lens did not look at (admin user mgmt,
  privilege escalation via role assignment, content prompt leak, actor
  host stats endpoint, observability). None of them re-introduce a bug
  the v1 fix had ostensibly removed.

- **No GraphQL proposals. No #7367F0 contrast flag.**

## Total finding count

| Severity | Count |
|---|---|
| P0 | 4 |
| P1 | 4 |
| P2 | 2 |
| Total | 10 |
