# BKD-003: Admin Roles & Permissions API

**Priority:** P0 — serves ADM-003 frontend
**Blocked by:** BKD-001 (auth middleware)
**Estimated effort:** 2 days
**Stack:** .NET 9 Minimal API, Marten (PostgreSQL), Firebase Admin SDK
**Frontend contract:** `tasks/admin/done/ADM-003-roles-permissions.md`

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The admin frontend (ADM-003) shows role cards with user counts and a permission matrix grid. Predefined roles (Super Admin through Parent) ship with default permissions. Admins can create custom roles and toggle individual permissions. Changes propagate to Firebase custom claims on next token refresh.

## Data Model

### Marten Document: `CenaRoleDefinition`

```csharp
public record CenaRoleDefinition
{
    public string Id { get; init; }          // e.g. "SUPER_ADMIN", "custom_role_xyz"
    public string Name { get; init; }
    public string Description { get; init; }
    public bool IsPredefined { get; init; }  // true for 6 built-in roles
    public Dictionary<string, List<string>> Permissions { get; init; } // category → actions[]
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

### Permission Categories (Seed Data)

| Category | Actions |
|----------|---------|
| Users | list, view, create, edit, delete, suspend, impersonate |
| Content | list, view, create, edit, delete, approve, reject, publish |
| Questions | list, view, create, edit, delete, review, approve |
| Analytics | view-own, view-class, view-school, view-platform, export |
| Focus Data | view-own, view-class, view-aggregated, configure-alerts |
| Mastery Data | view-own, view-class, view-school, configure-thresholds |
| Settings | view, edit-own, edit-org, edit-platform |
| System | view-health, manage-actors, view-logs, manage-config |

## Endpoints

### BKD-003.1: List Roles

**`GET /api/admin/roles`** — Policy: `ModeratorOrAbove`

**Response:**
```json
[
  {
    "id": "SUPER_ADMIN",
    "name": "Super Admin",
    "description": "Platform owner — full access to everything",
    "isPredefined": true,
    "userCount": 2,
    "permissionCount": 38,
    "permissions": { "Users": ["list","view","create",...], ... }
  }
]
```

**Implementation:**
- Query all `CenaRoleDefinition` documents
- Join user counts via `IQuerySession.Query<AdminUser>().GroupBy(u => u.Role).Select(...)` or a pre-computed count

### BKD-003.2: Role Detail

**`GET /api/admin/roles/{id}`** — Policy: `ModeratorOrAbove`

### BKD-003.3: Create Custom Role

**`POST /api/admin/roles`** — Policy: `SuperAdminOnly`

**Request:**
```json
{ "name": "Content Lead", "description": "Senior moderator with publish rights", "copyFrom": "MODERATOR" }
```

**Implementation:**
- Generate ID from slugified name
- If `copyFrom` specified, clone permissions from that role
- Store in Marten

### BKD-003.4: Update Role Permissions

**`PUT /api/admin/roles/{id}/permissions`** — Policy: `SuperAdminOnly`

**Request:**
```json
{ "permissions": { "Users": ["list", "view"], "Content": ["list", "view", "create", "edit", "approve"] } }
```

**Implementation:**
- Update the `Permissions` dict on the Marten document
- Predefined roles CAN have permissions modified (but cannot be deleted)

### BKD-003.5: Delete Custom Role

**`DELETE /api/admin/roles/{id}`** — Policy: `SuperAdminOnly`

**Implementation:**
- Reject if `IsPredefined == true`
- Reject if any users are assigned to this role
- Soft-delete from Marten

### BKD-003.6: List All Permissions

**`GET /api/admin/permissions`** — Policy: `ModeratorOrAbove`

**Response:**
```json
[
  { "category": "Users", "actions": ["list", "view", "create", "edit", "delete", "suspend", "impersonate"] },
  { "category": "Content", "actions": ["list", "view", "create", "edit", "delete", "approve", "reject", "publish"] }
]
```

Static data — no DB query needed.

### BKD-003.7: Assign Role to User

**`POST /api/admin/users/{id}/role`** — Policy: `AdminOnly`

**Request:** `{ "role": "MODERATOR" }`

**Implementation:**
1. Update `AdminUser.Role` in Marten
2. Update Firebase custom claims: `FirebaseAuth.DefaultInstance.SetCustomUserClaimsAsync(uid, newClaims)`
3. Record role change event in Marten event stream
4. Safety check: cannot remove last SUPER_ADMIN

### BKD-003.8: Get User CASL Abilities

**`GET /api/admin/users/{id}/abilities`** — Policy: `AdminOnly`

**Response:** CASL-formatted ability rules
```json
[
  { "action": "manage", "subject": "all" },
  { "action": "read", "subject": "Analytics" }
]
```

**Implementation:**
- Look up user's role → role's permissions → map to CASL format
- Used by frontend to refresh abilities after role change

## Files to Create

| File | Description |
|------|-------------|
| `src/actors/Cena.Actors/Api/Admin/AdminRoleEndpoints.cs` | Minimal API endpoint registration |
| `src/actors/Cena.Actors/Api/Admin/AdminRoleDtos.cs` | Request/response DTOs |
| `src/actors/Cena.Actors/Api/Admin/AdminRoleService.cs` | Business logic |
| `src/actors/Cena.Actors/Infrastructure/Documents/CenaRoleDefinition.cs` | Marten document |
| `src/actors/Cena.Actors/Infrastructure/Seed/RoleSeedData.cs` | Predefined role + permission seed |

## Seed: Predefined Roles

On startup, upsert 6 predefined roles with default permissions if they don't exist. SUPER_ADMIN gets all permissions. Each role below gets progressively fewer.

## Test

- [ ] GET /api/admin/roles returns 6 predefined roles with correct user counts
- [ ] Create custom role with `copyFrom` → clones permissions
- [ ] Update permissions → role's permission dict changes
- [ ] Cannot delete predefined role → 400
- [ ] Cannot delete role with assigned users → 400
- [ ] Assign role to user → Firebase claims updated
- [ ] Cannot remove last SUPER_ADMIN → 400
- [ ] GET abilities returns CASL-formatted rules
