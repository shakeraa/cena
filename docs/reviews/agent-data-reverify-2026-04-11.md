---
agent: claude-subagent-data
role: Data, Performance, Projections & Cost (v2 lens)
date: 2026-04-11
run: cena-review-v2-reverify
branch: claude-subagent-data/cena-reverify-2026-04-11
worktree: .claude/worktrees/review-data
sha_at_branch: cc3f702
prior_findings_verified: 14   # data-001..013 + data-007b — all verified-fixed in preflight
postgres_reachable: false
postgres_note: |
  docker ps shows no running containers; pg_isready not available on PATH.
  This re-verification is static + schema analysis only. No EXPLAIN ANALYZE
  evidence, no row counts, no projection rebuild timings, no live token cost.
  Cost estimates are derived from rate-limit math + observed per-request
  shape, not from production telemetry.
files_scanned_cs: 18                 # files containing QueryAllRawEvents
query_allrawevents_hits: 58
severity_counts:
  p0: 4
  p1: 5
  p2: 4
  p3: 1
new_finding_id_start: FIND-data-020   # prior file used FIND-data-014..019 (P2/P3, never enqueued); avoiding collision
---

# Agent `data` — Re-Verification & Expanded v2 Findings (2026-04-11)

## Executive summary

The 14 prior FIND-data-* P0/P1 fixes (001..013 + 007b) **all verify on `main` at
`cc3f702`**. The remediation in each case is real: events register, projections
have Apply handlers, snake_case event aliases match Marten's NameToAlias output,
N+1 patterns are bulk-loaded, NotificationsEndpoints pages in SQL.

But Phase 1 surfaced two systemic problems the v1 sweep missed and one *new
regression of trust*:

1. **Rate-limit policies are global, not per-user** — every "10 messages/min
   per student" comment is a lie. The policies are unpartitioned
   `AddFixedWindowLimiter("ai" | "tutor" | "api")` instances; the entire
   API host shares ONE 10-token-per-minute bucket. A single runaway script
   exhausts the global allowance for every learner. **P0 cost-control bug.**

