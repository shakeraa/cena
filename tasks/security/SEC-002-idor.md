# SEC-002: GraphQL @auth Directives, Resolver Scoping, IDOR Prevention

**Priority:** P0 — blocks all multi-tenant queries
**Blocked by:** SEC-001 (Firebase Auth)
**Estimated effort:** 2 days
**Contract:** `contracts/frontend/graphql-schema.graphql`, `contracts/REVIEW_security.md` (C-4: IDOR)

---

## Context

The GraphQL schema exposes queries like `knowledgeGraph(studentId, subjectId)` and `recentSessions(studentId)` that accept a `studentId` parameter. Without enforcement, Student A can query Student B's mastery maps, session history, and learning difficulties. This is an IDOR (Insecure Direct Object Reference) vulnerability flagged as CRITICAL in the security review (C-4). UUIDv7 IDs are time-sortable, making enumeration feasible.

## Subtasks

### SEC-002.1: @auth Directive Implementation

**Files to create/modify:**
- `src/Cena.Web/GraphQL/Directives/AuthDirective.cs` — custom schema directive
- `src/Cena.Web/GraphQL/Directives/AuthDirectiveType.cs` — HotChocolate directive type
- `src/Cena.Web/GraphQL/SchemaConfiguration.cs` — register directive

**Acceptance:**
- [ ] `@auth(roles: [STUDENT])` directive applied to all student self-queries (`myProfile`, `myKnowledgeGraph`, etc.)
- [ ] `@auth(roles: [TEACHER], scoped: true)` applied to `classOverview`, `studentDetail`, `knowledgeGapAnalysis`
- [ ] `@auth(roles: [PARENT], scoped: true)` applied to `childProgress`, `weeklyReport`, `riskAlerts`
- [ ] `@auth(roles: [ADMIN])` applied to admin-only mutations
- [ ] Directive enforced at field resolver level, not just type level
- [ ] Missing or invalid role -> `FORBIDDEN` error with GraphQL error extension `{ "code": "FORBIDDEN" }`
- [ ] Directive evaluated BEFORE resolver execution (no data leaks via timing)
- [ ] Schema introspection restricted to authenticated users only

**Test:**
```csharp
[Fact]
public async Task AuthDirective_StudentCannotAccessTeacherQuery()
{
    var token = await GetStudentToken();
    var result = await ExecuteGraphQL(token, @"
        query { classOverview(classRoomId: ""class-1"") { name } }
    ");
    Assert.Contains("FORBIDDEN", result.Errors[0].Code);
}

[Fact]
public async Task AuthDirective_TeacherCanAccessClassOverview()
{
    var token = await GetTeacherToken(classRoomIds: new[] { "class-1" });
    var result = await ExecuteGraphQL(token, @"
        query { classOverview(classRoomId: ""class-1"") { name } }
    ");
    Assert.Null(result.Errors);
    Assert.NotNull(result.Data["classOverview"]);
}
```

**Edge cases:**
- Token with no `role` claim -> treat as unauthenticated, reject all queries
- Multiple roles (admin who is also a teacher) -> union of permissions
- Schema introspection probe without auth -> 401

---

### SEC-002.2: Resolver-Level Student ID Enforcement

**Files to create/modify:**
- `src/Cena.Web/GraphQL/Resolvers/StudentQueryResolvers.cs`
- `src/Cena.Web/GraphQL/Middleware/OwnershipEnforcementMiddleware.cs`
- `src/Cena.Web/Services/RelationshipService.cs` — teacher-student, parent-child relationship lookups

**Acceptance:**
- [ ] For STUDENT role callers: `studentId` parameter IGNORED; forced to JWT `sub` claim
- [ ] `knowledgeGraph(studentId)` for students -> always returns own graph regardless of parameter
- [ ] For TEACHER role callers: `studentId` validated against `classRoomIds` claim -> student must be in teacher's classroom
- [ ] For PARENT role callers: `studentId` validated against `childIds` claim
- [ ] For ADMIN role callers: no restriction on `studentId`
- [ ] Cross-student access attempt logged: `{ event: "idor_attempt", callerUid, targetStudentId, query, timestamp }`
- [ ] IDOR attempt counter: 5 attempts in 10 minutes -> temporary account lock + alert to admin
- [ ] Subscription queries (`onKnowledgeGraphUpdate`, `onSessionProgress`) enforce ownership at subscription time
- [ ] Leaderboard `GLOBAL` scope: student names anonymized (initials only) unless same class

