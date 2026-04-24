# REV-014: Complete Tenant Isolation (Apply SameOrg Policy Across All Endpoints)

**Priority:** P2 -- MEDIUM (cross-tenant data leak: moderator at School A can see School B's students)
**Blocked by:** None
**Blocks:** Multi-school deployment
**Estimated effort:** 3 days
**Source:** System Review 2026-03-28 -- Solution Architect (Tenant section, Finding #2)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

Multi-tenancy is partially implemented. `AdminUserService` scopes queries by `school_id` claim for non-SUPER_ADMIN users. However:
- The `SameOrg` authorization policy is defined in `CenaAuthPolicies` but **applied to zero endpoints**
- `FocusAnalyticsService`, `MasteryTrackingService`, `ContentModerationService`, `MethodologyAnalyticsService`, `OutreachEngagementService`, `TutoringAdminService`, and `CulturalContextService` serve data across ALL schools
- NATS messages carry no tenant context (student events have no school_id)
- Marten uses `TenancyStyle.Single` (no database-level partitioning)

A moderator at School A can view mastery data, focus analytics, and tutoring sessions for students at School B.

## Architect's Decision

Implement **application-level tenant filtering** consistently across all admin services. The pattern already exists in `AdminUserService` -- extend it to all services that query student data.

Do NOT switch to Marten multi-tenancy (`TenancyStyle.Conjoined`) -- that's a schema-level change with migration risk. Application filtering is sufficient when done consistently.

Add a **shared tenant filter helper** that all services call, so the scoping logic lives in one place.

## Subtasks

### REV-014.1: Create Shared Tenant Scoping Helper

**File to create:** `src/shared/Cena.Infrastructure/Tenancy/TenantScope.cs`

```csharp
namespace Cena.Infrastructure.Tenancy;

public static class TenantScope
{
    /// <summary>
    /// Extracts school_id from the current user's claims.
    /// SUPER_ADMIN returns null (sees all schools).
    /// Non-SUPER_ADMIN returns their school_id.
    /// </summary>
    public static string? GetSchoolFilter(ClaimsPrincipal user)
    {
        var role = user.FindFirstValue("cena_role");
        if (role == "SUPER_ADMIN") return null; // No filter -- sees everything

        var schoolId = user.FindFirstValue("school_id");
        if (string.IsNullOrEmpty(schoolId))
            throw new UnauthorizedAccessException(
                "User has no school_id claim. Cannot determine tenant scope.");

        return schoolId;
    }

    /// <summary>
    /// Apply school filter to an IQueryable of documents that have a SchoolId property.
    /// </summary>
    public static IQueryable<T> FilterBySchool<T>(
        IQueryable<T> query, string? schoolId, Expression<Func<T, string?>> schoolSelector)
    {
        if (schoolId is null) return query; // SUPER_ADMIN -- no filter
        return query.Where(BuildFilter(schoolSelector, schoolId));
    }
}
```

### REV-014.2: Apply Tenant Scoping to All Student-Data Services

**Files to modify (add school_id filtering):**

| Service | File | Query to Filter |
|---------|------|-----------------|
| `FocusAnalyticsService` | `FocusAnalyticsService.cs` | All student focus queries |
| `MasteryTrackingService` | `MasteryTrackingService.cs` | Mastery distribution, at-risk, class mastery |
| `ContentModerationService` | `ContentModerationService.cs` | Moderation queue (by submitter's school) |
| `MethodologyAnalyticsService` | `MethodologyAnalyticsService.cs` | Stagnation, methodology effectiveness |
| `OutreachEngagementService` | `OutreachEngagementService.cs` | Student outreach history |
| `TutoringAdminService` | `TutoringAdminService.cs` | Session list, session detail |
| `CulturalContextService` | `CulturalContextService.cs` | Cultural distribution, resilience |
| `AdminDashboardService` | `AdminDashboardService.cs` | Overview stats, activity, mastery progress |

**Pattern for each service:**
```csharp
public async Task<FocusOverview> GetOverviewAsync(ClaimsPrincipal user)
{
    var schoolId = TenantScope.GetSchoolFilter(user);
    await using var session = _store.QuerySession();

    var query = session.Query<StudentProfileSnapshot>();
    if (schoolId is not null)
        query = query.Where(s => s.SchoolId == schoolId);

    // ... rest of query
}
```

**Acceptance:**
- [ ] All 8 services filter by school_id when the caller is not SUPER_ADMIN
- [ ] SUPER_ADMIN still sees all schools (no filter applied)
- [ ] Non-SUPER_ADMIN with missing school_id claim gets 401
- [ ] `TenantScope.GetSchoolFilter()` is the single source of truth for scoping

### REV-014.3: Add SchoolId to Student Profile Snapshot

**File to modify:** `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs`

If `SchoolId` is not already a field on the snapshot, add it and populate from the student's enrollment event.

**Acceptance:**
- [ ] `StudentProfileSnapshot` has a `SchoolId` property
- [ ] SchoolId is indexed in Marten for efficient filtered queries
- [ ] Existing snapshots are backfilled on next projection rebuild

### REV-014.4: Add Tenant Context to NATS Bus Messages

**File to modify:** `src/actors/Cena.Actors/Bus/NatsBusMessages.cs`

Add `SchoolId` to `BusEnvelope<T>`:
```csharp
public record BusEnvelope<T>(
    string MessageId,
    DateTimeOffset Timestamp,
    string Source,
    string? SchoolId,  // NEW: tenant context
    T Payload);
```

**File to modify:** `src/emulator/Program.cs` -- include school assignment per simulated student

**Acceptance:**
- [ ] All NATS messages carry SchoolId in the envelope
- [ ] Emulator assigns students to 2-3 simulated schools
- [ ] NatsEventSubscriber can filter events by school if needed
