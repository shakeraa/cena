# TASK-TEN-P3a: Firebase Custom Claims per Institute

**Phase**: 3
**Priority**: high
**Effort**: 2--3d
**Depends on**: Phase 2 (TEN-P2f)
**Blocks**: TEN-P3d, TEN-P3f
**Queue ID**: `t_c8ef4f5d3652`
**Assignee**: unassigned
**Status**: blocked

---

## Goal

Wire Firebase custom claims to carry per-institute role information. Replace the single `school_id` claim with an `institutes[]` array claim. Update `TenantScope.GetInstituteFilter` to read from real claims instead of the Phase 1 single-element fallback. Conduct a security review of the entire claim-based auth surface.

## Background

Phase 1 introduced `GetInstituteFilter` returning a single-element list derived from `school_id`. Phase 3 replaces this with real Firebase custom claims that carry an `institutes[]` array where each entry specifies the institute ID and the user's role within it. This is the security foundation for the mentor dashboard (P3b) and all institute-scoped operations.

## Specification

### Firebase custom claims shape

```json
{
  "role": "mentor",
  "institutes": [
    { "instituteId": "inst-001", "role": "owner" },
    { "instituteId": "inst-002", "role": "instructor" }
  ]
}
```

For students:

```json
{
  "role": "student",
  "institutes": [
    { "instituteId": "cena-platform", "role": "student" },
    { "instituteId": "inst-003", "role": "student" }
  ]
}
```

### IFirebaseAdminService extension

Add to the existing Firebase admin service interface:

```csharp
public interface IFirebaseAdminService
{
    // Existing methods...

    /// <summary>
    /// Sets the institutes[] custom claim array on a Firebase user.
    /// Merges with existing claims (does not overwrite role or other claims).
    /// </summary>
    Task SetUserInstituteRolesAsync(
        string uid,
        IReadOnlyList<InstituteRoleClaim> instituteClaims);
}

public record InstituteRoleClaim(string InstituteId, string Role);
```

### TenantScope update

Replace the Phase 1 fallback in `GetInstituteFilter`:

```csharp
public static IReadOnlyList<string> GetInstituteFilter(ClaimsPrincipal user)
{
    var role = user.FindFirstValue(ClaimTypes.Role)
             ?? user.FindFirstValue("role");
    if (role == "SUPER_ADMIN") return Array.Empty<string>();

    // Phase 3: read from institutes[] claim
    var institutesClaim = user.FindFirstValue("institutes");
    if (!string.IsNullOrEmpty(institutesClaim))
    {
        var institutes = JsonSerializer.Deserialize<InstituteRoleClaim[]>(institutesClaim);
        return institutes?.Select(i => i.InstituteId).ToArray()
               ?? Array.Empty<string>();
    }

    // Fallback: Phase 1 school_id claim
    var schoolId = user.FindFirstValue("school_id");
    if (!string.IsNullOrEmpty(schoolId))
        return new[] { schoolId };

    return new[] { "cena-platform" };
}
```

### ClaimsTransformer update

Update `src/api/Cena.Admin.Api/ClaimsTransformer.cs` (or equivalent) to parse the `institutes[]` claim from the Firebase JWT and expose it as standard claims that the authorization middleware can evaluate.

### Security review checklist

- [ ] Firebase Admin SDK call to set custom claims is server-side only (never client-callable)
- [ ] Custom claims total size does not exceed Firebase's 1000-byte limit
- [ ] `institutes[]` claim is set atomically (read-modify-write with Firebase Admin SDK)
- [ ] Token refresh is forced after claim changes (client must re-authenticate)
- [ ] No claim elevation: a user cannot add themselves to an institute they don't belong to
- [ ] SUPER_ADMIN bypass is explicitly tested and documented

## Implementation notes