**Test:**
```csharp
[Fact]
public async Task StudentCannotAccessOtherStudentGraph()
{
    var studentAToken = await GetStudentToken(uid: "student-a");
    var result = await ExecuteGraphQL(studentAToken, @"
        query { knowledgeGraph(studentId: ""student-b"", subjectId: ""math"") { overallMastery studentId } }
    ");
    // Should return student-a's graph, NOT student-b's
    Assert.Equal("student-a", result.Data["knowledgeGraph"]["studentId"].ToString());
}

[Fact]
public async Task TeacherCannotAccessStudentOutsideClassroom()
{
    var teacherToken = await GetTeacherToken(classRoomIds: new[] { "class-1" });
    var result = await ExecuteGraphQL(teacherToken, @"
        query { knowledgeGraph(studentId: ""student-in-class-2"", subjectId: ""math"") { overallMastery } }
    ");
    Assert.Contains("FORBIDDEN", result.Errors[0].Code);
}

[Fact]
public async Task IdorAttemptIsLogged()
{
    var studentToken = await GetStudentToken(uid: "attacker");
    for (int i = 0; i < 5; i++)
    {
        await ExecuteGraphQL(studentToken, $@"
            query {{ studentProfile(studentId: ""victim-{i}"") {{ displayName }} }}
        ");
    }
    var logs = await GetSecurityLogs(callerUid: "attacker", eventType: "idor_attempt");
    Assert.True(logs.Count >= 5);
}
```

**Edge cases:**
- Teacher reassigned to different classroom mid-session -> claims not updated until token refresh (1 hour max)
- Parent-child link revoked -> existing tokens still have old `childIds` until refresh
- Subscription held open after ownership revoked -> server-side periodic revalidation (every 5 min)

---

### SEC-002.3: IDOR Regression Test Suite + Automated Scanning

**Files to create/modify:**
- `tests/Security/IdorTests.cs` — comprehensive IDOR test matrix
- `scripts/security/idor-scanner.sh` — automated IDOR detection script
- `.github/workflows/security-idor.yml` — CI pipeline for IDOR tests

**Acceptance:**
- [ ] Test matrix covers all 15+ queries with `studentId`/`childId`/`classRoomId` parameters
- [ ] Each query tested with: own ID (allowed), other student ID (blocked), teacher->own class (allowed), teacher->other class (blocked), parent->own child (allowed), parent->other child (blocked)
- [ ] Mutation IDOR tests: `acknowledgeRiskAlert` validates caller is the parent
- [ ] Subscription IDOR tests: `onSessionProgress` validates observer authorization
- [ ] Automated scanner runs in CI on every PR touching `GraphQL/` or `Resolvers/`
- [ ] Zero tolerance: any IDOR finding blocks PR merge

**Test:**
```csharp
[Theory]
[InlineData("myProfile", "student", "student-a", true)]
[InlineData("knowledgeGraph", "student", "student-b", false)]
[InlineData("knowledgeGraph", "teacher-class-1", "student-in-class-1", true)]
[InlineData("knowledgeGraph", "teacher-class-1", "student-in-class-2", false)]
[InlineData("childProgress", "parent-of-a", "student-a", true)]
[InlineData("childProgress", "parent-of-a", "student-b", false)]
public async Task IdorMatrix(string query, string callerRole, string targetId, bool shouldSucceed)
{
    var token = await GetTokenForRole(callerRole);
    var result = await ExecuteTargetedQuery(token, query, targetId);

    if (shouldSucceed)
        Assert.Null(result.Errors);
    else
        Assert.Contains("FORBIDDEN", result.Errors?.FirstOrDefault()?.Code ?? "");
}
```

---

## Rollback Criteria
If authorization enforcement causes widespread access failures:
- Temporarily relax teacher/parent scoping to allow any authenticated user with correct role
- Keep student self-enforcement (hardest to break, most critical)
- Log all access for manual audit during relaxed period

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] IDOR test matrix covers 100% of parameterized queries
- [ ] `dotnet test --filter "Category=IDOR"` -> 0 failures
- [ ] Staging: manual penetration test confirms no cross-student data access
- [ ] CI pipeline blocks PRs with IDOR regressions
- [ ] PR reviewed by architect
