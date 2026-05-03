# Agent 2 — Security, Auth & Infra Architect Findings

Date: 2026-04-11
Base commit: 5415263e522e9c007a1362237685e8343238d344 (local main, `DB-06b: Migrate endpoints to Cena.Student.Api.Host + Cena.Admin.Api.Host`)
Worker: claude-subagent-sec
Services running: NO (localhost:5050/5051/5119 all refused TCP). All evidence below is static-analysis against source + git history.

## Summary

- **P0 count**: 2 (SQL injection in LeaderboardService; CORS wildcard + no-auth scaffolds on `main` that precede the DB-06b migration commit)
- **P1 count**: 5 (NATS dev-password fallback in prod; PII destructuring missing on new hosts; tenant isolation bypass in 4 Focus analytics read endpoints; non-Dev-gated hardcoded dev passwords in in-tree appsettings; Firebase Admin SDK init skipped on new hosts)
- **P2 count**: 1 (env var drift between in-code fallbacks and docker-compose/.env)
- **P3 count**: 0

### Note on scope

The task brief lists `5415263` as the last commit on the user's local main. `origin/main` on the remote is still at `989efa0` — the commit *prior* to DB-06b. This means P0-002 (the scaffold hosts with `AllowAnyOrigin` and no auth) is *currently what is on origin/main* — until DB-06b is pushed, any deployment pulling from origin will boot the scaffold hosts. Once DB-06b lands, P0-002 is resolved for the new hosts. I have kept P0-002 in the report because the brief named `989efa0` as ground truth and because it demonstrates the risk of shipping "scaffold" files with `AllowAnyOrigin` in the first place.

## Findings