- Firebase custom claims have a 1000-byte limit. With the `institutes[]` array, each entry is ~60 bytes. This supports ~15 institute memberships per user, which is sufficient for the Israeli market (a student rarely belongs to more than 3-4 institutes).
- Follow the existing `FirebaseClaimsSeeder` pattern in `src/shared/Cena.Infrastructure/Seed/FirebaseClaimsSeeder.cs` for setting claims during development.
- The `ClaimsTransformer` must handle both old-format claims (`school_id` only) and new-format claims (`institutes[]`) during the migration period.
- Follow FIND-sec-005: every admin query must use `GetInstituteFilter`, not direct claim reads.

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every code path must handle real data, real errors, real edge cases. This is a security-critical task -- the claim structure determines who can see what data. Follow FIND-sec-005 tenant scoping. The security review checklist above must be completed as part of the PR.

## Tests required

**Test class**: `FirebaseInstituteClaimsTests` in `src/api/Cena.Admin.Api.Tests/FirebaseInstituteClaimsTests.cs`

| Test method | Assertion |
|---|---|
| `SetUserInstituteRoles_SetsClaimsOnFirebaseUser` | Mock Firebase Admin SDK, call `SetUserInstituteRolesAsync`, verify claims payload. |
| `SetUserInstituteRoles_MergesWithExistingClaims` | User has `role=mentor`, add institutes, verify `role` claim preserved. |
| `GetInstituteFilter_ReadsFromInstitutesClaim` | User with `institutes` claim, assert `GetInstituteFilter` returns correct list. |
| `GetInstituteFilter_FallsBackToSchoolId` | User with only `school_id` claim, assert fallback works. |
| `GetInstituteFilter_SuperAdmin_ReturnsEmpty` | SUPER_ADMIN, assert empty list. |
| `ClaimsTransformer_ParsesInstitutesClaim` | JWT with `institutes[]`, assert claims are accessible via `ClaimsPrincipal`. |
| `ClaimsSizeLimit_Under1000Bytes` | Create claims with 15 institutes, assert serialized size < 1000 bytes. |
| `NoClaimElevation_CannotSelfAssign` | Attempt to call `SetUserInstituteRolesAsync` without admin auth, assert rejected. |

## Definition of Done

- [ ] `IFirebaseAdminService.SetUserInstituteRolesAsync` implemented
- [ ] `TenantScope.GetInstituteFilter` reads from `institutes[]` claim
- [ ] Fallback to `school_id` claim preserved for migration
- [ ] `ClaimsTransformer` updated to parse `institutes[]`
- [ ] Security review checklist completed (all items checked)
- [ ] Claims size validated under 1000-byte Firebase limit
- [ ] All 8 tests pass
- [ ] Existing `TenantScopeTests` and `ClaimsTransformerTests` still pass
- [ ] `dotnet build` succeeds with zero warnings
- [ ] No `// TODO` or `// Phase N` comments

## Files to read first

1. `src/shared/Cena.Infrastructure/Tenancy/TenantScope.cs` -- current implementation
2. `src/shared/Cena.Infrastructure/Seed/FirebaseClaimsSeeder.cs` -- claim-setting pattern
3. `src/api/Cena.Admin.Api.Tests/ClaimsTransformerTests.cs` -- existing claim tests
4. `src/shared/Cena.Infrastructure/Auth/ResourceOwnershipGuard.cs` -- claim consumer

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `src/shared/Cena.Infrastructure/Tenancy/TenantScope.cs` | modify | Read `institutes[]` claim |
| `src/shared/Cena.Infrastructure/Auth/IFirebaseAdminService.cs` | modify | Add `SetUserInstituteRolesAsync` |
| `src/shared/Cena.Infrastructure/Auth/InstituteRoleClaim.cs` | create | Claim record type |
| `src/api/Cena.Admin.Api/ClaimsTransformer.cs` | modify | Parse `institutes[]` |
| `src/shared/Cena.Infrastructure/Seed/FirebaseClaimsSeeder.cs` | modify | Seed institute claims for demo users |
| `src/api/Cena.Admin.Api.Tests/FirebaseInstituteClaimsTests.cs` | create | 8 security tests |
