# BKD-002: Admin User Management API

**Priority:** P0 — serves ADM-002 frontend
**Blocked by:** BKD-001 (auth middleware)
**Estimated effort:** 3 days
**Stack:** .NET 9 Minimal API, Marten (PostgreSQL), Firebase Admin SDK
**Frontend contract:** `tasks/admin/done/ADM-002-user-management.md`

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The admin frontend (ADM-002) calls these endpoints for user CRUD, suspension, invitation, activity logs, and session management. Users are stored in PostgreSQL via Marten. Firebase Admin SDK handles user creation and custom claims.

## Data Model

### Marten Document: `AdminUser`

```csharp
public record AdminUser
{
    public string Id { get; init; }          // Firebase UID
    public string Email { get; init; }
    public string FullName { get; init; }
    public CenaRole Role { get; init; }
    public UserStatus Status { get; init; }  // Active, Suspended, Pending
    public string? School { get; init; }
    public string? Grade { get; init; }
    public string Locale { get; init; }      // en, he, ar
    public string? AvatarUrl { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public string? SuspensionReason { get; set; }
    public DateTimeOffset? SuspendedAt { get; set; }
}
```

## Endpoints

### BKD-002.1: User List

**`GET /api/admin/users`** — Policy: `AdminOnly`

| Param | Type | Description |
|-------|------|-------------|
| q | string? | Search name or email |
| role | string? | Filter by role |
| status | string? | Filter by status |
| school | string? | Filter by school |
| grade | string? | Filter by grade |
| page | int | Page number (default 1) |
| itemsPerPage | int | Items per page (default 10) |
| sortBy | string? | Sort field |
| orderBy | string? | asc/desc |

**Response:** `{ users: AdminUser[], totalUsers: int, totalPages: int, page: int }`

**Implementation:**
- Marten `IQuerySession.Query<AdminUser>()` with `.Where()` filters
- Server-side pagination via `.Skip()` / `.Take()`
- Tenant scoping: if ADMIN (not SUPER_ADMIN), filter by `school_id` claim
- Search: ILIKE on FullName, Email

### BKD-002.2: User Detail

**`GET /api/admin/users/{id}`** — Policy: `AdminOnly`

**Response:** Full `AdminUser` document

### BKD-002.3: Create User

**`POST /api/admin/users`** — Policy: `AdminOnly`

**Request:**
```json
{ "fullName": "...", "email": "...", "role": "STUDENT", "school": "...", "grade": "9", "locale": "ar", "password": "..." }
```

**Implementation:**
1. Create Firebase user via Admin SDK: `FirebaseAuth.DefaultInstance.CreateUserAsync()`
2. Set custom claims: `FirebaseAuth.DefaultInstance.SetCustomUserClaimsAsync(uid, claims)`
3. Store `AdminUser` document in Marten
4. Return created user

### BKD-002.4: Update User

**`PUT /api/admin/users/{id}`** — Policy: `AdminOnly`

**Implementation:**
1. Update Marten document
2. If role changed: update Firebase custom claims
3. If email changed: update Firebase user email

### BKD-002.5: Delete User (Soft)

**`DELETE /api/admin/users/{id}`** — Policy: `AdminOnly`

**Implementation:**
1. Set status = Suspended, mark as soft-deleted in Marten
2. Disable Firebase user: `FirebaseAuth.DefaultInstance.UpdateUserAsync(uid, new UserRecordArgs { Disabled = true })`
3. Add to revocation list (BKD-001.5)

### BKD-002.6: Suspend / Activate

**`POST /api/admin/users/{id}/suspend`** — Policy: `AdminOnly`
- Request: `{ "reason": "Violation of ToS" }`
- Set status = Suspended, store reason and timestamp
- Disable Firebase user

**`POST /api/admin/users/{id}/activate`** — Policy: `AdminOnly`
- Set status = Active, clear suspension fields
- Re-enable Firebase user

### BKD-002.7: Invite User

**`POST /api/admin/users/invite`** — Policy: `AdminOnly`
- Request: `{ "email": "...", "role": "TEACHER", "school": "..." }`
- Create Firebase user with no password (sends sign-in link email)
- Store as Pending in Marten
- Use Firebase `generateSignInWithEmailLink()` or send custom email

**`POST /api/admin/users/bulk-invite`** — Policy: `AdminOnly`
- Accept CSV upload (multipart/form-data)
- Parse CSV: columns = name, email, role
- Create each user, collect results
- Response: `{ created: int, failed: [{ email, error }] }`

### BKD-002.8: User Activity

**`GET /api/admin/users/{id}/activity`** — Policy: `AdminOnly`
- Query Marten event stream for user-related events
- Return: `[{ timestamp, action, description, metadata }]`
- Actions: login, logout, password_change, role_change, suspension, etc.

### BKD-002.9: User Sessions

**`GET /api/admin/users/{id}/sessions`** — Policy: `AdminOnly`
- Query active sessions from Redis or session store
- Return: `[{ sessionId, device, browser, ip, location, lastActive, status }]`

**`DELETE /api/admin/users/{id}/sessions/{sid}`** — Policy: `AdminOnly`
- Revoke specific session from store

### BKD-002.10: User Stats

**`GET /api/admin/users/stats`** — Policy: `ModeratorOrAbove`
- Response: `{ totalUsers, newThisWeek, activeToday, pendingReview, byRole: { STUDENT: n, ... } }`
- Query Marten with aggregation

## Files to Create

| File | Description |
|------|-------------|
| `src/actors/Cena.Actors/Api/Admin/AdminUserEndpoints.cs` | Minimal API endpoint registration |
| `src/actors/Cena.Actors/Api/Admin/AdminUserDtos.cs` | Request/response DTOs |
| `src/actors/Cena.Actors/Api/Admin/AdminUserService.cs` | Business logic (Marten + Firebase Admin) |
| `src/actors/Cena.Actors/Infrastructure/Documents/AdminUser.cs` | Marten document model |
| `src/actors/Cena.Actors/Infrastructure/Firebase/FirebaseAdminService.cs` | Firebase Admin SDK wrapper |

## Test

- [ ] GET /api/admin/users returns paginated results
- [ ] Filters narrow results correctly (server-side)
- [ ] Create user → Firebase user created + Marten document stored
- [ ] Suspend user → Firebase disabled + status updated
- [ ] Tenant scoping: ADMIN only sees users in their school
- [ ] SUPER_ADMIN sees all users across schools
- [ ] Bulk invite CSV → users created, failures reported
- [ ] Activity log shows real events from Marten event stream
