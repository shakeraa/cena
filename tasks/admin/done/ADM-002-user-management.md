# ADM-002: User Management

**Priority:** P0 — core admin functionality
**Blocked by:** ADM-001 (auth must work first)
**Estimated effort:** 4 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, Vuexy Full v10.11.1

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

Cena has multiple user types: students, teachers, parents, admins, moderators. The admin dashboard needs full CRUD for user management, with filtering, search, bulk actions, and detail views. Vuexy ships with `apps/user/list` and `apps/user/view` pages to adapt.

## Subtasks

### ADM-002.1: User List Page

**Files to modify:**
- `src/admin/full-version/src/pages/apps/user/list/index.vue` — adapt for Cena user types
- `src/admin/full-version/src/views/apps/user/list/` — list view components

**Acceptance:**
- [ ] Data table shows: name, email, role, status (active/suspended/pending), school, grade, created date
- [ ] Server-side pagination (not client-side)
- [ ] Filters: role (student/teacher/parent/admin/moderator), status, school, grade level
- [ ] Search by name or email
- [ ] Bulk actions: activate, suspend, delete, export CSV
- [ ] Quick-action buttons per row: view, edit, suspend/activate, impersonate
- [ ] Role badge colors: admin (red), moderator (orange), teacher (blue), student (green), parent (purple)
- [ ] Responsive — works on tablet/mobile

### ADM-002.2: User Detail/Edit Page

**Files to modify:**
- `src/admin/full-version/src/pages/apps/user/view/[id].vue` — adapt for Cena user profile
- `src/admin/full-version/src/views/apps/user/view/` — detail view components

**Acceptance:**
- [ ] Profile header: avatar, name, email, role, status, member since
- [ ] Tabs: Overview, Security, Activity, Sessions
- [ ] **Overview tab:** personal info, school, grade, language preference, timezone
- [ ] **Security tab:** change password, force password reset, enable/disable 2FA, API keys
- [ ] **Activity tab:** login history, recent actions (content reviews, role changes, etc.)
- [ ] **Sessions tab:** active sessions with device info, ability to revoke
- [ ] Edit mode: inline editing of user fields
- [ ] Admin can change user role (with confirmation dialog)
- [ ] Admin can suspend/unsuspend with reason field

### ADM-002.3: Create User / Invite Flow

**Files to create:**
- `src/admin/full-version/src/pages/apps/user/add/index.vue` — new user form

**Acceptance:**
- [ ] Form: name, email, role, school, grade (conditional on role)
- [ ] Two modes: "Create" (set password) or "Invite" (send email invitation)
- [ ] Invite sends email with signup link pre-filled with role + school
- [ ] Bulk invite via CSV upload (name, email, role columns)
- [ ] Validation: email uniqueness, required fields, role-specific fields
- [ ] Success → redirect to user list with confirmation

### ADM-002.4: User Statistics Dashboard Widget

**Files to create:**
- `src/admin/full-version/src/views/admin/widgets/UserStatsWidget.vue`

**Acceptance:**
- [ ] Card showing: total users, new this week, active today, by role breakdown
- [ ] Mini chart: user growth over last 30 days
- [ ] Links to filtered user list (click "Teachers: 45" → user list filtered to teachers)

## .NET Backend Endpoints Required

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/admin/users` | List users (paginated, filterable, searchable) |
| GET | `/api/admin/users/{id}` | User detail |
| POST | `/api/admin/users` | Create user |
| PUT | `/api/admin/users/{id}` | Update user |
| DELETE | `/api/admin/users/{id}` | Delete user (soft delete) |
| POST | `/api/admin/users/{id}/suspend` | Suspend with reason |
| POST | `/api/admin/users/{id}/activate` | Reactivate |
| POST | `/api/admin/users/invite` | Send invite email |
| POST | `/api/admin/users/bulk-invite` | CSV bulk invite |
| GET | `/api/admin/users/{id}/activity` | User activity log |
| GET | `/api/admin/users/{id}/sessions` | Active sessions |
| DELETE | `/api/admin/users/{id}/sessions/{sid}` | Revoke session |
| GET | `/api/admin/users/stats` | User statistics summary |

## Test

- [ ] List page loads with real paginated data from backend
- [ ] Filters narrow results correctly (server-side)
- [ ] Create user → appears in list
- [ ] Edit user → changes persist
- [ ] Suspend user → status changes, user cannot login
- [ ] Bulk invite via CSV → emails sent, users appear as "pending"
- [ ] Activity tab shows real login/action history
- [ ] Tenant scoping: admin only sees users in their organization
