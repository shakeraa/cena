# REV-013: FERPA Compliance Framework (Audit Logging, Data Retention, Access Controls)

**Priority:** P1 -- HIGH (education platform processing student PII without formal compliance controls)
**Blocked by:** REV-003 (backup -- prerequisite for data retention)
**Blocks:** Production launch in US/IL education markets
**Estimated effort:** 5 days
**Source:** System Review 2026-03-28 -- Cyber Officer 2 (F-COMP-01/02/03), Cyber Officer 1 (Finding 16, 18)
**Related:** SEC-005 (GDPR), SEC-008 (Audit), DATA-011 (GDPR)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

Cena processes student education records including: learning attempts, mastery scores, tutoring session transcripts, focus analytics, engagement data, and assessment results. Under FERPA (US) and Israeli privacy law, these are protected records requiring:
- Audit trail of who accessed what student records
- Data retention and deletion policies
- Access controls scoped to authorized school personnel
- Consent management for minors

Currently: no audit logging for record access, no data retention policy (events accumulate indefinitely), no consent framework, and most admin endpoints serve cross-school data to any moderator+.

## Architect's Decision

Implement in three layers:
1. **Audit middleware**: Log every access to student-identifiable data endpoints (who, what student, when, what action)
2. **Data retention**: Event store archival policy (configurable per stream), with Marten's built-in archival support
3. **Access logging**: Dedicated `StudentRecordAccessLog` Marten document for compliance queries

Do NOT attempt consent management in this task -- that requires UX design for parent/guardian flows. File as a separate task.

## Subtasks

### REV-013.1: Create Audit Logging Middleware for Student Data Access

**File to create:** `src/shared/Cena.Infrastructure/Compliance/StudentDataAuditMiddleware.cs`

```csharp
public class StudentDataAuditMiddleware
{
    private static readonly HashSet<string> AuditedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/admin/mastery",
        "/api/admin/focus",
        "/api/admin/tutoring",
        "/api/admin/outreach",
        "/api/admin/cultural",
        "/api/v1/mastery"
    };

    public async Task InvokeAsync(HttpContext context, IDocumentSession session)
    {
        var path = context.Request.Path.Value ?? "";

        if (!AuditedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var uid = context.User.FindFirstValue("user_id");
        var role = context.User.FindFirstValue("cena_role");
        var school = context.User.FindFirstValue("school_id");

        // Extract student ID from route if present
        var studentId = ExtractStudentId(context.Request);

        await _next(context);

        // Log after response to capture status code
        session.Store(new StudentRecordAccessLog
        {
            Id = Guid.NewGuid(),
            AccessedAt = DateTimeOffset.UtcNow,
            AccessedBy = uid ?? "anonymous",
            AccessorRole = role ?? "unknown",
            AccessorSchool = school,
            StudentId = studentId,
            Endpoint = path,
            HttpMethod = context.Request.Method,
            StatusCode = context.Response.StatusCode,
            IpAddress = context.Connection.RemoteIpAddress?.ToString()
        });

        await session.SaveChangesAsync();
    }
}
```

**File to create:** `src/shared/Cena.Infrastructure/Compliance/StudentRecordAccessLog.cs`

```csharp
public class StudentRecordAccessLog
{
    public Guid Id { get; set; }
    public DateTimeOffset AccessedAt { get; set; }
    public string AccessedBy { get; set; } = "";
    public string AccessorRole { get; set; } = "";
    public string? AccessorSchool { get; set; }
    public string? StudentId { get; set; }
    public string Endpoint { get; set; } = "";
    public string HttpMethod { get; set; } = "";
    public int StatusCode { get; set; }
    public string? IpAddress { get; set; }
}
```

**Acceptance:**
- [ ] Every GET/POST to student data endpoints creates an audit log entry
- [ ] Audit log includes accessor identity, role, school, target student, endpoint, timestamp
- [ ] Audit log is queryable via Marten (indexed on AccessedBy, StudentId, AccessedAt)
- [ ] Audit log is append-only (no update/delete endpoints)

### REV-013.2: Create Audit Log Admin Endpoint

**File to create:** `src/api/Cena.Admin.Api/ComplianceEndpoints.cs`

Endpoints (SuperAdminOnly):
- `GET /api/admin/compliance/audit-log?studentId=X&from=Y&to=Z` -- query audit log
- `GET /api/admin/compliance/audit-log/summary` -- aggregate stats (accesses by role, by school)
- `GET /api/admin/compliance/data-retention` -- current retention policy status

**Acceptance:**
- [ ] SuperAdmin can query who accessed a specific student's records
- [ ] Results filterable by date range, accessor, student, school
- [ ] Summary endpoint shows access patterns for compliance reporting

### REV-013.3: Configure Event Store Data Retention

**File to modify:** `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs`

```csharp
// Add retention policy metadata to event store configuration
opts.Events.AddEventType(typeof(DataRetentionPolicy));

// Configure Marten's built-in archival for old events
opts.Schema.For<StudentRecordAccessLog>()
    .Index(x => x.AccessedBy)
    .Index(x => x.StudentId)
    .Index(x => x.AccessedAt);
```

**File to create:** `src/shared/Cena.Infrastructure/Compliance/DataRetentionPolicy.cs`

```csharp
public static class DataRetentionPolicy
{
    // Education records: 7 years after last enrollment (FERPA)
    public static readonly TimeSpan StudentRecordRetention = TimeSpan.FromDays(365 * 7);

    // Audit logs: 5 years (compliance best practice)
    public static readonly TimeSpan AuditLogRetention = TimeSpan.FromDays(365 * 5);

    // Session analytics: 2 years
    public static readonly TimeSpan AnalyticsRetention = TimeSpan.FromDays(365 * 2);

    // Engagement data (XP, streaks): 1 year after inactivity
    public static readonly TimeSpan EngagementRetention = TimeSpan.FromDays(365);
}
```

**Acceptance:**
- [ ] Retention periods defined and documented per data category
- [ ] Retention periods align with FERPA 7-year requirement for education records
- [ ] Archival implementation deferred to a scheduled background job (document the approach, don't implement the job in this task)

### REV-013.4: Add AllowedHosts Configuration

**Files to modify:**
- `src/api/Cena.Api.Host/appsettings.json`
- `src/actors/Cena.Actors.Host/appsettings.json`

```json
// BEFORE
"AllowedHosts": "*"

// AFTER (development)
"AllowedHosts": "localhost"
```

```json
// appsettings.Production.json (to create)
"AllowedHosts": "admin.cena.edu;api.cena.edu"
```

**Acceptance:**
- [ ] `AllowedHosts` is not `*` in any environment
- [ ] Development allows `localhost`
- [ ] Production template restricts to expected hostnames