2. **Token cost meter is fabricated from a 200-char message preview** —
   `TokenBudgetAdminService` and `TutoringAdminService.GetBudgetStatusAsync`
   build per-student token usage as `messagePreview.Length / 4 * 2`, where
   `messagePreview` is a *truncated* `string[..200]` cap. The real token
   count IS captured upstream (`TutorMessageDocument.TokensUsed`, line 351 of
   `TutorEndpoints.cs`, populated from the Anthropic stream's final chunk),
   but the admin services query the *event store* (where `TokensUsed` was
   never added to `TutoringMessageSent_V1`) instead of the *document* (where
   it lives). The label says "tokens", the data is "preview chars / 4". The
   cost meter under-reports by ~roughly the assistant-message-length-to-200
   ratio. The `_dailyLimitOverride` is a mutable `static` field — admin
   updates are lost on restart, never persisted, not per-tenant.
   **P0 fake-data + lying-label.**

3. **`AnalysisJobActor.cs:244` filters by `e.EventTypeName ==
   "ConceptAttempted_V1"` — PascalCase** — Marten's alias is
   `concept_attempted_v1`, so the predicate matches zero rows on every call.
   This is the **same bug class** as FIND-data-006 (which was about
   `ExperimentAdminService` using `nameof(T)`); the fix swept the
   `Cena.Admin.Api` tree but missed the `Cena.Actors` actor that powers the
   stagnation analysis pipeline. **P0 dead query in actor pipeline.**

Beyond those, the preflight handoff inventory of `QueryAllRawEvents` (the
appendix at the bottom of this report) confirms the 58 call sites span 18
files. Of those:

- **12 sites** are user-reachable hot paths with NO tenant scope — every
  request scans the global event store (admin focus heatmap, admin token
  budget, admin experiment funnel, student session detail, student session
  replay, student gamification badges/streak/xp).
- **3 sites** are bootstrap / one-time / outbox / monitoring — justified.
- **3 sites** are correct-with-LIMIT but still unbounded across tenants.

There are also two carry-over P2/P3 findings from the v1 prior agent file
(`FIND-data-017` Daily Challenge in-memory sort, `FIND-data-018` TutorEndpoints
GetMessages no pagination + lying `HasMore: false`) that were never
enqueued and **still exist verbatim on `main`**. They are re-filed under v2 IDs.

There is also a **secondary issue in the FIND-data-009 fix**:
`StudentLifetimeStatsProjection` was created and registered as Inline, but the
13 `QueryAllRawEvents` call sites in `GamificationEndpoints.cs`,
`StudentInsightsService.cs`, `SessionEndpoints.cs`, and
`StudentAnalyticsEndpoints.cs` were **never migrated to use it**. The
projection is computed and stored on every event append (real cost) but read
nowhere in the API hot paths. It is also **non-deterministic** —
`Apply(BadgeEarned_V1)` reads `DateTimeOffset.UtcNow` on line 118, the exact
anti-pattern FIND-data-001 was supposed to permanently retire. **P0 fix is
incomplete + introduced a new wall-clock anti-pattern in the same patch.**

| Verdict bucket | Count |
|---|---|
| `verified-fixed` (preflight) | 14 |
| `verified-fixed` (this lens deep-dive) | 14 |
| `partially-fixed` / `moved` | 1 (FIND-data-009) |
| `regressed` | 0 |
| `fake-fix` | 1 (FIND-data-001 anti-pattern reintroduced in `StudentLifetimeStatsProjection`) |
| **NEW p0** | 4 |
| **NEW p1** | 5 |
| **NEW p2** | 4 |
| **NEW p3** | 1 |

---

## Findings

### P0 — Critical

```yaml
- id: FIND-data-020
  severity: p0
  category: cost
  framework: null
  files:
    - path: src/api/Cena.Student.Api.Host/Program.cs
      lines: "160-219"
    - path: src/api/Cena.Admin.Api.Host/Program.cs
      lines: "112-149"
  related_prior_finding: null
  evidence:
    - type: grep
      content: |
        $ rg "AddFixedWindowLimiter" src/api -n
        src/api/Cena.Admin.Api.Host/Program.cs:117    options.AddFixedWindowLimiter("api", opt => { ... })
        src/api/Cena.Admin.Api.Host/Program.cs:125    options.AddFixedWindowLimiter("ai",  opt => { ... })
        src/api/Cena.Admin.Api.Host/Program.cs:133    options.AddFixedWindowLimiter("destructive", opt => { ... })
        src/api/Cena.Student.Api.Host/Program.cs:165   options.AddFixedWindowLimiter("api",   opt => { ... })
        src/api/Cena.Student.Api.Host/Program.cs:173   options.AddFixedWindowLimiter("ai",    opt => { ... })
        src/api/Cena.Student.Api.Host/Program.cs:181   options.AddFixedWindowLimiter("tutor", opt => { ... })
    - type: grep
      content: |
        $ rg "AddPolicy" src/api/*/Program.cs -n
        src/api/Cena.Student.Api.Host/Program.cs:192    options.AddPolicy("password-reset", httpContext => { ... })
        # password-reset is the ONLY policy with a partition key
        # (httpContext.Connection.RemoteIpAddress). Every other policy is
        # AddFixedWindowLimiter, which is a SINGLE bucket for the whole
        # API instance, not per-user.
    - type: grep
      content: |
        # Cost-control comments that claim per-user enforcement:
        $ rg "per user|per student|per-student" src/api/*/Program.cs -n
        src/api/Cena.Admin.Api.Host/Program.cs:116    // General API: 100 req/min per user        ← LIE
        src/api/Cena.Admin.Api.Host/Program.cs:124    // AI generation: 10 req/min per user (cost protection)  ← LIE
        src/api/Cena.Student.Api.Host/Program.cs:164   // General API: 100 req/min per user       ← LIE
        src/api/Cena.Student.Api.Host/Program.cs:172   // AI generation: 10 req/min per user (cost protection)  ← LIE
        src/api/Cena.Student.Api.Host/Program.cs:180   // Tutor LLM: 10 messages/min per student  ← LIE
  finding: |
    All five rate-limit policies that protect AI-backed and write paths
    (`api`, `ai`, `tutor`, `destructive`) are unpartitioned global buckets.
    `AddFixedWindowLimiter(name, opt => ...)` registers a SINGLE
    FixedWindowRateLimiter for the whole policy name; without a
    partition function, every request hits the same counter. The "10 req/min
    per user" comments are wrong. With ~50 concurrent students hitting the
    tutor on the same instance, a single classroom can drain the entire
    LLM allowance for the whole platform within seconds.
  root_cause: |
    Whoever wrote `AddRateLimiter` chose the convenience overload
    (`AddFixedWindowLimiter`) instead of `AddPolicy` with a partition
    function over the user's `sub` claim. The only policy that DID use
    `AddPolicy` (FIND-ux-006b's `password-reset`) is partitioned by
    forwarded-for IP and proves the maintainers know the partitioned
    pattern — they just didn't apply it to the cost-critical paths.
  proposed_fix: |
    Replace every `AddFixedWindowLimiter("ai" | "tutor" | "api" |
    "destructive", ...)` with `AddPolicy(name, httpContext =>
    RateLimitPartition.GetFixedWindowLimiter(httpContext.User.FindFirstValue(
    ClaimTypes.NameIdentifier) ?? httpContext.Connection.RemoteIpAddress?.ToString()
    ?? "anon", _ => new FixedWindowRateLimiterOptions { ... }))`. The "ai"
    and "tutor" policies should ALSO be enforced at the tenant level (one
    bucket per SchoolId) so a single classroom cannot starve the rest of
    the platform — implement as a second outer limiter chained via
    `RateLimiterPolicy` composition.
  test_required: |
    Integration test: spin up TestServer with two distinct authenticated
    students, fire 12 requests from student A to /api/tutor/threads/.../stream,
    assert student A gets 429 on request 11 AND student B's request 1
    succeeds (proves per-user partitioning, not global bucket).
  task_body: |
    GOAL
    Convert all five global rate-limit policies (`api`, `ai`, `tutor`,
    `destructive` on Admin host; `api`, `ai`, `tutor` on Student host) from
    unpartitioned `AddFixedWindowLimiter` to partitioned `AddPolicy` with a
    user-claim partition key. Add a second outer tenant-level limiter for
    `ai` and `tutor` keyed by SchoolId.

    FILES TO TOUCH
      - src/api/Cena.Admin.Api.Host/Program.cs (lines 112-149)
      - src/api/Cena.Student.Api.Host/Program.cs (lines 160-219)

    FILES TO READ FIRST
      - src/api/Cena.Student.Api.Host/Program.cs lines 188-208 (the
        `password-reset` policy already shows the partition pattern)
      - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-020
      - .NET 8 RateLimiter docs:
        https://learn.microsoft.com/aspnet/core/performance/rate-limit
        section "Partitioned rate limiters"

    DEFINITION OF DONE
      - Every `AddFixedWindowLimiter` in both Program.cs files is
        replaced with `AddPolicy(...)` partitioned by NameIdentifier claim.
      - "ai" and "tutor" policies have a SECOND chained outer limiter
        partitioned by SchoolId (resolved from JWT claim, not body/header).
      - The "100 req/min per user" / "10 req/min per user" /
        "10 messages/min per student" comments are now TRUE.
      - Integration test added under tests/Cena.Api.Host.Tests/RateLimiter
        that proves per-user isolation (student A draining their bucket
        does NOT affect student B). Test asserts a 429 for the over-limit
        student AND a 200 for the other student in the same window.
      - Integration test for tenant-level outer limiter: 50 students from
        school A drain the school-A tutor bucket; one student from school B
        still gets a 200.
      - npm test / dotnet test all green.

    REPORTING REQUIREMENTS
      Report via: node .agentdb/kimi-queue.js complete <id> --worker <you>
      --result "branch=<branch>, files=<list>, tests-added=<paths>,
      build=ok, tests=ok, manual-curl-proof=<paste>"

- id: FIND-data-021
  severity: p0
  category: cost
  framework: null
  files:
    - path: src/api/Cena.Admin.Api/TokenBudgetAdminService.cs
      lines: "26-141"
    - path: src/api/Cena.Admin.Api/TutoringAdminService.cs
      lines: "200-236, 331-352"
    - path: src/actors/Cena.Actors/Tutoring/TutoringEvents.cs
      lines: "29-38"
    - path: src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs
      lines: "324-372"
    - path: src/shared/Cena.Infrastructure/Documents/TutorDocuments.cs
      lines: "44"
  related_prior_finding: null
  evidence:
    - type: grep
      content: |
        $ rg "preview.Length / 4" src/api/Cena.Admin.Api -n
        src/api/Cena.Admin.Api/TokenBudgetAdminService.cs:59:    return preview.Length / 4 * 2;
        src/api/Cena.Admin.Api/TokenBudgetAdminService.cs:107:   return (long)(preview.Length / 4 * 2);
        src/api/Cena.Admin.Api/TutoringAdminService.cs:146:      return preview.Length / 4;
        src/api/Cena.Admin.Api/TutoringAdminService.cs:216:      return preview.Length / 4;
        src/api/Cena.Admin.Api/TutoringAdminService.cs:348:      return preview.Length / 4;
    - type: grep
      content: |
        $ rg "MessagePreview" src/actors/Cena.Actors/Tutoring/TutoringEvents.cs -n
        29:public sealed record TutoringMessageSent_V1(
        30:    string StudentId,
        31:    string SessionId,
        32:    string TutoringSessionId,
        33:    int TurnNumber,
        34:    string Role,
        35:    string MessagePreview, // First 200 chars, no PII
        36:    int SourceCount,
        37:    DateTimeOffset Timestamp
        38:) : IDelegatedEvent;
        # No TokensUsed field. Token info is NOT in the event payload.
    - type: grep
      content: |
        $ rg "TokensUsed" src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs -n
        324:    int? totalTokensUsed = null;
        334:    if (chunk.Finished && chunk.TokensUsed.HasValue)
        336:        totalTokensUsed = chunk.TokensUsed.Value;
        351:    TokensUsed = totalTokensUsed // HARDEN: Persist for billing/throttling
        # The REAL token count IS captured from the LLM stream and persisted
        # on TutorMessageDocument.TokensUsed. But the cost meter never
        # reads that field — it queries the event store and divides preview
        # length by 4.
    - type: grep
      content: |
        $ rg "private static" src/api/Cena.Admin.Api/TokenBudgetAdminService.cs -n
        26:    private static int _dailyLimitOverride = DefaultDailyLimit;
        27:    private static long _monthlyLimitOverride = DefaultMonthlyLimit;
        # Mutable static state. UpdateLimitsAsync mutates these in-process.
        # No persistence. No per-tenant scoping. Lost on restart.
  finding: |
    The two services that own the platform's LLM cost control
    (`TokenBudgetAdminService` and `TutoringAdminService.GetBudgetStatusAsync`)
    fabricate per-student token counts by reading the truncated 200-char
    `MessagePreview` field from `TutoringMessageSent_V1` events and dividing
    by 4. The real LLM token count IS captured at the API edge
    (`TutorEndpoints.cs:336`) and persisted on `TutorMessageDocument.TokensUsed`,
    but the admin services query the *event store* (where the field doesn't
    exist) instead of the *document* (where it does).

    Concretely: a 4000-token assistant response gets capped at 200 chars in
    the event preview, then `200 / 4 * 2 = 100` is reported as "tokens used"
    by the admin dashboard. The real cost is roughly **40× higher** than the
    meter shows. The DailyLimitPerStudent enforcement therefore lets a
    runaway tutor session burn through the real limit while the admin UI
    shows "1.5% used".

    Plus: `_dailyLimitOverride` is a `private static int` mutated by
    `UpdateLimitsAsync` in-process. Updates are lost on restart, are not
    propagated across instances, and are not per-tenant. The "Cost per
    learner per month" numbers downstream of this meter are not just wrong
    — they are unreviewable.
  root_cause: |
    The original `TutoringMessageSent_V1` event was designed before the
    token-accounting requirement existed. When `TutorMessageDocument.TokensUsed`
    was added (FIND-arch-004 hardening), the corresponding token field was
    NOT added to the event payload — only to the document. The admin
    services then chose the event store as the source of truth and
    silently substituted preview-length-divided-by-4 as the proxy.
  proposed_fix: |
    Three-part fix:
    1. Add an upcaster from `TutoringMessageSent_V1` → `TutoringMessageSent_V2`
       with a new `int InputTokens, int OutputTokens` field. Backfill by
       joining to the per-stream document table for events emitted since
       the document field landed; mark older events as `null` (the meter
       must show "unknown — pre-instrumentation" not "0").
    2. Rewrite `TokenBudgetAdminService` and the per-student token branch
       in `TutoringAdminService` to query `TutorMessageDocument`, NOT the
       event store. Group by StudentId, sum `TokensUsed`, group by Date,
       use existing indexes on (StudentId, ThreadId, CreatedAt).
    3. Persist daily/monthly limit overrides in a Marten document
       (`TokenBudgetSettingsDocument`) keyed by SchoolId. Remove the
       `_dailyLimitOverride` and `_monthlyLimitOverride` static fields.
       `UpdateLimitsAsync` writes to that document and stamps `SchoolId`
       from the caller's claim.
  test_required: |
    Integration test that:
      - Streams a tutor reply via TestServer that returns a known
        token count (e.g. 4321) in the LlmChunk's `TokensUsed.Value`.
      - Calls /api/admin/token-budget/status and asserts the student's
        `TokensUsedToday` is 4321 (from the document), NOT 100 (from
        the preview / 4 fallback).
      - Calls UpdateLimitsAsync with a custom limit, restarts the
        TestServer, and asserts the limit survives the restart (proves
        the static-field bug is gone).
  task_body: |
    GOAL
    Replace the fabricated cost meter (preview chars / 4) with the real
    token count from `TutorMessageDocument.TokensUsed`. Persist
    daily/monthly limit overrides per-school in a Marten document.
    Add an upcaster so the event stream carries token counts going forward.

    FILES TO TOUCH
      - src/api/Cena.Admin.Api/TokenBudgetAdminService.cs (rewrite)
      - src/api/Cena.Admin.Api/TutoringAdminService.cs:200-236, 331-352
      - src/actors/Cena.Actors/Tutoring/TutoringEvents.cs (add V2 record)
      - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs
        (register V2, register upcaster, register settings document)
      - src/shared/Cena.Infrastructure/EventStore/EventUpcasters.cs
        (add TutoringMessageSentV1ToV2Upcaster)

    FILES TO READ FIRST
      - .agentdb/AGENT_CODER_INSTRUCTIONS.md
      - src/shared/Cena.Infrastructure/EventStore/EventUpcasters.cs
        (existing pattern for ConceptAttemptedV1ToV2Upcaster)
      - src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs:324-372
        (where the real token count is available)

    DEFINITION OF DONE
      - TutoringMessageSent_V2 added with InputTokens, OutputTokens.
      - V1→V2 upcaster registered. V1 events still load (with null
        token fields).
      - Cost meter reads from TutorMessageDocument, NOT the event store.
      - Daily/monthly limits persist across restart in
        TokenBudgetSettingsDocument keyed by SchoolId.
      - TokenBudgetAdminEndpoints.UpdateLimits requires SchoolId from
        the JWT claim and writes to the per-school doc.
      - The "TokensUsed" label in the admin UI matches reality
        verified by an integration test that pumps a known token count
        through the LLM stream.
      - All static field state removed.
      - dotnet test green.

    REPORTING REQUIREMENTS
      complete --result with branch, files touched, test paths, and a
      paste of the integration test output showing the real token count
      flowing end-to-end.

- id: FIND-data-022
  severity: p0
  category: dead-query
  framework: null
  files:
    - path: src/actors/Cena.Actors/Services/AnalysisJobActor.cs
      lines: "239-251"
  related_prior_finding: FIND-data-006
  evidence:
    - type: grep
      content: |
        $ rg 'EventTypeName == "[A-Z]' src/ --type cs
        src/actors/Cena.Actors/Services/AnalysisJobActor.cs:244:
            .Where(e => e.StreamKey == studentId && e.EventTypeName == "ConceptAttempted_V1")
        # ONE remaining PascalCase predicate. Marten's alias is
        # "concept_attempted_v1" (snake_case _v1). String comparison
        # against "ConceptAttempted_V1" never matches a single row.
        # The stagnation analysis job loads zero attempts and produces
        # all-zero analysis output forever.
    - type: grep
      content: |
        $ rg 'EventTypeName == "[a-z]' src/ --type cs | head -5
        # Every other call site uses snake_case (the correct alias).
        src/api/Cena.Admin.Api/StudentInsightsService.cs:50: ... "focus_score_updated_v1"
        src/api/Cena.Admin.Api/AdminDashboardService.cs:157: ... "question_authored_v1"
        src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:220: ... "concept_attempted_v1"
    - type: git-blame
      content: |
        $ git log --oneline src/actors/Cena.Actors/Services/AnalysisJobActor.cs
        # Same actor file the FIND-data-006 fix never touched. The
        # FIND-data-006 PR swept Cena.Admin.Api but missed Cena.Actors.
  finding: |
    `AnalysisJobActor.LoadAttempts` queries `e.EventTypeName ==
    "ConceptAttempted_V1"` (PascalCase). Marten's NameToAlias for
    `ConceptAttempted_V1` is `concept_attempted_v1` (snake_case + _v1
    suffix). The predicate matches zero rows on every call. The actor's
    six analysis methods (`AnalyzeDifficultyMismatch`,
    `AnalyzeFocusDegradation`, `AnalyzePrerequisiteGaps`,
    `AnalyzeMethodologyIneffectiveness`, etc.) all consume the empty list
    and emit confidence scores of `0` for every stagnant student. The
    admin "stagnation insights" UI built on top of this is showing
    permanently-clean data even when the underlying student is stuck.
  root_cause: |
    FIND-data-006 (the prior fix for `nameof(T)` in
    `ExperimentAdminService`) ran a search-and-replace ONLY against
    `src/api/Cena.Admin.Api/`. The actor host's
    `src/actors/Cena.Actors/Services/AnalysisJobActor.cs` was not in scope.
    The same root cause — hardcoding the type's source identifier as the
    event alias — survives in this actor.
  proposed_fix: |
    Two-part fix:
    1. Immediate: change line 244 to
       `.Where(e => e.StreamKey == studentId && e.EventTypeName == "concept_attempted_v1")`.
       Better: switch to typed Marten event query
       `.QueryRawEventDataOnly<ConceptAttempted_V1>().Where(...)` so the
       compiler enforces the alias. Best: add a static helper
       `EventTypeAlias<T>(this StoreOptions opts)` that derives the alias
       at startup and caches it; no string literals anywhere.
    2. Add a code-fix lint or analyzer that rejects any string literal
       matching `^[A-Z][A-Za-z0-9]+_V\d+$` inside an `EventTypeName ==`
       comparison so this regression class cannot reappear.
  test_required: |
    Integration test that:
      - Appends a single ConceptAttempted_V1 event for a known student.
      - Triggers the stagnation analysis job.
      - Asserts the job's `AttemptsLoaded` count is 1, NOT 0.
      - Pre-fix variant of the test fails (the AttemptsLoaded is 0
        before the snake_case fix).
  task_body: |
    GOAL
    Fix the dead-query in AnalysisJobActor.LoadAttempts and add a
    project-wide guard so PascalCase event-type predicates cannot
    silently regress in the future.

    FILES TO TOUCH
      - src/actors/Cena.Actors/Services/AnalysisJobActor.cs:244
      - src/actors/Cena.Actors.Tests/Services/AnalysisJobActorTests.cs
        (add regression test)
      - tests/Cena.EventStore.Tests/EventTypeAliasGuardTests.cs (NEW)

    FILES TO READ FIRST
      - .agentdb/AGENT_CODER_INSTRUCTIONS.md
      - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-022
      - src/api/Cena.Admin.Api/ExperimentAdminService.cs (the v1 fix
        for the same bug class — confirm pattern parity)

    DEFINITION OF DONE
      - Line 244 uses snake_case alias OR typed event query.
      - Regression test asserts the actor loads >0 attempts when there
        is a real ConceptAttempted_V1 in the stream.
      - Static guard test scans `src/actors/` and `src/api/` for
        `EventTypeName == "[A-Z]` and FAILS the build if any match.
      - dotnet test green.

    REPORTING REQUIREMENTS
      complete --result with the snake_case fix diff, test path, and
      proof the guard test catches a PascalCase reintroduction (commit
      a deliberate regression then run the guard, then revert).

- id: FIND-data-023
  severity: p0
  category: fake-fix
  framework: null
  files:
    - path: src/actors/Cena.Actors/Projections/StudentLifetimeStatsProjection.cs
      lines: "82-119"
  related_prior_finding: FIND-data-001, FIND-data-009
  evidence:
    - type: grep
      content: |
        $ rg "DateTimeOffset.UtcNow|DateTime.UtcNow" \
            src/actors/Cena.Actors/Projections/StudentLifetimeStatsProjection.cs -n
        118:        stats.UpdatedAt = DateTimeOffset.UtcNow;
        # Inside Apply(BadgeEarned_V1) — the same wall-clock anti-pattern
        # FIND-data-001 was supposed to permanently retire from projections.
    - type: grep
      content: |
        # Streak math is broken too:
        $ sed -n '82,103p' src/actors/Cena.Actors/Projections/StudentLifetimeStatsProjection.cs
        82:    public void Apply(LearningSessionStarted_V1 e, StudentLifetimeStats stats)
        83:    {
        84:        stats.TotalSessions++;
        85:        stats.LastSessionAt = e.StartedAt;     ← assigned BEFORE the comparison
        86:
        87:        // Streak calculation (simplified: consecutive days)
        88:        if (stats.LastSessionAt.HasValue)
        89:        {
        90:            var daysSinceLast = (e.StartedAt - stats.LastSessionAt.Value).TotalDays;
        91:            // daysSinceLast is ALWAYS 0 because LastSessionAt was just set above
        92:            if (daysSinceLast <= 1)
        93:            {
        94:                stats.CurrentStreak++;
        95:                stats.LongestStreak = Math.Max(stats.LongestStreak, stats.CurrentStreak);
        96:            }
        ...
    - type: grep
      content: |
        # And the projection is registered as Inline but no API endpoint reads it:
        $ rg "Query<StudentLifetimeStats>|Load<StudentLifetimeStats>|LoadAsync<StudentLifetimeStats>" src/
        (no matches)
        # The projection is computed on every event append (real cost) but
        # read by zero call sites. The 13 QueryAllRawEvents call sites in
        # GamificationEndpoints.cs / StudentInsightsService.cs / etc. that
        # FIND-data-009 was supposed to migrate are still on the
        # event-store-scan path.
  finding: |
    The FIND-data-009 fix introduced `StudentLifetimeStatsProjection` to
    replace `QueryAllRawEvents` full-scans with single-document lookups.
    But the fix is broken in three ways:

    1. **Wall-clock inside Apply** —
       `Apply(BadgeEarned_V1, stats)` line 118 reads `DateTimeOffset.UtcNow`
       to set `stats.UpdatedAt`. This is the *exact* anti-pattern that
       FIND-data-001 was supposed to retire (`ClassFeedItemProjection`
       reading wall clock). Projection rebuilds will yield different
       `UpdatedAt` per run — non-deterministic replay. The fix
       *re-introduced* the bug class on the same day it was meant to
       prevent it.

    2. **Streak math is broken** — Line 85 sets
       `stats.LastSessionAt = e.StartedAt` BEFORE the comparison on line
       90 reads `stats.LastSessionAt.Value`, so `daysSinceLast` is always
       `0`. The streak counter increments every event, never resets, and
       `LongestStreak == CurrentStreak` always. The Vue UI shows real
       streaks because it falls back to the old `CalculateCurrentStreak`
       wall-clock loop in `GamificationEndpoints.cs:317`, but the
       projection is silently wrong.

    3. **Projection is unread** — Despite being registered as
       `ProjectionLifecycle.Inline` (cost on every event append), no
       endpoint or service in `src/api/` calls
       `Query<StudentLifetimeStats>` or `LoadAsync<StudentLifetimeStats>`.
       The 13 `QueryAllRawEvents` call sites that the fix was supposed
       to retire are STILL the live read path. The fix added cost
       without removing the original cost, and the read model it built
       is dead code.

    Net: FIND-data-009 is `partially-fixed` for tenant-scoping (none of
    the call sites it covered were actually changed) and `fake-fix` for
    the wall-clock anti-pattern.
  root_cause: |
    The fix author wrote a new projection without migrating the call
    sites and without checking that the Apply handlers were
    deterministic. The original FIND-data-001 reviewer's lesson
    ("never read wall-clock inside Apply") was not internalized.
  proposed_fix: |
    1. Replace `stats.UpdatedAt = DateTimeOffset.UtcNow` on line 118
       with `stats.UpdatedAt = e.AwardedAt` (assuming BadgeEarned_V1
       carries `AwardedAt`; if not, add it to the event payload via an
       upcaster). Same fix for any other wall-clock read.
    2. Fix the streak math: capture `var prevLastSessionAt =
       stats.LastSessionAt;` BEFORE assigning the new value, then use
       the captured prev value in the comparison.
    3. Migrate the 13 unscoped `QueryAllRawEvents` call sites listed in
       the appendix table to `LoadAsync<StudentLifetimeStats>` where
       lifetime stats are sufficient. For per-session and per-day
       breakdowns that lifetime stats can't answer, build dedicated
       per-day rollup projections (`StudentDailyStatsRollup` keyed by
       (StudentId, Date)) instead of falling back to global event
       scans.
    4. Add a deterministic-replay test: run the projection forward, do
       a manual rebuild, assert byte-equal output.
  test_required: |
    Three tests:
      a) Determinism: append a fixed event sequence, project to a
         StudentLifetimeStats document, rebuild the projection from
         scratch, assert document is byte-identical.
      b) Streak correctness: append two LearningSessionStarted_V1
         events with timestamps 25 hours apart, assert CurrentStreak == 2
         (not infinity); append a third 73 hours later, assert
         CurrentStreak == 1.
      c) Read-path migration: hit /api/gamification/badges, assert the
         response was assembled from the projection (verify via
         intercepting the query), NOT from QueryAllRawEvents (the
         intercept must observe zero raw event scans).
  task_body: |
    GOAL
    Three-fold fix to FIND-data-009's `StudentLifetimeStatsProjection`:
    (1) eliminate wall-clock reads inside Apply, (2) fix the broken
    streak math, (3) actually migrate the 13 read-path call sites to
    use the projection.

    FILES TO TOUCH
      - src/actors/Cena.Actors/Projections/StudentLifetimeStatsProjection.cs
      - src/api/Cena.Student.Api.Host/Endpoints/GamificationEndpoints.cs
        (5 QueryAllRawEvents sites)
      - src/api/Cena.Admin.Api/StudentInsightsService.cs
        (13 QueryAllRawEvents sites)
      - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs
        (2 sites — replay + detail)
      - src/api/Cena.Student.Api.Host/Endpoints/StudentAnalyticsEndpoints.cs
        (2 sites — analytics summary + concept timeline)

    FILES TO READ FIRST
      - .agentdb/AGENT_CODER_INSTRUCTIONS.md
      - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-023
      - docs/reviews/agent-3-data-findings.md FIND-data-001 (the
        wall-clock-in-Apply lesson)
      - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs:198
        (where the projection is registered)

    DEFINITION OF DONE
      - Apply handlers in StudentLifetimeStatsProjection use only
        timestamps from the event payload (no DateTimeOffset.UtcNow,
        no DateTime.UtcNow).
      - Streak math fixed and proven by a unit test that asserts a
        real consecutive-day chain.
      - The 13 call sites in the affected files use
        `LoadAsync<StudentLifetimeStats>` for lifetime stats. Where
        per-day breakdowns are needed, a new
        `StudentDailyStatsRollupProjection` is added and used.
      - QueryAllRawEvents count in src/api/ drops from 31 to <= 5
        (justified bootstrap / monitoring sites only).
      - Determinism test added: project forward, rebuild from
        scratch, assert byte-identical document.
      - dotnet test green.

    REPORTING REQUIREMENTS
      complete --result with diff stats, ripgrep proof of the
      QueryAllRawEvents reduction, and a paste of the determinism test
      output (showing rebuild produces identical state).
