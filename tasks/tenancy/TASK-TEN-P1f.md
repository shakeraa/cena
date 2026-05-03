# TASK-TEN-P1f: TenantScope.GetInstituteFilter

**Phase**: 1
**Priority**: high
**Effort**: 0.5d
**Depends on**: TEN-P1e
**Blocks**: Phase 2 (TEN-P2a through TEN-P2f)
**Queue ID**: `t_f6b1364b1892`
**Assignee**: unassigned
**Status**: blocked

---

## Goal

Add `TenantScope.GetInstituteFilter` that returns a single-element list in Phase 1, keeping existing `GetSchoolFilter` as a thin alias. This is the tenant-scoping bridge that lets all admin queries start using institute-based filtering without any behavior change.

## Background

Today `TenantScope.GetSchoolFilter` returns a single `string?` (null for SUPER_ADMIN, school_id for everyone else). ADR-0001 replaces this with `GetInstituteFilter` returning `IReadOnlyList<string>` (a student may belong to multiple institutes). In Phase 1, the list always has exactly one element -- the student's `DefaultInstituteId` from the upcasted snapshot. `GetSchoolFilter` is kept as a deprecated alias.

## Specification

Modify `src/shared/Cena.Infrastructure/Tenancy/TenantScope.cs`:

```csharp
/// <summary>
/// Returns the institute IDs the authenticated user is scoped to.
/// SUPER_ADMIN: empty list (unrestricted -- sees all).
/// Mentor: list of institutes they own (from "institutes" claim).
/// Student: list of institutes they are enrolled in.
/// Phase 1: always returns a single-element list from "school_id" claim
/// mapped to DefaultInstituteId. Returns ["cena-platform"] as fallback.
/// </summary>
public static IReadOnlyList<string> GetInstituteFilter(ClaimsPrincipal user)
{
    var role = user.FindFirstValue(ClaimTypes.Role)
             ?? user.FindFirstValue("role");
    if (role == "SUPER_ADMIN") return Array.Empty<string>();

    // Phase 1: derive from school_id claim (single institute)
    var schoolId = user.FindFirstValue("school_id");
    if (!string.IsNullOrEmpty(schoolId))
        return new[] { schoolId };

    // Fallback: platform institute
    return new[] { "cena-platform" };
}

/// <summary>
/// DEPRECATED: use GetInstituteFilter instead.
/// Kept for backward compatibility during Phase 1.
/// </summary>
[Obsolete("Use GetInstituteFilter. Will be removed in Phase 3.")]
public static string? GetSchoolFilter(ClaimsPrincipal user)
{
    // Existing implementation unchanged
    var role = user.FindFirstValue(ClaimTypes.Role)
             ?? user.FindFirstValue("role");
    if (role == "SUPER_ADMIN") return null;

    var schoolId = user.FindFirstValue("school_id");
    if (string.IsNullOrEmpty(schoolId))
        throw new UnauthorizedAccessException(
            "User has no school_id claim. Cannot determine tenant scope.");

    return schoolId;
}
```

### Caller migration (Phase 1 -- no changes to callers)

In Phase 1, NO existing callers are migrated. `GetSchoolFilter` remains the active method used by all admin services. `GetInstituteFilter` is available for new tenancy-aware code only. Existing callers will be migrated in Phase 3 (TEN-P3a) when Firebase custom claims carry real `institutes[]` arrays.

## Implementation notes

- The `[Obsolete]` attribute on `GetSchoolFilter` produces compile-time warnings but does not break the build. This is intentional -- it guides new code toward `GetInstituteFilter` without forcing a mass migration.
- `GetInstituteFilter` returns `Array.Empty<string>()` for SUPER_ADMIN (unrestricted), not `null`. Empty list means "no filter" -- callers skip the WHERE clause.
- The `"cena-platform"` fallback handles students who were onboarded before tenancy and have no `school_id` claim. This is safe because every pre-tenancy student is being upcasted to a platform enrollment (TEN-P1e).
- Follow FIND-sec-005: tenant scoping must never silently fail. If neither `school_id` nor `institutes` claim exists AND the user is not SUPER_ADMIN, the fallback to `"cena-platform"` is logged at `Warning` level.

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every code path must handle real data, real errors, real edge cases. Follow FIND-sec-005 tenant scoping pattern -- every code path that touches tenant-scoped data must go through `TenantScope`. The `Obsolete` attribute must not break any existing tests.

## Tests required

**Test class**: `TenantScopeInstituteFilterTests` in `src/api/Cena.Admin.Api.Tests/TenantScopeInstituteFilterTests.cs`

| Test method | Assertion |
|---|---|
| `SuperAdmin_ReturnsEmptyList` | User with `role=SUPER_ADMIN`, assert `GetInstituteFilter` returns empty list. |
| `StudentWithSchoolId_ReturnsSingleElement` | User with `school_id=school-123`, assert `GetInstituteFilter` returns `["school-123"]`. |
| `StudentWithNoSchoolId_ReturnsPlatformFallback` | User with `role=student` and no `school_id`, assert returns `["cena-platform"]`. |
| `GetSchoolFilter_StillWorks_ForExistingCallers` | User with `school_id=school-123`, assert `GetSchoolFilter` returns `"school-123"`. |
| `GetSchoolFilter_SuperAdmin_ReturnsNull` | Verify existing behavior unchanged. |
| `GetSchoolFilter_NoSchoolId_Throws` | Verify existing `UnauthorizedAccessException` behavior unchanged. |

**Existing test class**: `src/api/Cena.Admin.Api.Tests/TenantScopeTests.cs` -- must still pass without modification.

## Definition of Done

- [ ] `GetInstituteFilter` method added to `TenantScope.cs`
- [ ] Returns `IReadOnlyList<string>` (empty for SUPER_ADMIN, single-element for others)
- [ ] `GetSchoolFilter` marked `[Obsolete]` but NOT removed
- [ ] `GetSchoolFilter` behavior unchanged (existing tests pass)
- [ ] All 6 new tests pass
- [ ] All existing `TenantScopeTests` pass without modification
- [ ] Warning-level log on fallback to `"cena-platform"`
- [ ] `dotnet build` succeeds (obsolete warnings are expected, not errors)
- [ ] No `// TODO` or `// Phase N` comments

## Files to read first

1. `src/shared/Cena.Infrastructure/Tenancy/TenantScope.cs` -- current implementation
2. `src/api/Cena.Admin.Api.Tests/TenantScopeTests.cs` -- existing test expectations
3. `src/shared/Cena.Infrastructure/Auth/ResourceOwnershipGuard.cs` -- caller of `GetSchoolFilter`

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `src/shared/Cena.Infrastructure/Tenancy/TenantScope.cs` | modify | Add `GetInstituteFilter`, mark `GetSchoolFilter` obsolete |
| `src/api/Cena.Admin.Api.Tests/TenantScopeInstituteFilterTests.cs` | create | 6 new tests |