- id: FIND-sec-001
  severity: p0
  category: security
  file: src/shared/Cena.Infrastructure/Gamification/LeaderboardService.cs
  line: 147, 154, 156, 214, 215, 222, 224, 267, 271, 300, 303, 322, 325, 326, 349, 352, 353
  evidence:
    - type: grep
      content: |
        $ grep -n 'var sql = \$' src/shared/Cena.Infrastructure/Gamification/LeaderboardService.cs
        81:        var sql = $@"
        147:        var sql = $@"
        215:        var sql = $@"
        267:        var sql = $@"
    - type: file-extract
      content: |
        # Line 147-156 — GetClassLeaderboardAsync: schoolId interpolated into raw SQL
        var sql = $@"
            SELECT data->>'StudentId' as StudentId, ...
            FROM cena.mt_doc_studentprofilesnapshot
            WHERE data->>'SchoolId' = '{classroom.SchoolId}'
            ORDER BY COALESCE((data->>'TotalXp')::int, 0) DESC
            LIMIT {limit}";
        ...
        await using var cmd = session.Connection?.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        # Line 215-224 — GetFriendsLeaderboardAsync: friend IDs joined into IN clause
        var idList = string.Join("','", friendIds);
        var sql = $@"
            ...
            WHERE data->>'StudentId' IN ('{idList}')
            ORDER BY COALESCE((data->>'TotalXp')::int, 0) DESC
            LIMIT {limit}";

        # Line 267-271, 300-303, 322-326, 349-353 — GetStudentRanksAsync: studentId + schoolId + studentXp interpolated into 4 separate raw SQL statements

  finding: |
    `LeaderboardService` builds 7+ raw SQL statements by C# string interpolation and
    executes them via `NpgsqlCommand.CommandText` with NO parameter binding. The
    interpolated values include:

      - `classroom.SchoolId` (loaded from ClassroomDocument — originally set at
        classroom-creation time from whatever value the admin endpoint wrote)
      - `friendIds` (loaded from FriendshipDocument — originally stored from
        POST /api/social/friends/request)
      - `studentId` (the caller's own Firebase sub claim)
      - `limit` (int32 caller parameter — not SQLi-exploitable today, but same
        broken discipline)

    The caller today is always the authenticated student's Firebase UID, which is
    currently a 28-char alphanumeric token and not directly weaponisable. But:

      1. **Stored SQLi channel is live** — the moment any write path (existing or
         future) accepts a `schoolId` or `studentId` string from a request body
         and persists it to `ClassroomDocument.SchoolId` or
         `FriendshipDocument.StudentAId/BId` without shape validation, every
         subsequent read through this service becomes an SQL-injection primitive.
         There is no defence-in-depth here — the document layer is trusted to
         produce clean SQL literals.

      2. **Per-tenant trust is broken** — a malicious moderator on one school
         could update a classroom document to contain a single-quote-plus-UNION
         payload and trigger cross-school data exfiltration on the next
         `GetClassLeaderboardAsync` call.

      3. **The `limit` interpolation is a code smell** that would turn into real
         SQLi the moment someone changes the parameter type from `int` to
         `string`.

    This is a P0 because dynamic SQL via string interpolation is an
    industry-classified critical anti-pattern regardless of current input shape,
    and the user has explicitly banned "stub / canned / narrow-vector-only" fixes.

  root_cause: |
    Author used `$@"..."` interpolated verbatim strings and passed them to
    `cmd.CommandText` without `cmd.Parameters.AddWithValue`. The parameterised
    pattern already exists elsewhere in the repo (e.g. EmbeddingAdminService.cs
    lines 149-163 do it correctly). This one file was never migrated.

  proposed_fix: |
    Rewrite all 7 raw SQL statements in `LeaderboardService.cs` to use positional
    Postgres parameters (`$1`, `$2`, …) and `cmd.Parameters.AddWithValue(...)`.
    Pattern to copy: `src/api/Cena.Admin.Api/EmbeddingAdminService.cs` lines
    149-163. While there, add an explicit `CAST($N AS int)` for the LIMIT slot
    and remove the `string.Join("','", friendIds)` IN-clause hack in favour of
    `= ANY($N::text[])`. Add a unit test that passes a payload containing a
    single quote (e.g. `school-'; DROP TABLE--`) and asserts the query runs
    without error and returns no rows (proving the payload was treated as data,
    not SQL).

  task_body: |
    # FIND-sec-001 (P0): Parameterise LeaderboardService raw SQL — eliminate SQLi anti-pattern

    **File**: src/shared/Cena.Infrastructure/Gamification/LeaderboardService.cs

    **Goal**: Replace 7 string-interpolated SQL statements with parameterised
    NpgsqlCommand queries. No behavioural change. Add regression tests.

    **Scope**:

    Rewrite these methods to use `NpgsqlCommand.Parameters.AddWithValue`:

      - GetGlobalLeaderboardAsync (line 73)   — `LIMIT {limit}` → `LIMIT $1`
      - GetClassLeaderboardAsync  (line 125)  — `SchoolId = '{classroom.SchoolId}'` → `= $1`, `LIMIT $2`
      - GetFriendsLeaderboardAsync(line 192)  — `IN ('{idList}')` → `= ANY($1::text[])`, `LIMIT $2`
      - GetStudentRanksAsync      (line 260)  — 4 separate raw SQLs; parameterise every `studentXp`, `schoolId`, `studentId`, friend list

    Reference implementation in the same repo:
    `src/api/Cena.Admin.Api/EmbeddingAdminService.cs` lines 149-163.

    **Definition of Done**:

      - [ ] `grep -n '$@"' src/shared/Cena.Infrastructure/Gamification/LeaderboardService.cs` returns no results
      - [ ] `grep -n 'string.Join.*friendIds' src/shared/Cena.Infrastructure/Gamification/LeaderboardService.cs` returns no results
      - [ ] All 4 leaderboard views (global, class, friends, ranks) still return identical results on a seeded dataset
      - [ ] New tests in `src/api/Cena.Admin.Api.Tests` or adjacent: `LeaderboardServiceSqliSafetyTests.cs`:
        - Seeds a classroom with `SchoolId = "school-'; DROP TABLE cena.mt_doc_studentprofilesnapshot;--"`
        - Calls GetClassLeaderboardAsync and asserts it returns empty list (not error, not exception — proving the payload was treated as data)
        - Seeds a student with a single-quote-containing `StudentId` via the raw doc store
        - Calls GetStudentRanksAsync and asserts it returns correct counts
      - [ ] `npm run lint` + `dotnet test` pass
      - [ ] Branch: `<worker>/<task-id>-sec-001-leaderboard-sqli`
      - [ ] Commit trailer: `Task: <id>` + `Co-Authored-By: <worker> <email>`
      - [ ] Push branch; do NOT merge to main

    **Files to read first**:
      - src/shared/Cena.Infrastructure/Gamification/LeaderboardService.cs
      - src/api/Cena.Admin.Api/EmbeddingAdminService.cs (reference)
      - src/api/Cena.Student.Api.Host/Endpoints/GamificationEndpoints.cs (call sites)

    **Reporting**: `complete --result "<summary> Branch: <branch>"` citing before/after test counts and the specific lines changed.

- id: FIND-sec-002
  severity: p0
  category: security
  file: src/api/Cena.Admin.Api.Host/Program.cs (origin/main), src/api/Cena.Student.Api.Host/Program.cs (origin/main)
  line: 22-31 (both files)
  evidence:
    - type: grep
      content: |
        $ git show 989efa0:src/api/Cena.Admin.Api.Host/Program.cs | sed -n '22,32p'
        // ---- CORS (minimal default) ----
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            });
        });

        $ git show 989efa0:src/api/Cena.Student.Api.Host/Program.cs | sed -n '22,32p'
        // ---- CORS (minimal default) ----
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            });
        });

        $ git show 989efa0:src/api/Cena.Admin.Api.Host/Program.cs | wc -l
        50
        $ git show 989efa0:src/api/Cena.Student.Api.Host/Program.cs | wc -l
        50

  finding: |
    On origin/main (commit 989efa0), both `Cena.Admin.Api.Host/Program.cs` and
    `Cena.Student.Api.Host/Program.cs` are 50-line scaffold files that configure:

      - `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()`  (wildcard CORS)
      - NO `UseAuthentication()`
      - NO `UseAuthorization()`
      - NO Firebase JWT handler
      - NO rate limiter
      - Only `/health/live`, `/health/ready`, and `GET /` ("scaffold status") endpoints

    These hosts were introduced in commit `02e9fe0e / Phase 1 scaffold` and sat
    on origin/main as a deliberate "scaffold". The subsequent DB-06b commit
    (`5415263`, local main only, not yet pushed) overwrites both files with a
    full middleware pipeline including Firebase auth + narrow CORS allow-list.

    **The concrete risk**: any deployment pipeline that pulls from origin/main
    will build and boot the scaffold hosts. The hosts don't expose business
    endpoints on origin/main, so the data-loss surface is limited — but the
    pattern of committing `AllowAnyOrigin` + no-auth skeleton files to main is
    itself the vulnerability. The DB-06b author clearly intended to wire up
    endpoints; the next person to touch the file could plausibly re-introduce
    `MapPost` endpoints without re-checking the middleware pipeline.

    Even in the DB-06b version (5415263), the auth middleware is
    `app.UseAuthentication(); app.UseAuthorization();` — but group-level
    `.RequireAuthorization()` is what actually blocks anonymous access. See
    FIND-sec-007.

  root_cause: |
    The initial Phase 1 scaffold treated "no auth" as the development-only state
    and committed the permissive defaults to main expecting they would be
    overwritten before production. They were only overwritten in an unpushed
    local commit (5415263). No branch-protection rule or CI check prevents
    `AllowAnyOrigin` from appearing in `Program.cs` files under `src/api/`.

  proposed_fix: |
    1. Merge DB-06b (`5415263`) to origin/main promptly, OR revert the scaffold
       hosts to remove `AllowAnyOrigin` while endpoints are still being
       designed.

    2. Add a CI grep-gate that fails the build on `AllowAnyOrigin()` in any
       file under `src/api/**/Program.cs` — this would have caught the issue
       at PR time.

    3. Add a template "empty host" helper in Cena.Infrastructure.Configuration
       that wires up auth + rate limit + narrow CORS as a one-call extension
       so scaffolds cannot drift from production pattern.

  task_body: |
    # FIND-sec-002 (P0): Eliminate AllowAnyOrigin scaffolds + add CI gate

    **Files**:
      - src/api/Cena.Admin.Api.Host/Program.cs
      - src/api/Cena.Student.Api.Host/Program.cs

    **Goal**: Guarantee no `Program.cs` under `src/api/` can ever ship with
    `AllowAnyOrigin()`, regardless of whether it is a "scaffold" or not.

    **Scope**:
      1. Verify commit 5415263 (DB-06b) lands on origin/main before the next
         deployment. Coordinator confirms via `git log origin/main --oneline
         -3 | grep 5415263`. If not present: merge it.
      2. Add a grep-based CI gate (GitHub Actions workflow or pre-push hook):
         ```yaml
         - name: No wildcard CORS in API hosts
           run: |
             if git grep -n 'AllowAnyOrigin' -- 'src/api/**/Program.cs'; then
               echo "::error::AllowAnyOrigin detected in an API host Program.cs"
               exit 1
             fi
         ```
      3. Extract the common middleware pipeline (auth + rate limiter + narrow
         CORS + security headers + correlation + error + FERPA audit +
         revocation) into a single extension method, e.g.
         `app.UseCenaStudentHostPipeline()` and `app.UseCenaAdminHostPipeline()`,
         under `src/shared/Cena.Infrastructure/Configuration/`. Both hosts call
         the extension in one line and cannot diverge.

    **Definition of Done**:
      - [ ] `git grep -l 'AllowAnyOrigin' -- 'src/api/**/Program.cs'` returns nothing
      - [ ] CI job blocks PRs that reintroduce `AllowAnyOrigin` under `src/api/**/Program.cs`
      - [ ] Both hosts use the shared pipeline extension; neither duplicates middleware wiring
      - [ ] `dotnet build` + `dotnet test` green
      - [ ] Branch: `<worker>/<task-id>-sec-002-eliminate-wildcard-cors`

    **Files to read first**:
      - src/api/Cena.Admin.Api.Host/Program.cs
      - src/api/Cena.Student.Api.Host/Program.cs
      - src/api/Cena.Api.Host/Program.cs (reference for full pipeline)
      - src/shared/Cena.Infrastructure/Configuration/*

- id: FIND-sec-003
  severity: p1
  category: security
  file: src/api/Cena.Student.Api.Host/Program.cs, src/api/Cena.Admin.Api.Host/Program.cs, src/api/Cena.Api.Host/Program.cs
  line: Student 73-78; Admin 70-75; legacy 88-91
  evidence:
    - type: grep
      content: |
        $ grep -n '"dev_api_pass"' src/api/Cena.*.Host/Program.cs
        src/api/Cena.Admin.Api.Host/Program.cs:75:        ?? "dev_api_pass";
        src/api/Cena.Api.Host/Program.cs:91:        ?? "dev_api_pass";
        src/api/Cena.Student.Api.Host/Program.cs:78:        ?? "dev_api_pass";

    - type: file-extract
      content: |
        # Student.Api.Host/Program.cs lines 73-78
        var natsUser = builder.Configuration["NATS:ApiUsername"]
            ?? Environment.GetEnvironmentVariable("NATS_API_USERNAME")
            ?? "cena_api_user";
        var natsPass = builder.Configuration["NATS:ApiPassword"]
            ?? Environment.GetEnvironmentVariable("NATS_API_PASSWORD")
            ?? "dev_api_pass";

        # Contrast with Redis password (line 59-61) which DOES gate the fallback:
        options.Password = builder.Configuration["Redis:Password"]
            ?? Environment.GetEnvironmentVariable("REDIS_PASSWORD")
            ?? (builder.Environment.IsDevelopment() ? "cena_dev_redis" : null);

  finding: |
    NATS client credentials fall back to the hardcoded dev password
    `"dev_api_pass"` in all three API hosts (Student, Admin, legacy) with NO
    environment gate. If `NATS:ApiPassword` config and the `NATS_API_PASSWORD`
    env var are both missing at production boot — a common misconfiguration
    scenario — the host silently connects to NATS using the dev credential.

    The matching dev credential is also present as the docker-compose default
    (`docker-compose.yml` line 57: `NATS_API_PASSWORD=${NATS_API_PASSWORD:-dev_api_pass}`),
    so the risk is: if production deployment inherits any docker-compose
    defaults, the entire prod cluster shares one well-known NATS password.

    Redis gets this right: its fallback is explicitly gated on
    `builder.Environment.IsDevelopment() ? "cena_dev_redis" : null`. NATS should
    do the same.

  root_cause: |
    The three NATS config blocks were copy-pasted between hosts without applying
    the same dev-gate treatment used for Redis. There is no central helper like
    `CenaConnectionStrings.GetRedis` for NATS.

  proposed_fix: |
    1. Create `src/shared/Cena.Infrastructure/Configuration/CenaNatsOptions.cs`
       with a `GetNatsAuth(IConfiguration, IHostEnvironment, string usernameKey,
       string passwordKey)` helper that returns `(user, pass)` and throws
       `InvalidOperationException` if `!env.IsDevelopment()` and the password
       is null. Usage:
       ```csharp
       var (natsUser, natsPass) = CenaNatsOptions.GetApiAuth(builder.Configuration, builder.Environment);
       ```
    2. Replace the 3 copy-pasted blocks with single-line calls to the helper.
    3. Remove the hardcoded `"dev_api_pass"` literal from all three Program.cs
       files.

  task_body: |
    # FIND-sec-003 (P1): Gate NATS dev password fallback on IsDevelopment

    **Files**:
      - src/api/Cena.Student.Api.Host/Program.cs (line 73-78)
      - src/api/Cena.Admin.Api.Host/Program.cs (line 70-75)
      - src/api/Cena.Api.Host/Program.cs (line 88-91)
      - NEW: src/shared/Cena.Infrastructure/Configuration/CenaNatsOptions.cs

    **Goal**: NATS credentials must never fall back to a hardcoded password
    outside development. Production hosts should fail-fast on missing config.

    **Scope**:
      1. Extract NATS auth resolution into a single helper like the existing
         `CenaConnectionStrings` pattern. Helper throws if non-Dev + no password.
      2. Remove all three `?? "dev_api_pass"` literals from Program.cs files.
      3. Add a unit test that constructs a non-Development host environment and
         asserts the helper throws when `NATS_API_PASSWORD` is missing.

    **Definition of Done**:
      - [ ] `git grep -l 'dev_api_pass' src/api/` returns nothing
      - [ ] `dotnet test` green; new unit test covers the non-Development throw case
      - [ ] All three hosts boot successfully when `NATS_API_PASSWORD` is set
      - [ ] Attempting to boot a non-Development host without `NATS_API_PASSWORD` throws a clear `InvalidOperationException`
      - [ ] Branch: `<worker>/<task-id>-sec-003-nats-dev-password-gate`

    **Files to read first**:
      - src/shared/Cena.Infrastructure/Configuration/CenaConnectionStrings.cs (reference pattern for GetRedis/GetPostgres)
      - src/api/Cena.Student.Api.Host/Program.cs
      - src/api/Cena.Admin.Api.Host/Program.cs

- id: FIND-sec-004
  severity: p1
  category: security
  file: src/api/Cena.Student.Api.Host/Program.cs, src/api/Cena.Admin.Api.Host/Program.cs
  line: Student 34-39; Admin 31-36
  evidence:
    - type: grep
      content: |
        $ grep -n 'PiiDestructuringPolicy' src/api/Cena.*.Host/Program.cs
        src/api/Cena.Api.Host/Program.cs:50:        .Destructure.With<Cena.Infrastructure.Compliance.PiiDestructuringPolicy>();

        # Student + Admin hosts have NO match.

    - type: file-extract
      content: |
        # Student.Api.Host/Program.cs lines 33-39 (incomplete Serilog config)
        builder.Host.UseSerilog((context, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
        });

        # Contrast with Cena.Api.Host/Program.cs (legacy, correct):
        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .Destructure.With<Cena.Infrastructure.Compliance.PiiDestructuringPolicy>();
        });

  finding: |
    The FERPA-compliance PII destructuring policy (`PiiDestructuringPolicy` in
    `src/shared/Cena.Infrastructure/Compliance/PiiLogSanitizer.cs`) is registered
    on Serilog ONLY in the legacy `Cena.Api.Host`. The new `Cena.Student.Api.Host`
    and `Cena.Admin.Api.Host` Program.cs files omit it.

    Effect: every log statement in the new hosts that passes a student profile,
    email, full name, or any PII-bearing record into a Serilog template emits
    the raw field values. The `StudentDataAuditMiddleware` (FERPA audit) works
    — that tracks who accessed what — but the event-log scrub does not. This is
    a compliance-critical regression because the whole point of the destructuring
    policy is to keep student PII out of Splunk / ELK sinks.

    The policy file is correctly marked internal with an xml-doc showing the
    intended usage pattern, but nothing fails the build if a host forgets to
    call it.

  root_cause: |
    DB-06b migration copied and compressed the Serilog config block. The
    `PiiDestructuringPolicy` line (which lives on the 3-arg overload
    `UseSerilog((context, services, configuration) =>`) was dropped when the
    config was moved to the 2-arg overload `UseSerilog((context, configuration) =>`.

  proposed_fix: |
    1. Add a `.Destructure.With<PiiDestructuringPolicy>()` call to both new
       hosts' Serilog config.
    2. Add a unit test that asserts a `LogContext.PushProperty("Student", new {
       Email = "a@b" })` call produces a log event where the destructured
       property has no `Email` field.
    3. Once FIND-sec-002's shared pipeline extension lands, move the
       Serilog-with-PII-destructuring call into the shared helper so no host
       can ever drop it again.

  task_body: |
    # FIND-sec-004 (P1): Register PiiDestructuringPolicy on new API hosts

    **Files**:
      - src/api/Cena.Student.Api.Host/Program.cs (line 33-39)
      - src/api/Cena.Admin.Api.Host/Program.cs (line 30-36)

    **Goal**: PII destructuring must apply to every log event produced by
    Cena.Student.Api.Host and Cena.Admin.Api.Host.

    **Scope**:
      - Replace the 2-arg `UseSerilog((context, configuration) => ...)` with the
        3-arg `UseSerilog((context, services, configuration) => ...)` and add
        `.Destructure.With<Cena.Infrastructure.Compliance.PiiDestructuringPolicy>()`.
      - Add a unit test in `src/api/Cena.Admin.Api.Tests` that feeds a
        `LogContext.PushProperty("Student", new StudentProfile { Email = "..." })`
        through the configured logger and asserts the emitted event contains no
        `Email` key (or contains `Email = "[REDACTED]"`).
      - Add a `using Cena.Infrastructure.Compliance;` import.

    **Definition of Done**:
      - [ ] `grep -n 'PiiDestructuringPolicy' src/api/Cena.*.Host/Program.cs` returns 3 matches (one per host)
      - [ ] New unit test covers both hosts
      - [ ] `dotnet test` green
      - [ ] Branch: `<worker>/<task-id>-sec-004-pii-destructuring`

    **Files to read first**:
      - src/shared/Cena.Infrastructure/Compliance/PiiLogSanitizer.cs
      - src/api/Cena.Api.Host/Program.cs (line 50 — reference)
      - src/api/Cena.Student.Api.Host/Program.cs
      - src/api/Cena.Admin.Api.Host/Program.cs

- id: FIND-sec-005
  severity: p1
  category: security
  file: src/api/Cena.Admin.Api/FocusAnalyticsService.cs
  line: 95-116 (GetClassFocusAsync), 118-154 (GetDegradationCurveAsync), 156-170 (GetExperimentsAsync), 210-228 (GetClassHeatmapAsync)
  evidence:
    - type: grep
      content: |
        $ grep -n 'public async Task.*GetClass\|public async Task.*GetDegrad\|public async Task.*GetExperim\|public async Task.*GetClassHeat' src/api/Cena.Admin.Api/FocusAnalyticsService.cs
        95:    public async Task<ClassFocusResponse?> GetClassFocusAsync(string classId)
        118:    public async Task<FocusDegradationResponse> GetDegradationCurveAsync()
        156:    public async Task<FocusExperimentsResponse> GetExperimentsAsync()
        210:    public async Task<ClassHeatmapResponse> GetClassHeatmapAsync(string classId)

    - type: file-extract
      content: |
        # Line 95-116: GetClassFocusAsync — no ClaimsPrincipal, no SchoolId filter
        public async Task<ClassFocusResponse?> GetClassFocusAsync(string classId)
        {
            await using var session = _store.QuerySession();
            var today = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
            var since7d = today.AddDays(-7);

            var classRollup = await session.Query<ClassAttentionRollupDocument>()
                .Where(r => r.ClassId == classId)          // <-- NO SchoolId filter
                .OrderByDescending(r => r.Date)
                .Take(1)
                .ToListAsync();

            var studentRollups = await session.Query<FocusSessionRollupDocument>()
                .Where(r => r.ClassId == classId && r.Date >= since7d)  // <-- NO SchoolId filter
                .ToListAsync();
            ...
        }

        # Route wiring at AdminApiEndpoints.cs line 40-44 — no user param passed
        group.MapGet("/classes/{classId}", async (string classId, IFocusAnalyticsService service) =>
        {
            var detail = await service.GetClassFocusAsync(classId);
            return detail != null ? Results.Ok(detail) : Results.NotFound();
        }).WithName("GetClassFocus");

  finding: |
    Four endpoints on `/api/admin/focus/*` bypass tenant isolation:

      1. `GET /api/admin/focus/classes/{classId}`               → GetClassFocusAsync
      2. `GET /api/admin/focus/classes/{classId}/heatmap`       → GetClassHeatmapAsync
      3. `GET /api/admin/focus/degradation-curve`               → GetDegradationCurveAsync
      4. `GET /api/admin/focus/experiments`                     → GetExperimentsAsync

    The method signatures do not accept a `ClaimsPrincipal` parameter and the
    LINQ queries filter only by `ClassId`/`ExperimentCohort`/`Date`, never by
    `SchoolId`. The group-level `RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)`
    gate on line 25 of AdminApiEndpoints.cs ensures the caller is authenticated
    as moderator+, but a Moderator at School A can pass any `classId` they know
    (or enumerate UUIDs) and receive School B's focus analytics.

    Contrast: the sibling methods in the same file that DO accept a
    `ClaimsPrincipal` call `TenantScope.GetSchoolFilter(user)` and correctly
    filter the query (e.g. `GetOverviewAsync` line 47-64, `GetStudentFocusAsync`
    line 66-93, `GetStudentsNeedingAttentionAsync` line 172-187).

    Severity rationale: the task brief reserves P0 for state-mutating tenant
    bypass; these are read-only. Still P1 (high) — cross-tenant data disclosure
    on an education platform with FERPA obligations is a serious compliance
    violation.

  root_cause: |
    Four methods were written by a pattern that pre-dated the REV-014
    tenant-scope refactor. They were never backfilled when `TenantScope` was
    introduced. No linter or test asserts every method on `IFocusAnalyticsService`
    accepts a `ClaimsPrincipal` argument.

  proposed_fix: |
    1. Add `ClaimsPrincipal user` to all four method signatures on the
       `IFocusAnalyticsService` interface and the implementing class.
    2. In each method, call `var schoolId = TenantScope.GetSchoolFilter(user);`
       and add `.Where(x => schoolId == null || x.SchoolId == schoolId)` to the
       LINQ chain.
    3. For `GetDegradationCurveAsync` and `GetExperimentsAsync` — which are
       platform-wide curves — choose one: (a) restrict to `SUPER_ADMIN` via
       policy, or (b) bucket the response by schoolId and filter to the caller's
       school. Option (a) is simpler and matches the "platform curve" intent.
    4. Update the endpoint wiring at `AdminApiEndpoints.cs` lines 40-75 to pass
       `ClaimsPrincipal user` to each call.
    5. Add an integration test in Cena.Admin.Api.Tests that:
       - seeds a ClassFocusRollupDocument with `SchoolId = "school-A"`
       - authenticates as a moderator with `school_id = "school-B"`
       - calls `GET /api/admin/focus/classes/{classA-id}`
       - asserts 404 or empty response (NOT 200 with school-A data)

  task_body: |
    # FIND-sec-005 (P1): Tenant isolation bypass in 4 Focus Analytics read endpoints

    **File**: src/api/Cena.Admin.Api/FocusAnalyticsService.cs
    **Endpoints**:
      - GET /api/admin/focus/classes/{classId}
      - GET /api/admin/focus/classes/{classId}/heatmap
      - GET /api/admin/focus/degradation-curve
      - GET /api/admin/focus/experiments

    **Goal**: Every `IFocusAnalyticsService` method must filter by the caller's
    school scope — no exceptions. A Moderator from School A must never see
    School B data through this surface.

    **Scope**:
      1. Add `ClaimsPrincipal user` parameter to all 4 methods on the interface
         and implementation. Match the signatures of `GetOverviewAsync` and
         `GetStudentsNeedingAttentionAsync` in the same file.
      2. Call `TenantScope.GetSchoolFilter(user)` at the top and apply the
         filter to every document query. Reference: lines 47-64 in the same file.
      3. For `GetDegradationCurveAsync` and `GetExperimentsAsync`: either apply
         the same school filter OR restrict the endpoint to
         `CenaAuthPolicies.SuperAdminOnly`. Choose and document the choice in
         the commit message.
      4. Update `src/api/Cena.Admin.Api/AdminApiEndpoints.cs` lines 40-75 so
         every call passes `ClaimsPrincipal user` down from the endpoint
         handler.
      5. Add unit tests in `Cena.Admin.Api.Tests/Admin/FocusAnalyticsMappingTests.cs`
         (or a new `FocusAnalyticsTenantScopeTests.cs`) asserting that a
         moderator for school A sees NO data from school B for each of the 4
         methods. Test must fail on today's code and pass after the fix.

    **Definition of Done**:
      - [ ] All 8 `session.Query<...>` calls in FocusAnalyticsService.cs:95-228 include a `.Where(r => r.SchoolId == schoolId)` clause gated on `schoolId != null`
      - [ ] Interface `IFocusAnalyticsService` methods all take `ClaimsPrincipal`
      - [ ] New tests prove cross-tenant disclosure is blocked
      - [ ] `dotnet test` green (total tests increased by N)
      - [ ] Branch: `<worker>/<task-id>-sec-005-focus-tenant-scope`

    **Files to read first**:
      - src/api/Cena.Admin.Api/FocusAnalyticsService.cs
      - src/shared/Cena.Infrastructure/Tenancy/TenantScope.cs
      - src/api/Cena.Admin.Api/AdminApiEndpoints.cs (lines 22-77)
      - src/api/Cena.Admin.Api/MasteryTrackingService.cs (reference — already filters correctly)

- id: FIND-sec-006
  severity: p1
  category: security
  file: src/api/Cena.Api.Host/appsettings.json, src/api/Cena.Admin.Api.Host/appsettings.json, src/api/Cena.Student.Api.Host/appsettings.json
  line: 3 (all three files)
  evidence:
    - type: grep
      content: |
        $ grep -n 'Password' src/api/Cena.*.Host/appsettings.json
        src/api/Cena.Admin.Api.Host/appsettings.json:3: "PostgreSQL": "Host=localhost;Port=5433;Database=cena;Username=cena_admin;Password=cena_admin_dev_password",
        src/api/Cena.Api.Host/appsettings.json:3: "PostgreSQL": "Host=localhost;Port=5433;Database=cena;Username=cena;Password=cena_dev_password",
        src/api/Cena.Student.Api.Host/appsettings.json:3: "PostgreSQL": "Host=localhost;Port=5433;Database=cena;Username=cena_student;Password=cena_student_dev_password",

        $ grep -n 'ConnectionStrings' src/api/Cena.Api.Host/appsettings.Production.json
        # No match — production override is empty for ConnectionStrings.

    - type: file-extract
      content: |
        # src/api/Cena.Api.Host/appsettings.Production.json — entire file
        {
          "AllowedHosts": "admin.cena.edu;api.cena.edu"
        }

  finding: |
    Every API host has its PostgreSQL password hardcoded in the *base*
    `appsettings.json` — the file that ships in the Docker image and is the
    fallback when environment overrides are missing. Values:

      - `cena_dev_password` (legacy Api.Host)
      - `cena_admin_dev_password` (Admin.Api.Host)
      - `cena_student_dev_password` (Student.Api.Host)

    `appsettings.Production.json` for Cena.Api.Host overrides only `AllowedHosts`
    — it does NOT override the connection string. For Student + Admin hosts
    there is no Production file at all, so the dev password is effectively the
    production default.

    This is not an immediate leak of *real* credentials — `cena_dev_password`
    is a well-known development placeholder — but:

      1. It trains deployers to expect "the base file has a password" and
         gives them a template to paste real credentials into (the exact
         anti-pattern that caused REV-001).

      2. If environment variables are not fully exported at boot (a common prod
         incident), the host connects to whatever Postgres is reachable at
         `localhost:5433` using `cena_dev_password`. On a shared prod host with
         multiple services, that may be a real dev database.

      3. The format makes it trivial for `git grep -n 'Password='` to match
         and shows 3 hits on the main branch, which raises false-positive
         secret-scanner alerts and makes real leaks harder to triage.

  root_cause: |
    Early dev experience optimisation: the author wanted `dotnet run` to work
    with zero env-var setup. The `CenaConnectionStrings.GetPostgres` helper
    exists (`src/shared/Cena.Infrastructure/Configuration/CenaConnectionStrings.cs`)
    and already gates the fallback on `IsDevelopment`, but the Postgres
    connection string in `appsettings.json` bypasses the helper — the raw
    string is passed straight to Marten.

  proposed_fix: |
    1. Replace the `ConnectionStrings.PostgreSQL` value in all three base
       `appsettings.json` with `""` (empty string) or remove the key entirely.
    2. Move the dev password literal into
       `CenaConnectionStrings.GetPostgres(config, env)` behind the existing
       `IsDevelopment()` gate — it already knows how to return
       `"Host=localhost;...Password=cena_dev_password"` when env is dev.
    3. Add a startup assertion: if `!env.IsDevelopment()` and the resolved
       connection string is empty, throw a clear `InvalidOperationException`
       before any service is registered.
    4. Add a CI grep-gate: `git grep -l 'cena.*dev_password' -- 'src/**/*.json'`
       must return nothing.

  task_body: |
    # FIND-sec-006 (P1): Remove hardcoded dev passwords from appsettings.json

    **Files**:
      - src/api/Cena.Api.Host/appsettings.json (line 3)
      - src/api/Cena.Admin.Api.Host/appsettings.json (line 3)
      - src/api/Cena.Student.Api.Host/appsettings.json (line 3)

    **Goal**: No file under `src/api/**/appsettings.json` (or any tracked config)
    contains a hardcoded database password literal.

    **Scope**:
      1. Empty-out or delete the `ConnectionStrings.PostgreSQL` key in all
         three base `appsettings.json` files.
      2. Verify `CenaConnectionStrings.GetPostgres` is what the hosts actually
         call. If they use `Configuration.GetConnectionString("PostgreSQL")`
         directly, migrate them to the helper (reference: how Redis and NATS
         are resolved).
      3. In the helper, gate the `"Host=localhost;...;Password=cena_dev_password"`
         fallback on `env.IsDevelopment()`.
      4. Throw `InvalidOperationException` at boot if a non-Development host
         cannot resolve a connection string.
      5. Add CI gate: `git grep -l 'dev_password' -- 'src/**/*.json'` exits with
         non-zero if any hits.

    **Definition of Done**:
      - [ ] `git grep -n 'Password=' -- 'src/**/*.json'` returns zero matches
      - [ ] Dev boot (`dotnet run` in each host) still works without setting any env vars
      - [ ] Setting `ASPNETCORE_ENVIRONMENT=Production` without a connection string causes a clean startup failure (not a silent connect to localhost)
      - [ ] `dotnet test` green
      - [ ] Branch: `<worker>/<task-id>-sec-006-no-hardcoded-passwords`

    **Files to read first**:
      - src/shared/Cena.Infrastructure/Configuration/CenaConnectionStrings.cs
      - src/api/Cena.*.Host/Program.cs
      - src/api/Cena.*.Host/appsettings.json
      - src/api/Cena.*.Host/appsettings.Production.json