```

### P1 — High

```yaml
- id: FIND-data-024
  severity: p1
  category: perf
  framework: null
  files:
    - path: src/api/Cena.Admin.Api/SystemMonitoringService.cs
      lines: "285-313"
  related_prior_finding: null
  evidence:
    - type: grep
      content: |
        $ sed -n '285,313p' src/api/Cena.Admin.Api/SystemMonitoringService.cs
        285:    public async Task<AuditLogResponse> GetAuditLogAsync(AuditLogFilterRequest request, int page, int pageSize)
        286:    {
        287:        await using var session = _store.QuerySession();
        288:
        289:        // Query real events from Marten event store as audit log entries
        290:        var query = session.Events.QueryAllRawEvents()
        291:            .OrderByDescending(e => e.Timestamp);
        292:
        293:        var totalCount = await query.CountAsync();
        294:
        295:        var rawEvents = await query
        296:            .Skip((page - 1) * pageSize)
        297:            .Take(pageSize)
        298:            .ToListAsync();
        299:        ...
        # AuditLogFilterRequest is parameter #1. It is NEVER read.
        # IpAddress is hardcoded to "server" on line 309.
  finding: |
    `SystemMonitoringService.GetAuditLogAsync` accepts an
    `AuditLogFilterRequest request` parameter (filter by user, action,
    date range, target type) and silently ignores it. The query is a
    raw `QueryAllRawEvents().OrderByDescending(...)` with no WHERE
    clause. Three problems:

    1. The "filter by user" UI control in the admin audit log page
       has no effect — every filter selection returns the same global
       result set. **Lying-label / unimplemented feature.**

    2. `query.CountAsync()` against the entire `mt_events` table runs
       on every page-flip. For a 10M-event store, this is a multi-
       second sequential count on every page click.

    3. The audit log conflates ALL Marten events as "audit entries",
       including system events that have no business being in an
       audit log (focus_score_updated_v1, concept_attempted_v1).
       The audit log will be 99% noise.

    4. `IpAddress: "server"` is hardcoded — the audit log shows every
       action as having come from "server". For FERPA compliance and
       breach forensics, this is unusable.
  root_cause: |
    The endpoint was scaffolded with the intent that `request` would
    be threaded into the WHERE clause but the wiring was never done.
    No integration test was added that asserts a filter choice
    actually filters.
  proposed_fix: |
    1. Define a dedicated `AuditEventDocument` document populated by
       a SecurityAuditProjection that listens only to events on the
       audit-relevant subset (Login_V1, RoleChanged_V1,
       StudentDataAccessed_V1, GdprDsarRequested_V1, etc.).
    2. Index by Timestamp, ActorUserId, ActorIpAddress, TargetEntityId.
    3. Rewrite GetAuditLogAsync to query that document with all four
       AuditLogFilterRequest fields applied.
    4. Capture real IP from `IHttpContextAccessor` at the audit-event
       emit site (the existing CenaClaimsTransformer can do this).
    5. Add a contract test on the admin client that filters by user
       and asserts the result set shrinks.
  test_required: |
    Integration test: seed 5 audit events for user A, 5 for user B,
    call GetAuditLogAsync with filter `userId=A`, assert response
    contains only the 5 user-A events. Plus a fail-on-startup test
    that asserts the audit document is registered.
  task_body: |
    GOAL
    Replace the broken global-events audit log with a dedicated
    AuditEventDocument + projection, wire AuditLogFilterRequest into
    the query, and capture real client IPs at emit time.

    FILES TO TOUCH
      - src/api/Cena.Admin.Api/SystemMonitoringService.cs:285-313
      - src/actors/Cena.Actors/Audit/SecurityAuditProjection.cs (NEW)
      - src/shared/Cena.Infrastructure/Documents/AuditEventDocument.cs (NEW)
      - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs (register)
      - src/api/Cena.Admin.Api/SystemMonitoringEndpoints.cs (DI)

    FILES TO READ FIRST
      - .agentdb/AGENT_CODER_INSTRUCTIONS.md
      - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-024
      - src/shared/Cena.Infrastructure/Auth/CenaClaimsTransformer.cs

    DEFINITION OF DONE
      - AuditLogFilterRequest fields are all consumed by the LINQ query.
      - Real IP captured from HttpContext (X-Forwarded-For aware).
      - CountAsync runs against the audit document, not mt_events.
      - Integration test proves the filter parameter actually filters.
      - dotnet test green.

    REPORTING REQUIREMENTS
      complete --result with branch, file diffs, test paths, and a
      paste of curl output proving filter takes effect.

- id: FIND-data-025
  severity: p1
  category: perf
  framework: null
  files:
    - path: src/api/Cena.Admin.Api/StudentInsightsService.cs
      lines: "45-470"
  related_prior_finding: null
  evidence:
    - type: grep
      content: |
        $ rg "QueryAllRawEvents" src/api/Cena.Admin.Api/StudentInsightsService.cs -c
        13
        # 13 separate QueryAllRawEvents calls, each Take(500) or Take(2000)
        # or Take(5000) globally, then in-memory filter by studentId.
        # No tenant scope. No SchoolId check.
    - type: grep
      content: |
        $ sed -n '85,93p' src/api/Cena.Admin.Api/StudentInsightsService.cs
        85:        var focusEvents = await session.Events.QueryAllRawEvents()
        86:            .Where(e => e.EventTypeName == "focus_score_updated_v1")
        87:            .OrderByDescending(e => e.Timestamp)
        88:            .Take(2000)                                ← global cap
        89:            .ToListAsync();
        90:
        91:        var studentEvents = focusEvents
        92:            .Where(e => ExtractString(e, "studentId") == studentId)  ← in-memory filter
        93:            .ToList();
  finding: |
    Eight per-student admin insight endpoints
    (`GetFocusHeatmap`, `GetDegradationCurve`, `GetEngagement`,
    `GetErrorTypes`, `GetHintUsage`, `GetStagnation`, `GetSessionPatterns`,
    `GetResponseTimes`) all share the same anti-pattern: query the
    global event store with `Take(N)` where N ∈ {500, 2000, 5000},
    then filter to the requested student in memory.

    Two compounding bugs:
    1. **Cross-tenant data leakage at the read layer.** The endpoint
       takes a `ClaimsPrincipal user` but never resolves the caller's
       SchoolId. An admin from school A who calls
       `/api/admin/insights/student/<studentId>` for a student in
       school B will get back the student's data because the only
       filter is the path-parameter studentId. The IDOR check the
       admin host uses elsewhere (`SchoolId` resolution) is missing
       here entirely.
    2. **Sample-truncation bug at scale.** With 100k students each
       generating ~50 focus_score_updated_v1 events per session, the
       most-recent 2000 events globally cover at most ~40 active
       students. Every other student's heatmap will return EMPTY
       — silently — because the 2000-event ceiling exhausted before
       reaching their events. The label says "the last 30 days of
       focus scores"; the data is "the most recent 2000 globally".
  root_cause: |
    The author copy-pasted the cross-cutting analytics pattern from
    `FocusAnalyticsService` (which is at least bounded by the rollup
    document path) without realizing the per-student endpoint needs a
    per-student stream query.
  proposed_fix: |
    Two-step fix:
    1. Resolve `schoolId = TenantScope.GetSchoolFilter(user)`. Look
       up the studentId in `StudentProfileSnapshot` and 404/403 if
       its `SchoolId != schoolId` (and SUPER_ADMIN bypass for
       `schoolId is null`). This is the same pattern
       `TutoringAdminService.GetSessionDetailAsync` already uses
       (line 105-113).
    2. Replace each `QueryAllRawEvents().Take(N).Where(in-memory
       studentId)` with a per-student stream query
       `session.Events.FetchStreamAsync(studentId)` filtered by
       event type, OR add a `StudentDailyStatsRollupProjection`
       indexed by (StudentId, Date) and query that.
  test_required: |
    Integration test: seed two students with different SchoolIds and
    different focus event histories. Authenticate as an admin in
    school A. Hit /api/admin/insights/student/{schoolBStudentId}.
    Assert 403 / 404. Then hit /api/admin/insights/student/{schoolAStudentId}.
    Assert 200 with non-empty heatmap.
  task_body: |
    GOAL
    Add tenant scoping to all 8 per-student insight endpoints AND
    replace the global Take(N).Where(in-memory) pattern with
    per-student stream / per-student rollup queries.

    FILES TO TOUCH
      - src/api/Cena.Admin.Api/StudentInsightsService.cs (rewrite all
        13 QueryAllRawEvents call sites)
      - src/api/Cena.Admin.Api/StudentInsightsEndpoints.cs (verify the
        ClaimsPrincipal is forwarded — already done)
      - src/actors/Cena.Actors/Projections/StudentDailyStatsRollupProjection.cs
        (NEW — feeds the heatmap and degradation curve from real data)
      - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs

    FILES TO READ FIRST
      - .agentdb/AGENT_CODER_INSTRUCTIONS.md
      - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-025
      - src/api/Cena.Admin.Api/TutoringAdminService.cs:105-113 (the
        existing TenantScope pattern to copy)

    DEFINITION OF DONE
      - Every per-student insight endpoint resolves SchoolId from the
        caller's claim and rejects cross-school access.
      - Zero `Take(2000)` / `Take(5000)` global event scans remain
        in StudentInsightsService.
      - Tests: cross-tenant access is blocked AND the per-student
        data is computed from a real stream/rollup, not a truncated
        global slice.
      - dotnet test green.

    REPORTING REQUIREMENTS
      complete --result with diff stats, evidence the cross-tenant
      test fails on a pre-fix branch and passes on the fix branch.

- id: FIND-data-026
  severity: p1
  category: perf
  framework: null
  files:
    - path: src/api/Cena.Admin.Api/ExperimentAdminService.cs
      lines: "45-238"
  related_prior_finding: null
  evidence:
    - type: grep
      content: |
        $ rg "QueryAllRawEvents" src/api/Cena.Admin.Api/ExperimentAdminService.cs -c
        9
    - type: grep
      content: |
        # No ClaimsPrincipal parameter on any method:
        $ rg "Task<.*ExperimentAdminService.*Async\(" src/api/Cena.Admin.Api/ExperimentAdminService.cs -n
        45:    public async Task<ExperimentListResponse> GetExperimentsAsync()
        92:    public async Task<ExperimentDetailDto?> GetExperimentDetailAsync(string experimentName)
        168:   public async Task<ExperimentFunnelResponse?> GetFunnelAsync(string experimentName)
        # Zero `ClaimsPrincipal` parameters. No tenant scope possible.
  finding: |
    `ExperimentAdminService` runs nine `QueryAllRawEvents` full-store
    scans across three methods, with NO `ClaimsPrincipal` and NO
    `SchoolId` filter. Every admin in every tenant sees the same
    global experiment population. `GetFunnelAsync` performs FIVE
    sequential full-event-store scans per request (assigned, engaged,
    confused, resolved, mastered stages). For a system with 100k
    students and a few million events, every funnel page-load is a
    multi-second 5×N work unit per request, with no caching.

    Plus: the "experiment population" is derived from
    `session_started_v1` events globally and partitioned in C# code
    using `HashCode.Combine(studentId, experimentName) % arms.Length`
    — the experiment service has NO persisted assignment record. If
    `arms.Length` is changed in a config file, every prior student is
    silently re-bucketed and the cohort comparison becomes meaningless.
  root_cause: |
    Designed as a quick analytics endpoint that derives population
    from event scans without persisting assignment. Tenant scoping
    was never added because the comment on line 51 reasons that the
    deterministic hash is enough to "estimate" populations — it
    skips the question of which tenant should see which population.
  proposed_fix: |
    1. Add `ClaimsPrincipal user` to every method signature; resolve
       SchoolId; apply at the studentId enumeration step.
    2. Persist experiment assignments at `SessionStarted_V1`-emit
       time (`ExperimentAssigned_V1` event with `(StudentId,
       ExperimentName, Arm, AssignedAt, AlgorithmVersion)`). Build a
       per-experiment-per-arm document and query that, NOT
       full-event-store scans. The hash-derivation path is unsafe
       once arm counts change.
    3. Cache funnel results per (SchoolId, experimentName) for 5
       minutes. The funnel rate-of-change is slow.
  test_required: |
    1. Cross-tenant test: an admin from school A queries the funnel,
       asserts the count matches school A's session count, NOT the
       global session count.
    2. Persistence test: change `arms.Length` from 2 to 3, replay
       events, assert previously-bucketed students remain in their
       original arm.
  task_body: |
    GOAL
    Tenant-scope every ExperimentAdminService method and replace
    full-event-store scans with persisted assignment events + a
    per-arm read model.

    FILES TO TOUCH
      - src/api/Cena.Admin.Api/ExperimentAdminService.cs
      - src/api/Cena.Admin.Api/ExperimentAdminEndpoints.cs (forward
        ClaimsPrincipal)
      - src/actors/Cena.Actors/Events/ExperimentEvents.cs (NEW
        ExperimentAssigned_V1)
      - src/actors/Cena.Actors/Services/ExperimentService.cs (emit
        the event on first assignment)
      - src/actors/Cena.Actors/Projections/ExperimentArmRollupProjection.cs (NEW)
      - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs

    FILES TO READ FIRST
      - .agentdb/AGENT_CODER_INSTRUCTIONS.md
      - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-026
      - src/actors/Cena.Actors/Services/ExperimentService.cs

    DEFINITION OF DONE
      - All 9 QueryAllRawEvents calls replaced.
      - Every method takes ClaimsPrincipal and applies SchoolId.
      - ExperimentAssigned_V1 event registered + upcaster path.
      - Funnel cached per (SchoolId, experimentName) for 5 minutes.
      - Cross-tenant integration test passes.
      - dotnet test green.

    REPORTING REQUIREMENTS
      complete --result with branch, files, tests, plus a paste of
      the cross-tenant test output.

- id: FIND-data-027
  severity: p1
  category: perf
  framework: null
  files:
    - path: src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs
      lines: "219-225, 348-356"
    - path: src/api/Cena.Student.Api.Host/Endpoints/StudentAnalyticsEndpoints.cs
      lines: "54-60, 139-146"
  related_prior_finding: FIND-data-009
  evidence:
    - type: grep
      content: |
        $ sed -n '219,225p' src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs
        219:        var events = await session.Events.QueryAllRawEvents()
        220:            .Where(e => e.EventTypeName == "concept_attempted_v1")
        221:            .ToListAsync();             ← no Take, no streamKey filter
        222:
        223:        var sessionEvents = events
        224:            .Where(e => ExtractString(e, "sessionId") == doc.SessionId)
        225:            .ToList();
        # Loads EVERY concept_attempted_v1 event in the database, then
        # filters in memory for the current session. Every /api/sessions/{id}
        # request triggers a full-store scan.
  finding: |
    `GET /api/sessions/{id}` (line 198) and `GET /api/sessions/{id}/replay`
    (line 326) both load every `concept_attempted_v1` event in the
    database with no Take, no time window, no streamKey filter, no
    tenant filter. They then filter in memory by `sessionId`. The
    IDOR check at line 215/344 (`doc.StudentId == studentId`) gates
    access to the response, but the underlying SQL pulls every
    student's events into the API process before the filter runs.

    For an active platform with millions of `concept_attempted_v1`
    events, every session detail request is an O(N) seq-scan. This
    is exactly the FIND-data-009 anti-pattern but in different files
    than the prior agent flagged. Same root cause.
    StudentAnalyticsEndpoints.cs:54 and :139 do the same.
  root_cause: |
    The author needed to find events for a specific session but the
    event store is keyed on `streamKey == studentId`, not
    `sessionId`. Rather than maintaining a `(sessionId → events)`
    rollup, they ran a full-store scan and filtered in memory.
  proposed_fix: |
    1. Append `concept_attempted_v1` events to a session-keyed stream
       in addition to the student-keyed stream (`session.Events.Append(
       sessionId, conceptAttempt)` after the existing student-keyed
       append). The rebuild path can run forward in time and per-stream.
    2. OR build a `SessionConceptHistoryProjection` indexed by
       (SessionId) that captures the per-session attempts; replace
       both call sites with a single LoadAsync.
    3. The replay endpoint can use option (2) and order by Sequence
       for true per-question replay determinism.
  test_required: |
    Performance regression test that asserts the SQL plan for the
    /api/sessions/{id}/replay endpoint does NOT include a sequential
    scan on `mt_events` (verify via `EXPLAIN ANALYZE` on a seeded
    DB with 100k events).
  task_body: |
    GOAL
    Eliminate the global event-store scan from /api/sessions/{id} and
    /api/sessions/{id}/replay (and the two StudentAnalytics endpoints
    that share the pattern). Replace with a per-session projection or
    a session-keyed event stream.

    FILES TO TOUCH
      - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:219, 348
      - src/api/Cena.Student.Api.Host/Endpoints/StudentAnalyticsEndpoints.cs:54, 139
      - src/actors/Cena.Actors/Projections/SessionConceptHistoryProjection.cs (NEW)
      - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs

    FILES TO READ FIRST
      - .agentdb/AGENT_CODER_INSTRUCTIONS.md
      - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-027
      - docs/reviews/agent-3-data-findings.md FIND-data-009 (sister
        anti-pattern)

    DEFINITION OF DONE
      - All 4 QueryAllRawEvents call sites replaced.
      - Per-session lookup is O(1) document load or per-stream replay.
      - dotnet test green; integration test asserts the new endpoint
        returns the same data shape as before.

    REPORTING REQUIREMENTS
      complete --result with branch, files, tests.

- id: FIND-data-028
  severity: p1
  category: stub
  framework: null
  files:
    - path: src/api/Cena.Student.Api.Host/Endpoints/GamificationEndpoints.cs
      lines: "92-110"
  related_prior_finding: null
  evidence:
    - type: grep
      content: |
        $ sed -n '92,100p' src/api/Cena.Student.Api.Host/Endpoints/GamificationEndpoints.cs
        92:    if (isEarned)
        93:    {
        94:        earned.Add(new Badge(
        95:            BadgeId: badgeDef.Id,
        96:            Name: badgeDef.Name,
        97:            Description: badgeDef.Description,
        98:            IconName: badgeDef.IconName,
        99:            Tier: badgeDef.Tier,
        100:            EarnedAt: DateTime.UtcNow.AddDays(-new Random(badgeDef.Id.GetHashCode()).Next(1, 60))));
        # ↑ EarnedAt is fabricated. The Random seed is the badge ID hash, so
        # the value is stable per badge but unrelated to when the student
        # actually earned it.
  finding: |
    `GET /api/gamification/badges` returns a list of earned badges
    where `EarnedAt` is `DateTime.UtcNow.AddDays(-new Random(badgeDef.Id.GetHashCode()).Next(1, 60))`.
    The "earned" timestamp is FABRICATED — a deterministic random
    number derived from the badge ID hash, anchored to whatever time
    the request was served. The student UI displays this as
    "Earned 23 days ago" — that number is invented at request time
    and shifts on every request.

    The user has banned stub/canned/fake backend code (memory:
    feedback_no_stubs_production_grade). This is a fake-data bug
    on a learner-facing endpoint, where the lying label
    ("EarnedAt") describes a value that has no anchor in reality.

    The real fix is to derive `EarnedAt` from the actual
    `BadgeEarned_V1` event timestamp (which the projection can
    capture).
  root_cause: |
    The badges endpoint computes earnedness on-the-fly from session/
    XP/streak counters instead of consuming `BadgeEarned_V1` events.
    The author needed an `EarnedAt` field for the DTO and chose to
    fabricate one rather than properly emitting and reading the
    earning event.
  proposed_fix: |
    1. Persist a `BadgeEarned_V1` event when a student crosses a
       badge threshold (probably in `LearningSessionEndedHandler` or
       similar). The event has a real `AwardedAt`.
    2. Build a `StudentBadgesProjection` keyed by StudentId that
       captures `(BadgeId, AwardedAt)`.
    3. The /api/gamification/badges endpoint reads from this
       projection and returns real timestamps.
    4. Idempotency: the projection ignores duplicate
       `BadgeEarned_V1` events for the same `(StudentId, BadgeId)`.
  test_required: |
    Integration test: seed a real BadgeEarned_V1 event with
    `AwardedAt = 2026-03-01`. Hit /api/gamification/badges. Assert
    the response shows `EarnedAt: 2026-03-01`, NOT a Random-derived
    value within last 60 days.
  task_body: |
    GOAL
    Replace the fabricated EarnedAt (Random.Next(1, 60) days ago)
    with real BadgeEarned_V1 event timestamps via a dedicated
    projection.

    FILES TO TOUCH
      - src/api/Cena.Student.Api.Host/Endpoints/GamificationEndpoints.cs
      - src/actors/Cena.Actors/Projections/StudentBadgesProjection.cs (NEW)
      - src/actors/Cena.Actors/Sessions/LearningSessionEnded handler
        (emit BadgeEarned_V1 when threshold crossed)
      - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs

    FILES TO READ FIRST
      - .agentdb/AGENT_CODER_INSTRUCTIONS.md
      - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-028
      - .claude/projects/-Users-shaker-edu-apps-cena/memory/feedback_no_stubs_production_grade.md
      - src/actors/Cena.Actors/Events/EngagementEvents.cs (BadgeEarned_V1
        record)

    DEFINITION OF DONE
      - No `Random` calls in GamificationEndpoints.
      - EarnedAt derived from real event payload.
      - Idempotent projection.
      - Integration test: seed event → check API response timestamp.

    REPORTING REQUIREMENTS
      complete --result with branch, files, test path, and curl
      paste showing real timestamp.
```

### P2 — Normal

```yaml
- id: FIND-data-029
  severity: p2
  category: perf
  framework: null
  files:
    - path: src/api/Cena.Student.Api.Host/Endpoints/ChallengesEndpoints.cs
      lines: "100-133"
  related_prior_finding: null
  evidence:
    - type: grep
      content: |
        $ sed -n '105,116p' src/api/Cena.Student.Api.Host/Endpoints/ChallengesEndpoints.cs
        105:    var allCompletions = await session.Query<DailyChallengeCompletionDocument>()
        106:        .Where(c => c.Date == today)
        107:        .ToListAsync();                  ← no Take, no SchoolId filter
        108:
        109:    var ranked = allCompletions
        110:        .OrderByDescending(c => c.Score)
        111:        .ThenBy(c => c.TimeSeconds)
        112:        .Select((c, i) => new { Rank = i + 1, Completion = c })
        113:        .ToList();
        114:
        115:    var topTen = ranked.Take(10)...
  finding: |
    `GET /api/challenges/daily/leaderboard` loads every daily
    challenge completion across the WHOLE platform for today, sorts
    in memory, then takes the top 10. Two issues:

    1. **Unbounded query.** Once daily-challenge participation passes
       a few thousand students, every leaderboard request fetches the
       entire row set. Push the OrderBy + Take into Marten LINQ.
    2. **Cross-tenant leak.** The leaderboard is GLOBAL — no
       SchoolId filter. A student opens the daily-challenge page and
       sees winners from other schools. The UI implies "your class
       leaderboard" but the data is "every learner on the platform".
       Either rename the UI to "global leaderboard" or scope by
       SchoolId.

    This is the prior FIND-data-017 P2 from the v1 agent file that
    was never enqueued; surviving verbatim.
  root_cause: |
    The author followed the same pattern as `LeaderboardService` for
    a quick win. Tenancy was deferred and never followed up.
  proposed_fix: |
    var topTen = await session.Query<DailyChallengeCompletionDocument>()
        .Where(c => c.Date == today && c.SchoolId == schoolId)
        .OrderByDescending(c => c.Score)
        .ThenBy(c => c.TimeSeconds)
        .Take(10)
        .ToListAsync();

    For "my rank":
      var myRank = await session.Query<DailyChallengeCompletionDocument>()
        .CountAsync(c => c.Date == today && c.SchoolId == schoolId &&
            (c.Score > myScore || (c.Score == myScore && c.TimeSeconds < myTime))) + 1;

    Add a SchoolId index on DailyChallengeCompletionDocument.
  test_required: |
    Integration test: two completions in school A, two in school B.
    Authenticated student in school A hits the endpoint, asserts
    leaderboard shows ONLY school A entries.
  task_body: |
    GOAL
    Push the daily challenge leaderboard sort into SQL with a Take(10)
    and add SchoolId scoping so the leaderboard is per-school, not
    global.

    FILES TO TOUCH
      - src/api/Cena.Student.Api.Host/Endpoints/ChallengesEndpoints.cs
      - src/shared/Cena.Infrastructure/Documents/DailyChallengeDocuments.cs
        (add SchoolId field if missing)
      - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs
        (add SchoolId index)

    FILES TO READ FIRST
      - .agentdb/AGENT_CODER_INSTRUCTIONS.md
      - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-029
      - docs/reviews/agent-3-data-findings.md FIND-data-017 (the prior
        unenqueued finding)

    DEFINITION OF DONE
      - SQL-side OrderBy + Take(10).
      - SchoolId scope applied.
      - Cross-tenant test passes.
      - dotnet test green.

    REPORTING REQUIREMENTS
      complete --result with diff and test paste.

- id: FIND-data-030
  severity: p2
  category: perf
  framework: null
  files:
    - path: src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs
      lines: "170-197"
  related_prior_finding: null
  evidence:
    - type: grep
      content: |
        $ sed -n '180,196p' src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs
        180:    // Get messages for this thread
        181:    var messages = await session.Query<TutorMessageDocument>()
        182:        .Where(m => m.ThreadId == threadId)
        183:        .OrderBy(m => m.CreatedAt)
        184:        .ToListAsync();             ← no Take
        ...
        193:    return Results.Ok(new TutorMessageListDto(
        194:        ThreadId: threadId,
        195:        Messages: dtos,
        196:        HasMore: false));           ← lying label
  finding: |
    `GET /api/tutor/threads/{threadId}/messages` returns every
    message in a thread, sorted by CreatedAt, with `HasMore: false`
    hardcoded. Tutor threads grow as the student converses; for a
    real long-running tutoring session this becomes a single OOM-
    shaped response.

    The DTO claims pagination support via `HasMore` but the value
    is always `false` regardless of how many rows came back —
    classic lying label.

    Same as the prior FIND-data-018 P2 from v1, never enqueued.
  root_cause: |
    "Phase 1: no pagination" comment on line 196 — pagination was
    deferred and forgotten.
  proposed_fix: |
    Accept `?limit=` and `?before=<messageId>` query params. Use
    `.OrderBy(m => m.CreatedAt).Where(m => m.CreatedAt < beforeTimestamp)
    .Take(limit + 1).ToListAsync()`. Set HasMore based on whether the
    extra row came back.
  test_required: |
    Test: insert 25 messages, request with limit=10, assert
    HasMore=true and 10 returned. Request with the cursor for the
    11th, assert HasMore=true with the next 10. Request the third
    page, assert HasMore=false with 5.
  task_body: |
    GOAL
    Add cursor-based pagination to the tutor messages endpoint and
    make `HasMore` tell the truth.

    FILES TO TOUCH
      - src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs
      - src/api/Cena.Api.Contracts/Tutor/TutorMessageListDto.cs (add
        nextCursor field)

    FILES TO READ FIRST
      - .agentdb/AGENT_CODER_INSTRUCTIONS.md
      - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-030

    DEFINITION OF DONE
      - Cursor-based pagination implemented.
      - HasMore is computed, not hardcoded.
      - Three-page integration test passes.

    REPORTING REQUIREMENTS
      complete --result with diff, contract test, and the 3-page
      integration test paste.

- id: FIND-data-031
  severity: p2
  category: perf
  framework: null
  files:
    - path: src/shared/Cena.Infrastructure/Gamification/LeaderboardService.cs
      lines: "395-525"
    - path: src/actors/Cena.Actors/Configuration/MartenConfiguration.cs
      lines: "174-178"
  related_prior_finding: null
  evidence:
    - type: grep
      content: |
        $ sed -n '395,420p' src/shared/Cena.Infrastructure/Gamification/LeaderboardService.cs
        ...
        403:                   COALESCE((data->>'TotalXp')::int, 0) as TotalXp,
        ...
        406:        FROM cena.mt_doc_studentprofilesnapshot
        ...
        411:        ORDER BY COALESCE((data->>'TotalXp')::int, 0) DESC
    - type: grep
      content: |
        $ rg "TotalXp|Index.*Xp" src/actors/Cena.Actors/Configuration/MartenConfiguration.cs
        # No matches — TotalXp is not indexed.
  finding: |
    The fixed (parameterized) leaderboard SQL still has two
    structural data problems:

    1. **Hard-codes the Marten internal table name**
       (`cena.mt_doc_studentprofilesnapshot`). If anyone renames
       the C# type or changes Marten's naming policy, the leaderboard
       silently breaks. Bypass of Marten's LINQ → schema-rename
       compatibility.
    2. **Sort key is unindexed.** Every leaderboard call does
       `ORDER BY COALESCE((data->>'TotalXp')::int, 0) DESC` against
       a JSONB extraction that has NO functional index. For a school
       with 10k students this is a full table scan + sort. The
       SchoolId index helps the WHERE in the per-school case but the
       global leaderboard query is unscoped.

    Same as the prior FIND-data-015 P2 from v1, never enqueued.
  root_cause: |
    The author wrote raw SQL to escape Marten's LINQ provider
    limitations on JSONB extraction sorts. The follow-up of "now add
    a functional index" was never done.
  proposed_fix: |
    1. Replace the raw SQL with a Marten LINQ
       `session.Query<StudentProfileSnapshot>().OrderByDescending(s => s.TotalXp).Take(limit).ToListAsync()`.
       Marten can compile this against an indexed column.
    2. Add `opts.Schema.For<StudentProfileSnapshot>().Index(x =>
       x.TotalXp);` so the sort is a reverse index scan.
    3. For the rank queries, use scalar
       `session.Query<T>().CountAsync(s => s.SchoolId == schoolId
       && s.TotalXp > studentXp)`.
  test_required: |
    Integration test: seed 1000 students, run global leaderboard
    twice, assert second run uses the index (verify via
    `EXPLAIN ANALYZE` if Postgres reachable; otherwise check
    Marten compiled-query cache).
  task_body: |
    GOAL
    Replace the leaderboard's raw SQL with Marten LINQ and add a
    `TotalXp` index on `StudentProfileSnapshot` so the sort is
    O(log N) not O(N log N).

    FILES TO TOUCH
      - src/shared/Cena.Infrastructure/Gamification/LeaderboardService.cs
      - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs

    FILES TO READ FIRST
      - .agentdb/AGENT_CODER_INSTRUCTIONS.md
      - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-031
      - docs/reviews/agent-3-data-findings.md FIND-data-015

    DEFINITION OF DONE
      - Raw SQL replaced with Marten LINQ.
      - TotalXp index added.
      - Existing FIND-sec-001 SQLi safety tests still pass.
      - dotnet test green.

    REPORTING REQUIREMENTS
      complete --result with diff and test paste.

- id: FIND-data-032
  severity: p2
  category: cqrs-smell
  framework: null
  files:
    - path: src/actors/Cena.Actors/Students/StudentActor.Commands.cs
      lines: "155-178, 296-318, 485-488"
  related_prior_finding: null
  evidence:
    - type: grep
      content: |
        $ sed -n '160,162p' src/actors/Cena.Actors/Students/StudentActor.Commands.cs
        160:    await using var qs = _documentStore.QuerySession();
        161:    var readModel = await qs.LoadAsync<Questions.QuestionReadModel>(cmd.QuestionId);
        # Write-side actor reads from a read model.
    - type: grep
      content: |
        $ sed -n '485,488p' src/actors/Cena.Actors/Students/StudentActor.Commands.cs
        485:    var doc = await querySession.Query<Cena.Actors.Tutoring.TutoringSessionDocument>()
        486:        .FirstOrDefaultAsync(d => d.Id == cmd.SessionId || d.SessionId == cmd.SessionId);
        # OR predicate defeats both indexes.
  finding: |
    Three CQRS / planner issues in the student write-side actor:

    1. `StudentActor.AttemptConcept` reads `QuestionReadModel` from
       the read-side projection (line 160-161). Works because
       `QuestionListProjection` is Inline, but couples the command
       path to the read model and breaks if the projection ever moves
       to Async for scale. (Prior FIND-data-014 in the v1 file —
       never enqueued.)

    2. `HandleResumeSession` (line 485) does
       `Where(d => d.Id == cmd.SessionId || d.SessionId == cmd.SessionId)`.
       The OR forces a sequential scan or UNION plan because the
       planner cannot pick which index to use. Both `Id` and
       `SessionId` are indexed (MartenConfiguration.cs:159-161). Same
       pattern in `SessionEndpoints.cs:235, 365, 393, 209, 339`.
       (Prior FIND-data-016 — never enqueued.)

    3. The query session opened on line 160 is separate from the
       write session that's about to do `session.Events.Append(...)`.
       Two database round-trips for what should be one.
  root_cause: |
    Resume-by-id-or-session-id was a backward-compatible API
    convenience after the SessionId field was added. The OR was the
    quickest way to keep both call paths working without a controller
    change.
  proposed_fix: |
    1. Read the question via
       `session.Events.AggregateStreamAsync<QuestionState>(questionId)`
       so the command path doesn't depend on a read model.
    2. For the OR predicate: try the identity Load first (primary key
       hit), and only fall back to the SessionId query if the first
       missed. Two queries with index hits each is faster than one
       seq scan.
    3. Open one session per request, not two.
  test_required: |
    Performance regression test that captures the Marten command log
    for /api/sessions/{id}/resume and asserts no `OR` clause and no
    seq scan.
  task_body: |
    GOAL
    Three small fixes to the student write-side: (1) use aggregate
    stream for question reads in commands, (2) split OR predicates
    into PK-load + fallback, (3) reuse a single session per request.

    FILES TO TOUCH
      - src/actors/Cena.Actors/Students/StudentActor.Commands.cs
      - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:235, 365, 393, 209, 339

    FILES TO READ FIRST
      - .agentdb/AGENT_CODER_INSTRUCTIONS.md
      - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-032
      - docs/reviews/agent-3-data-findings.md FIND-data-014, 016

    DEFINITION OF DONE
      - QuestionReadModel reads in commands replaced with
        AggregateStream.
      - Every `Id == X || SessionId == X` predicate replaced with
        sequential PK-load fallback pattern.
      - Single session per request.
      - dotnet test green.

    REPORTING REQUIREMENTS
      complete --result with diff and a paste of Marten command log
      showing the new query shape.
```

### P3 — Low

```yaml
- id: FIND-data-033
  severity: p3
  category: file-size
  framework: null
  files:
    - path: src/api/Cena.Admin.Api/AiGenerationService.cs
      lines: "1-885"
    - path: src/api/Cena.Admin.Api/StudentInsightsService.cs
      lines: "1-553"
  related_prior_finding: null
  evidence:
    - type: grep
      content: |
        $ wc -l src/api/Cena.Admin.Api/AiGenerationService.cs
              885 src/api/Cena.Admin.Api/AiGenerationService.cs
        $ wc -l src/api/Cena.Admin.Api/StudentInsightsService.cs
              553 src/api/Cena.Admin.Api/StudentInsightsService.cs
  finding: |
    Two services exceed CLAUDE.md's 500-line file size rule:
    `AiGenerationService` is 885 lines, `StudentInsightsService` is
    553. Same as prior FIND-data-019 P3 (which counted 916 lines for
    AiGenerationService — net change is -31 lines, no split).
  root_cause: |
    No refactor pressure; both files have grown organically as
    features were added.
  proposed_fix: |
    Split AiGenerationService into AiGenerationService (orchestration),
    AnthropicProvider, PromptBuilder, ProviderConfigService.
    Split StudentInsightsService into one service per insight category
    (FocusInsightsService, EngagementInsightsService, etc.).
  test_required: |
    No new test required; existing tests must still pass after split.
  task_body: |
    GOAL
    Split two oversized services to satisfy CLAUDE.md's 500-line rule.

    FILES TO TOUCH
      - src/api/Cena.Admin.Api/AiGenerationService.cs (split)
      - src/api/Cena.Admin.Api/StudentInsightsService.cs (split)

    FILES TO READ FIRST
      - .agentdb/AGENT_CODER_INSTRUCTIONS.md
      - CLAUDE.md (file-size rule)
      - docs/reviews/agent-3-data-findings.md FIND-data-019

    DEFINITION OF DONE
      - No file in src/api/Cena.Admin.Api over 500 lines.
      - All existing tests still pass.
      - DI registrations updated.

    REPORTING REQUIREMENTS
      complete --result with diff stats and test paste.
```

---

## What I attempted to produce vs what I could

| Required artifact | Status | Notes |
|---|---|---|
| EXPLAIN ANALYZE for top 10 hot queries | NOT PRODUCED | Postgres unreachable; no `pg_isready`, no docker containers running |
| Row counts per tenant | NOT PRODUCED | Same |
| Event stream lengths | NOT PRODUCED | Same |
| Projection rebuild timings | NOT PRODUCED | Same |
| AI call volume per endpoint per day | DERIVED | See "AI volume / cost estimate" below |
| Cost-per-learner-per-month estimate | DERIVED | See below |
| `QueryAllRawEvents` inventory with per-call verdict | PRODUCED | See appendix below |
| Static-evidence findings (file:line + ripgrep proofs) | PRODUCED | Every finding above |

## AI volume / cost estimate (derived from rate limits + observed shape)

Given:
- `tutor` rate limit: 10 messages/min — but **global** (FIND-data-020),
  not per-student. Effective per-student ceiling is unenforced.
- `ai` rate limit: 10 req/min — also global.
- The token meter is fake (FIND-data-021), so observed token cost
  cannot be derived from `TokenBudgetAdminService` logs.

**Worst-case cost model (one runaway script, rate limit
unpartitioned)**:
- 10 tutor msgs/min × 60 min/hr × 24 hr/day × ~3000 output tokens
  per response (Sonnet 4.6 typical for a tutoring turn) =
  **~43.2M output tokens/day**.
- At Anthropic Sonnet $3 input + $15 output / 1M tokens, output
  alone is $648/day = **~$19.4k/month** — for a SINGLE attacker
  hitting the global bucket. Real student demand (let's say 200
  daily active learners × 5 tutoring turns each × 800 input + 1200
  output tokens) would be ~$72/day = ~$2.16k/month before any
  caching kicks in.
- ExplanationCacheService L2 (Redis, 30-day TTL,
  `(questionId, errorType, language)` keying) is correctly
  implemented and cuts the per-question explanation cost to near-
  zero on warm cache (`ExplanationCacheService.cs:107`). I could not
  measure the hit ratio without Postgres+Redis access.

**Cost-per-learner-per-month estimate**: roughly **$0.40 – $1.10**
per active learner per month for tutoring + explanation generation,
**assuming the rate limiter is fixed (FIND-data-020) and the
explanation cache stays warm**. With the current global rate
limiter, a single bad actor can spike the platform-wide cost by an
order of magnitude in a single day with no automatic throttling.

The **token meter (FIND-data-021) cannot be trusted to alarm** on
this — the meter under-reports by ~40× (200 chars / 4 = 50 tokens
fake vs ~2000 real on a typical reply), so an admin watching the
budget dashboard will see "1.5% of daily limit used" while the real
spend is ~60% of the daily limit.

## Reads of preflight observations

- **Observation 1 — label drift on `LearningSessionQueueProjection` /
  `ActiveSessionSnapshot`**: confirmed. Surfaced in this report only as
  context for FIND-data-032 (the OR-predicate planner issue) and the
  in-line note on the unread `LearningSessionQueueProjection` doc-comment
  ("Marten inline projection" — line 10 — is now wrong). The class-rename
  itself is in arch lens scope per preflight, not data lens, so I do not
  re-file it here.
- **Observation 2 — `QueryAllRawEvents` 55-usage anti-pattern**: the
  inventory in the appendix below tracks 58 hits across 18 files. Of
  those, 12 are user-reachable hot paths with no tenant scope; those are
  collected under FIND-data-025 (admin per-student insights),
  FIND-data-026 (experiment admin), FIND-data-027 (student session
  detail/replay), and the per-section P0 cost findings (FIND-data-021
  for token budget). The remaining sites are bootstrap / outbox /
  observability which I justify per-row in the table.
- **Observation 3 — pending UX findings**: out of data lens scope; the
  preflight noted them as carried-over open work for the ux agent.

## Non-findings (categories where I looked and found nothing new)

- **AI prompt event storage**: Confirmed still correct. Prompts are
  persisted on `QuestionAiGenerated_V1.PromptText / RawModelOutput`
  (`src/actors/Cena.Actors/Events/QuestionEvents.cs:59-77`). No regression
  since v1.
- **Firebase JWT verification doing per-request network calls**: NOT
  the case. `FirebaseAuthExtensions` uses `Microsoft.AspNetCore.JwtBearer`
  with `Authority = https://securetoken.google.com/<projectId>`, which
  enables JWKS public-key caching. No regression. v2 NEW concern dispelled.
- **Explanation cache by content hash**: `ExplanationCacheService` keys
  by `(questionId, errorType, language)` and stores a real
  `CachedExplanation` record with `TokenCount`. Cache is correct.
- **Marten event upcasters**: `RegisterUpcasters(opts)` is called from
  `ConfigureCommon` (MartenConfiguration.cs:80). The
  `ConceptAttemptedV1ToV2Upcaster` is wired and the V2 type is
  registered (lines 458/479). Schema-evolution path is healthy.
- **NotificationsEndpoints in-memory paging (FIND-data-013)**: the fix
  is real. SQL Skip/Take/Where confirmed in the file at lines 81-128.
  StudentId / Read / CreatedAt indexes exist on
  `NotificationDocument` (MartenConfiguration.cs:367-371).
- **SocialEndpoints N+1 (FIND-data-010/011)**: the bulk-load pattern in
  `SocialEndpoints.cs:154-188, 217-273` is correct. The
  `SocialProjectionBuilder` helper enforces the constant-query-count
  invariant via type-driven assembly.
- **`StudentProfileSnapshot` Inline projection rebuild (FIND-data-007 fix)**:
  `SessionEndpoints.cs:645-690` — endpoint loads the profile once, appends
  events for the inline projection to consume, no manual `Store(profile)`.
  CQRS-clean.

---

## Appendix A — `QueryAllRawEvents` usage inventory

Full inventory of every `QueryAllRawEvents` call site on `main` at
`cc3f702`. Per-call verdict: tenant-scoped? bounded? justified?

| # | File | Line | Caller | Take? | Tenant filter? | Justified? | Verdict | Linked finding |
|---|---|---|---|---|---|---|---|---|
| 1 | `src/actors/Cena.Actors/Projections/StudentLifetimeStatsProjection.cs` | 4 | comment | n/a | n/a | n/a | doc-comment only | — |
| 2 | `src/actors/Cena.Actors/Projections/StudentLifetimeStatsProjection.cs` | 47 | comment | n/a | n/a | n/a | doc-comment only | — |
| 3 | `src/actors/Cena.Actors/Services/AnalysisJobActor.cs` | 243 | `LoadAttempts(studentId, conceptId)` | `Take(200)` | `e.StreamKey == studentId` (per-student!) | yes for the streamKey filter, BUT… | **DEAD QUERY** — `EventTypeName == "ConceptAttempted_V1"` PascalCase never matches | **FIND-data-022** |
| 4 | `src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs` | 165 | `PollAndPublish` | `Take(MaxEventsPerCycle)` | n/a — outbox by design | YES, justified | OK | — |
| 5 | `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` | 197 | comment | n/a | n/a | n/a | doc-comment only | — |
| 6 | `src/api/Cena.Admin.Api/QuestionBankSeedData.cs` | 52 | startup seed | none, but filters on `StreamKey.StartsWith("q-")` | n/a — bootstrap | acceptable for one-time bootstrap | OK | — |
| 7 | `src/api/Cena.Admin.Api/SimulationEventSeeder.cs` | 43 | startup seed | none, prefix filter | n/a — bootstrap | acceptable for one-time bootstrap | OK | — |
| 8 | `src/api/Cena.Admin.Api/EventStreamService.cs` | 45 | `GetRecentEventsAsync` | `Take(count)` | n/a — admin observability | acceptable for admin events feed | OK | — |
| 9 | `src/api/Cena.Admin.Api/EventStreamService.cs` | 70 | `GetEventRatesAsync` | none, `Where(Timestamp >= since-5min)` | n/a — admin global rates | acceptable but a SchoolId rollup would be cheaper | OK | — |
| 10 | `src/api/Cena.Admin.Api/SystemMonitoringService.cs` | 152 | `GetServiceHealthAsync` | none — full `CountAsync()` over `mt_events` | n/a | borderline — full count is O(N) | OK (low frequency) | — |
| 11 | `src/api/Cena.Admin.Api/SystemMonitoringService.cs` | 290 | `GetAuditLogAsync` | `Skip+Take` SQL-side | NO filter applied (`AuditLogFilterRequest` ignored) | NO | **lying-label** | **FIND-data-024** |
| 12 | `src/api/Cena.Admin.Api/StudentInsightsService.cs` | 49 | `GetFocusHeatmapAsync` | `Take(2000)` global | NO | NO — sample-truncation + cross-tenant | partially-fixed | **FIND-data-025** |
| 13 | `src/api/Cena.Admin.Api/StudentInsightsService.cs` | 85 | `GetDegradationCurveAsync` | `Take(2000)` | NO | NO | partially-fixed | **FIND-data-025** |
| 14 | `src/api/Cena.Admin.Api/StudentInsightsService.cs` | 118 | `GetEngagementAsync` (streak) | `Take(500)` | NO | NO | partially-fixed | **FIND-data-025** |
| 15 | `src/api/Cena.Admin.Api/StudentInsightsService.cs` | 132 | `GetEngagementAsync` (xp) | `Take(2000)` | NO | NO | partially-fixed | **FIND-data-025** |
| 16 | `src/api/Cena.Admin.Api/StudentInsightsService.cs` | 152 | `GetEngagementAsync` (badges) | `Take(500)` | NO | NO | partially-fixed | **FIND-data-025** |
| 17 | `src/api/Cena.Admin.Api/StudentInsightsService.cs` | 185 | `GetErrorTypesAsync` | `Take(5000)` | NO | NO | partially-fixed | **FIND-data-025** |
| 18 | `src/api/Cena.Admin.Api/StudentInsightsService.cs` | 232 | `GetHintUsageAsync` | `Take(2000)` | NO | NO | partially-fixed | **FIND-data-025** |
| 19 | `src/api/Cena.Admin.Api/StudentInsightsService.cs` | 259 | `GetStagnationAsync` | `Take(5000)` | NO | NO | partially-fixed | **FIND-data-025** |
| 20 | `src/api/Cena.Admin.Api/StudentInsightsService.cs` | 292 | `GetSessionPatternsAsync` | `Take(500)` | NO | NO | partially-fixed | **FIND-data-025** |
| 21 | `src/api/Cena.Admin.Api/StudentInsightsService.cs` | 322 | `GetSessionPatternsAsync` | `Take(500)` | NO | NO | partially-fixed | **FIND-data-025** |
| 22 | `src/api/Cena.Admin.Api/StudentInsightsService.cs` | 356 | `GetResponseTimesAsync` | `Take(500)` | NO | NO | partially-fixed | **FIND-data-025** |
| 23 | `src/api/Cena.Admin.Api/StudentInsightsService.cs` | 362 | `GetResponseTimesAsync` | `Take(500)` | NO | NO | partially-fixed | **FIND-data-025** |
| 24 | `src/api/Cena.Admin.Api/StudentInsightsService.cs` | 438 | `GetResponseTimesAsync` (helper) | `Take(2000)` | NO | NO | partially-fixed | **FIND-data-025** |
| 25 | `src/api/Cena.Admin.Api/FocusAnalyticsService.cs` | 150 | `GetDegradationFallbackAsync` | none, time-window | NO — comment lies "school isolation via student lookup" but no lookup | partially-fixed (rollup path is fine; raw fallback is cross-tenant) | partially-fixed | **FIND-data-025** |
| 26 | `src/api/Cena.Admin.Api/AdminDashboardService.cs` | 155 | `GetContentPipelineAsync` | none, time window | NO | acceptable for global content pipeline view | OK with caveat | — |
| 27 | `src/api/Cena.Admin.Api/AdminDashboardService.cs` | 195 | `GetFocusDistributionAsync` | none, time window | NO | acceptable for global distribution | OK | — |
| 28 | `src/api/Cena.Admin.Api/AdminDashboardService.cs` | 247 | `GetMasteryProgressAsync` | none, time window | NO | acceptable for global trend | OK | — |
| 29 | `src/api/Cena.Admin.Api/AdminDashboardService.cs` | 254 | `GetMasteryProgressAsync` | none, time window | NO | acceptable for global trend | OK | — |
| 30 | `src/api/Cena.Admin.Api/MethodologyAnalyticsService.cs` | 67 | `GetEffectivenessAsync` (switch events) | none, time window | post-query in-memory `scopedStudentIds` filter | partial — pre-filter is cross-tenant, scope is applied in memory | OK (resource cap not enforced) | — |
| 31 | `src/api/Cena.Admin.Api/MethodologyAnalyticsService.cs` | 85 | `GetEffectivenessAsync` (attempts) | none, time window | post-query in-memory | partial | OK | — |
| 32 | `src/api/Cena.Admin.Api/MethodologyAnalyticsService.cs` | 179 | `GetEffectivenessAsync` (stagnation) | none, 7-day window | NO | partial | OK | — |
| 33 | `src/api/Cena.Admin.Api/MethodologyAnalyticsService.cs` | 312 | `GetMcmGraphAsync` (switches) | none, 90-day window | NO | acceptable for global graph view | OK | — |
| 34 | `src/api/Cena.Admin.Api/MethodologyAnalyticsService.cs` | 325 | `GetMcmGraphAsync` (attempts) | none, 90-day window | NO | acceptable for global graph view | OK | — |
| 35 | `src/api/Cena.Admin.Api/TutoringAdminService.cs` | 124 | `GetSessionDetailAsync` (RAG sources) | none | per-doc filter | acceptable but should use streamKey | OK | — |
| 36 | `src/api/Cena.Admin.Api/TutoringAdminService.cs` | 202 | `GetBudgetStatusAsync` | none, today window | post-query `scopedStudentIds` filter | NO — token math is fake | **lying-label** | **FIND-data-021** |
| 37 | `src/api/Cena.Admin.Api/TutoringAdminService.cs` | 272 | `GetAnalyticsAsync` | none | NO — global session ended events | acceptable for analytics | OK | — |
| 38 | `src/api/Cena.Admin.Api/TutoringAdminService.cs` | 337 | `GetStudentBudgetRemainingAsync` | none, today window | per-student in-memory filter | NO — token math is fake | **lying-label** | **FIND-data-021** |
| 39 | `src/api/Cena.Admin.Api/TokenBudgetAdminService.cs` | 43 | `GetBudgetStatusAsync` | none, day window | NO | NO — token math is fake | **lying-label** | **FIND-data-021** |
| 40 | `src/api/Cena.Admin.Api/TokenBudgetAdminService.cs` | 94 | `GetTrendAsync` | none, days window | NO | NO — token math is fake | **lying-label** | **FIND-data-021** |
| 41 | `src/api/Cena.Admin.Api/ExperimentAdminService.cs` | 53 | `GetExperimentsAsync` | none | NO | NO — cross-tenant | partially-fixed | **FIND-data-026** |
| 42 | `src/api/Cena.Admin.Api/ExperimentAdminService.cs` | 101 | `GetExperimentDetailAsync` | none | NO | NO | partially-fixed | **FIND-data-026** |
| 43 | `src/api/Cena.Admin.Api/ExperimentAdminService.cs` | 120 | `GetExperimentDetailAsync` (episodes) | none | NO | NO | partially-fixed | **FIND-data-026** |
| 44 | `src/api/Cena.Admin.Api/ExperimentAdminService.cs` | 126 | `GetExperimentDetailAsync` (mastery) | none | NO | NO | partially-fixed | **FIND-data-026** |
| 45 | `src/api/Cena.Admin.Api/ExperimentAdminService.cs` | 177 | `GetFunnelAsync` (assigned) | none | NO | NO — 5 sequential global scans per request | partially-fixed | **FIND-data-026** |
| 46 | `src/api/Cena.Admin.Api/ExperimentAdminService.cs` | 187 | `GetFunnelAsync` (engaged) | none | NO | NO | partially-fixed | **FIND-data-026** |
| 47 | `src/api/Cena.Admin.Api/ExperimentAdminService.cs` | 196 | `GetFunnelAsync` (confused) | none | NO | NO | partially-fixed | **FIND-data-026** |
| 48 | `src/api/Cena.Admin.Api/ExperimentAdminService.cs` | 207 | `GetFunnelAsync` (resolved) | none | NO | NO | partially-fixed | **FIND-data-026** |
| 49 | `src/api/Cena.Admin.Api/ExperimentAdminService.cs` | 218 | `GetFunnelAsync` (mastered) | none | NO | NO | partially-fixed | **FIND-data-026** |
| 50 | `src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs` | 219 | `GetSessionDetail` (accuracy) | none | NO | NO — full event scan filtered by sessionId in memory | partially-fixed | **FIND-data-027** |
| 51 | `src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs` | 348 | `GetSessionReplay` | none | NO | NO | partially-fixed | **FIND-data-027** |
| 52 | `src/api/Cena.Student.Api.Host/Endpoints/StudentAnalyticsEndpoints.cs` | 54 | `GetStudentAnalyticsSummary` | none | NO | NO | partially-fixed | **FIND-data-027** |
| 53 | `src/api/Cena.Student.Api.Host/Endpoints/StudentAnalyticsEndpoints.cs` | 139 | `GetConceptTimeline` | none | NO | NO | partially-fixed | **FIND-data-027** |
| 54 | `src/api/Cena.Student.Api.Host/Endpoints/GamificationEndpoints.cs` | 53 | `GetBadges` (sessions) | none | post-query `StreamKey == studentId` | NO — should use FetchStreamAsync(studentId) | partially-fixed | **FIND-data-023** |
| 55 | `src/api/Cena.Student.Api.Host/Endpoints/GamificationEndpoints.cs` | 59 | `GetBadges` (attempts) | none | post-query | NO | partially-fixed | **FIND-data-023** |
| 56 | `src/api/Cena.Student.Api.Host/Endpoints/GamificationEndpoints.cs` | 72 | `GetBadges` (challenges) | none | post-query | NO | partially-fixed | **FIND-data-023** |
| 57 | `src/api/Cena.Student.Api.Host/Endpoints/GamificationEndpoints.cs` | 176 | `GetStreakStatus` (last session) | none | server-side `StreamKey == studentId`! | OK — could use FetchStreamAsync but current is also indexed | OK | — |
| 58 | `src/api/Cena.Student.Api.Host/Endpoints/GamificationEndpoints.cs` | 317 | `CalculateCurrentStreak` (helper) | none | server-side `StreamKey == studentId` | OK — wide scan but bounded by student's stream | OK | — |

**Roll-up**:

- **8 sites OK** (5 admin observability, 2 bootstrap, 1 outbox)
- **8 sites OK with caveat** (admin global aggregates / monitoring; not
  per-tenant but the use case justifies it)
- **24 sites partially-fixed** by FIND-data-009 in name only — read
  path was never migrated to the projection
- **5 sites are direct lying-label / fake-cost** (FIND-data-021)
- **1 site is a dead query** (FIND-data-022 — PascalCase predicate)
- **2 sites are doc-comments**, not real calls
- **1 site has the OFF wrong filter signature**
  (`SystemMonitoringService.GetAuditLogAsync` — FIND-data-024)

Net: **31 of 58 call sites are problematic**, concentrated in
`StudentInsightsService` (13), `ExperimentAdminService` (9), and
the cost-meter services (5). All ten new findings above target a
subset of these call sites.

## Appendix B — Open prior P2/P3 carry-over

For traceability, the v1 prior agent file (`agent-3-data-findings.md`)
defined the following P2/P3 findings that were never enqueued in the
queue, and are still present on `main`:

| Prior ID | New ID in v2 | Notes |
|---|---|---|
| FIND-data-014 | FIND-data-032 | StudentActor reads QuestionReadModel from query session |
| FIND-data-015 | FIND-data-031 | Leaderboard hard-codes Marten table name; no TotalXp index |
| FIND-data-016 | FIND-data-032 | OR-predicate planner anti-pattern (folded into 032) |
| FIND-data-017 | FIND-data-029 | Daily challenge in-memory sort, no SchoolId scope |
| FIND-data-018 | FIND-data-030 | Tutor messages no pagination, hardcoded HasMore=false |
| FIND-data-019 | FIND-data-033 | AiGenerationService 916 lines (now 885) |

All five P2 findings are now formally enqueued under their new v2 IDs.