- id: FIND-sec-007
  severity: p1
  category: security
  file: src/api/Cena.Admin.Api.Host/Program.cs, src/api/Cena.Student.Api.Host/Program.cs
  line: Admin 90-96, 225-250; Student 111-118, 248-295
  evidence:
    - type: grep
      content: |
        $ grep -n 'FirebaseClaimsSeeder\|ApplicationStarted' src/api/Cena.*.Host/Program.cs
        src/api/Cena.Api.Host/Program.cs:334:lifetime.ApplicationStarted.Register(async () =>
        src/api/Cena.Api.Host/Program.cs:343:    await FirebaseClaimsSeeder.SyncAdminClaimsAsync(appLogger);

        # Student + Admin hosts: no matches — no application-started hook.

    - type: file-extract
      content: |
        # Cena.Api.Host/Program.cs lines 331-344 (legacy, has the hook)
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        var appLogger = app.Services.GetRequiredService<ILogger<Program>>();
        lifetime.ApplicationStarted.Register(async () =>
        {
            var store = app.Services.GetRequiredService<IDocumentStore>();
            await DatabaseSeeder.SeedAllAsync(store, appLogger, 300, ...);

            // Ensure Firebase Admin SDK is initialized, then sync claims for demo users
            _ = app.Services.GetRequiredService<IFirebaseAdminService>();
            await FirebaseClaimsSeeder.SyncAdminClaimsAsync(appLogger);
        });

        # Cena.Admin.Api.Host/Program.cs end — no lifetime/seed block
        # (the file ends at line 246 with app.Run())

  finding: |
    `Cena.Admin.Api.Host` registers `IFirebaseAdminService` as a singleton (line
    90) but never resolves it at startup and never calls
    `FirebaseClaimsSeeder.SyncAdminClaimsAsync`. `Cena.Student.Api.Host` does not
    even register `IFirebaseAdminService`. The Firebase Admin SDK
    (`FirebaseApp.DefaultInstance`) therefore initialises lazily on first call
    — and on both new hosts there is no first call today, meaning:

      1. **No fail-fast**: if the Firebase service-account JSON path is wrong
         or ADC is not configured, the host boots green and only fails when an
         admin endpoint first touches user-management. In production, that
         surprise is a pager event, not a deployment-time error.

      2. **Demo-user claim sync is silently skipped**: the legacy host uses
         `FirebaseClaimsSeeder.SyncAdminClaimsAsync` to ensure the three demo
         accounts (`admin@cena.edu`, `teacher@cena.edu`, `parent@cena.edu`)
         have the correct `role` custom claim. If someone resets Firebase and
         boots only the new Admin.Api.Host, those claims go missing and demo
         users log in but cannot pass `CenaAuthPolicies.ModeratorOrAbove`
         — every admin endpoint returns 403 with no clear signal that a
         startup step was omitted.

      3. **Operational drift**: the legacy host's lifetime hook also runs
         `DatabaseSeeder.SeedAllAsync`. Neither new host does, so a fresh
         Postgres boot under the new hosts starts empty (no seeded roles,
         question bank, etc.). DB-06b clearly intended to migrate these
         concerns, but the lifetime block was dropped.

  root_cause: |
    The DB-06b migration copied the services block and endpoint mappings from
    Cena.Api.Host but omitted the `lifetime.ApplicationStarted.Register` block
    — presumably because the author was focused on "same requests, new host"
    and forgot the startup hooks.

  proposed_fix: |
    1. Port the `lifetime.ApplicationStarted.Register(async () => { … })` block
       from Cena.Api.Host lines 331-344 to Cena.Admin.Api.Host. Admin.Api.Host
       should seed everything that has admin surface + sync demo admin claims.
    2. Port a narrower version to Cena.Student.Api.Host — no admin claim sync
       (Student host shouldn't do admin-user provisioning), but it should
       still seed the role table and question bank so a fresh Student host
       can answer requests without Admin.Api.Host being up.
    3. Consider promoting the startup block into a shared
       `CenaHostBootstrap.InitializeAsync(app)` extension so future hosts don't
       forget.

  task_body: |
    # FIND-sec-007 (P1): Port application-started startup hook to new hosts

    **Files**:
      - src/api/Cena.Admin.Api.Host/Program.cs
      - src/api/Cena.Student.Api.Host/Program.cs
      - Reference: src/api/Cena.Api.Host/Program.cs lines 331-344

    **Goal**: The new hosts must fail-fast at boot if Firebase Admin SDK cannot
    initialise, and must seed the database so endpoints work immediately after
    `app.Run()` returns healthy.

    **Scope**:
      1. Admin.Api.Host: add a `lifetime.ApplicationStarted.Register` block that
         resolves `IDocumentStore`, runs `DatabaseSeeder.SeedAllAsync` with
         whatever seeders are relevant to admin concerns, resolves
         `IFirebaseAdminService` (forcing init), and calls
         `FirebaseClaimsSeeder.SyncAdminClaimsAsync`.
      2. Student.Api.Host: add a narrower version that seeds roles + question
         bank, and resolves any Firebase-backed service it uses. If Student
         host never touches Firebase Admin SDK, just seed the DB.
      3. Extract the shared pattern into `CenaHostBootstrap.InitializeAsync`
         under `src/shared/Cena.Infrastructure/Configuration/`.

    **Definition of Done**:
      - [ ] Both new hosts have a `lifetime.ApplicationStarted.Register` hook
      - [ ] Admin.Api.Host resolves `IFirebaseAdminService` at startup and logs success/failure
      - [ ] `dotnet test` green
      - [ ] Branch: `<worker>/<task-id>-sec-007-host-bootstrap`

    **Files to read first**:
      - src/api/Cena.Api.Host/Program.cs (lines 331-344)
      - src/shared/Cena.Infrastructure/Firebase/FirebaseAdminService.cs
      - src/shared/Cena.Infrastructure/Auth/*FirebaseClaimsSeeder*  (if the file exists)
      - src/shared/Cena.Infrastructure/Seed/DatabaseSeeder.cs

- id: FIND-sec-008
  severity: p2
  category: security
  file: src/api/Cena.Student.Api.Host/appsettings.json, src/api/Cena.Admin.Api.Host/appsettings.json
  line: whole file
  evidence:
    - type: grep
      content: |
        $ grep -l 'Cors:AllowedOrigins\|"Cors"' src/api/Cena.*.Host/appsettings*.json
        src/api/Cena.Api.Host/appsettings.json

        # Student and Admin Host appsettings have no Cors section.
        # Their Program.cs files fall back to a hardcoded single-origin default.

    - type: file-extract
      content: |
        # Student.Api.Host/Program.cs lines 120-134 — CORS resolution
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:5174" };

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins(allowedOrigins)
                    .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
                    .WithHeaders("Authorization", "Content-Type", "Accept", "X-Requested-With",
                        "X-SignalR-User-Agent")
                    .AllowCredentials();
            });
        });

  finding: |
    The Student + Admin host `appsettings.json` files do not declare a `Cors`
    section. The Program.cs fallback is a single hardcoded origin per host
    (`localhost:5174` for Student, `localhost:5175` for Admin). In production
    this means:

      1. The frontend CORS allow-list must be provided via environment variable
         (`Cors__AllowedOrigins__0=https://cena.edu`, etc.). If not, CORS fails
         for every real frontend — the frontend gets a blocked preflight and
         the user sees a broken page.

      2. Unlike `AllowAnyOrigin` this is fail-secure (requests get blocked,
         not allowed), so it is not a P0/P1. It is a P2 "operational
         configuration gap" because the legacy `Cena.Api.Host/appsettings.json`
         (line 11-13) declares `Cors:AllowedOrigins` explicitly with both the
         dev origin and `https://admin.cena.edu`, so someone reading the new
         hosts' configs might reasonably assume the same default is in place.

    Additionally, the docker-compose environment section does not set
    `Cors__AllowedOrigins__0` for either host, so a docker-compose-driven prod
    deploy would break frontends until someone sets it manually.

  root_cause: |
    DB-06b migration did not copy the `Cors` section from the legacy
    `appsettings.json` into the new hosts' files.

  proposed_fix: |
    1. Add a `Cors.AllowedOrigins` array to both new hosts' base `appsettings.json`
       containing just the dev origins (dev-only).
    2. Add a `Cors.AllowedOrigins` array to each host's
       `appsettings.Production.json` (create the file for Student + Admin
       hosts) with the real production domains.
    3. Add the matching env var placeholders to `docker-compose.yml` so a
       compose-driven deploy can inject them.
    4. Document in the host README.md files that `Cors__AllowedOrigins__N` is
       a required env var in non-Development environments.

  task_body: |
    # FIND-sec-008 (P2): Declare CORS allow-lists for new hosts

    **Files**:
      - src/api/Cena.Student.Api.Host/appsettings.json
      - src/api/Cena.Student.Api.Host/appsettings.Production.json (create)
      - src/api/Cena.Admin.Api.Host/appsettings.json
      - src/api/Cena.Admin.Api.Host/appsettings.Production.json (create)
      - docker-compose.yml (add env vars for both hosts)

    **Scope**: Backport `Cors.AllowedOrigins` declaration from
    Cena.Api.Host/appsettings.json (lines 11-13) to the new hosts. Production
    files should contain the actual customer-facing origins (`https://cena.edu`,
    `https://admin.cena.edu`, or whatever the real domains are — ask the
    coordinator if unknown).

    **Definition of Done**:
      - [ ] Both new hosts have a `Cors.AllowedOrigins` in base appsettings.json (dev only)
      - [ ] Both new hosts have a Production override file with real origins
      - [ ] docker-compose.yml passes `Cors__AllowedOrigins__0` to both hosts
      - [ ] Branch: `<worker>/<task-id>-sec-008-cors-config`

    **Files to read first**:
      - src/api/Cena.Api.Host/appsettings.json (reference)
      - src/api/Cena.Student.Api.Host/Program.cs (lines 120-134)
      - src/api/Cena.Admin.Api.Host/Program.cs (lines 99-111)
      - docker-compose.yml

## Not-findings (investigated, cleared)

Briefly listing what I checked and accepted as OK, so the reviewer knows the
scope of the sweep:

- **Firebase JWT validation** — `FirebaseAuthExtensions.cs` correctly validates
  issuer, audience, lifetime via `JwtBearer` + JWKS auto-fetch from
  `https://securetoken.google.com/{projectId}`. Signing key pinning and
  rotation are handled by the framework.
- **Tenant extraction from JWT claims** — `CenaClaimsTransformer.cs` reads
  `school_id` from the validated JWT only; no codepath pulls tenant from
  request body/header/query. `TenantScope.GetSchoolFilter` throws on missing
  claim for non-SUPER_ADMIN.
- **IDOR prevention** — `ResourceOwnershipGuard.VerifyStudentAccess` is called
  on every per-student Student.Api.Host endpoint (verified by `grep -c`:
  ChallengesEndpoints=9, NotificationsEndpoints=9, KnowledgeEndpoints=8,
  GamificationEndpoints=5, TutorEndpoints=5, SessionEndpoints covers each
  mutation, StudentAnalyticsEndpoints covers each read).
- **Admin endpoint authorisation** — every `MapGroup("/api/admin/*")` in
  `src/api/Cena.Admin.Api/*Endpoints*.cs` has a group-level
  `RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove|AdminOnly|SuperAdminOnly)`
  attached. Verified by enumerating every `.MapPost/.MapPut/.MapDelete/.MapPatch`
  under src/api/Cena.Admin.Api/ and confirming each is inside such a group.
  The hardest-to-verify ones — `POST /api/admin/mastery/students/{id}/methodology-override`,
  `POST /api/admin/system/reseed`, `POST /api/admin/system/clean-reseed`,
  `POST /api/admin/ingestion/upload` — all pass.
- **Destructive seed endpoints** — `/api/admin/system/reseed` and `/clean-reseed`
  are explicitly gated on `env.IsDevelopment()` and tagged with
  `RequireRateLimiting("destructive")` (AdminApiEndpoints.cs lines 449-475).
- **AI endpoint rate-limiting** — `/api/admin/ai/*` group has
  `.RequireRateLimiting("ai")` (AdminApiEndpoints.cs line 709). Tutor streaming
  has `.RequireRateLimiting("tutor")` (TutorEndpoints.cs line 42). Non-streaming
  tutor send is a placeholder (does not call LLM) so "api" rate limit is fine.
- **SignalR hub auth** — `CenaHub.cs` has `[Authorize]` at class level, and
  `OnConnectedAsync` aborts the connection if the `student_id` claim is missing.
- **Parameterised pgvector search** — `EmbeddingAdminService.cs` and
  `Cena.Actors.Services.EmbeddingService` both use positional parameters
  ($1..$N) correctly. The only raw-SQL file that gets it wrong is
  `LeaderboardService.cs` (FIND-sec-001).
- **Path traversal in cloud-dir ingest** — `IngestionPipelineCloudDir.cs`
  lines 47-54 and 103-109 call `Path.GetFullPath` and check the result is a
  `StartsWith` an allowed directory, and lines 138-144 re-check inside the
  file loop. Guard is correct.
- **Secrets in git history** — scanned via `git log --all -p -S "sk-ant" / "sk-proj" / "BEGIN PRIVATE KEY" / "AIza"`. Only hit is a REV-001 report that
  *mentions* the Firebase service-account key path (no key value). REV-001 is
  already in the backlog per task brief — not re-filed.
- **Frontend Firebase apiKey exposure** — `src/admin/full-version/src/plugins/firebase.ts:5`
  reads `import.meta.env.VITE_FIREBASE_API_KEY`; no hardcoded value in tracked
  files. Firebase web API keys are public identifiers by design — not a secret.
- **Revocation middleware** — `TokenRevocationMiddleware.cs` correctly
  checks Redis for `revoked:{uid}` and fails closed on a 401 with code
  `CENA_AUTH_TOKEN_REVOKED`. Local MemoryCache fallback is bounded (5-min TTL,
  10k-entry cap) and invalidated when a revocation is seen.

## Enqueued Tasks

- FIND-sec-001 → t_27a595bd9212 (critical — LeaderboardService SQLi)
- FIND-sec-002 → t_ffe63b9416ad (critical — AllowAnyOrigin scaffolds + CI gate)
- FIND-sec-003 → t_b55ab7978b06 (high    — NATS dev-password fallback)
- FIND-sec-004 → t_b6e7a533f3d3 (high    — PiiDestructuringPolicy missing)
- FIND-sec-005 → t_530c3515c737 (high    — Focus Analytics tenant isolation)
- FIND-sec-006 → t_029629143fa7 (high    — hardcoded dev passwords in appsettings)
- FIND-sec-007 → t_393c8c9cba34 (high    — host bootstrap hook missing)

FIND-sec-008 (P2 — CORS allow-list missing from new hosts) not enqueued per
task-brief rule "Enqueue for P0/P1 only".
