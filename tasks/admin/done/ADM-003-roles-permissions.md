# ADM-003: Roles & Permissions Management

**Priority:** P0 — required for RBAC enforcement
**Blocked by:** ADM-001 (auth), ADM-002 (user management)
**Estimated effort:** 3 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, Vuexy Full v10.11.1, CASL

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

Cena needs fine-grained RBAC. Vuexy ships with CASL integration and `apps/roles` + `apps/permissions` pages. This task wires them to Cena's permission model. Roles define what each user type can access in the admin dashboard and the student-facing app.

## Predefined Roles

| Role | Description | Key Permissions |
|------|-------------|-----------------|
| **Super Admin** | Platform owner | Everything — user management, system config, billing |
| **Admin** | School/org administrator | User CRUD within org, content approval, analytics |
| **Moderator** | Content moderator | Review/approve/reject questions, flag content, view reports |
| **Teacher** | Classroom teacher | View student progress, assign content, view class analytics |
| **Student** | Learner | Self-service profile, view own progress (no admin access) |
| **Parent** | Guardian | View child's progress (no admin access) |

## Subtasks

### ADM-003.1: Roles List & Management

**Files to modify:**
- `src/admin/full-version/src/pages/apps/roles/index.vue` — adapt for Cena roles
- `src/admin/full-version/src/views/apps/roles/` — role components

**Acceptance:**
- [ ] Role cards showing: role name, description, user count, permission count
- [ ] Click role → detail view with full permission matrix
- [ ] Create custom role: name, description, copy permissions from existing role
- [ ] Edit role: toggle permissions on/off
- [ ] Delete custom role (predefined roles cannot be deleted)
- [ ] Assign role to user(s) from role detail view

### ADM-003.2: Permissions Matrix

**Files to modify:**
- `src/admin/full-version/src/pages/apps/permissions/index.vue` — full permission matrix

**Permission Categories:**

| Category | Actions |
|----------|---------|
| **Users** | list, view, create, edit, delete, suspend, impersonate |
| **Content** | list, view, create, edit, delete, approve, reject, publish |
| **Questions** | list, view, create, edit, delete, review, approve |
| **Analytics** | view-own, view-class, view-school, view-platform, export |
| **Focus Data** | view-own, view-class, view-aggregated, configure-alerts |
| **Mastery Data** | view-own, view-class, view-school, configure-thresholds |
| **Settings** | view, edit-own, edit-org, edit-platform |
| **System** | view-health, manage-actors, view-logs, manage-config |

**Acceptance:**
- [ ] Matrix grid: rows = permissions, columns = roles
- [ ] Checkbox toggle per cell (role × permission)
- [ ] Bulk toggle: select all in a category for a role
- [ ] Changes saved immediately (optimistic UI with rollback on error)
- [ ] Search/filter permissions by name or category
- [ ] Visual diff: highlight unsaved changes before save

### ADM-003.3: CASL Integration with Backend

**Files to modify:**
- `src/admin/full-version/src/plugins/casl/ability.ts` — map backend permissions to CASL rules
- `src/admin/full-version/src/plugins/1.router/guards.ts` — enforce per-route permissions

**Acceptance:**
- [ ] Login response includes `abilities[]` array from backend
- [ ] CASL ability instance updated on login and token refresh
- [ ] Route-level guards check CASL abilities (e.g., `/admin/users` requires `users.list`)
- [ ] Component-level: `v-if="can('create', 'Content')"` hides unauthorized UI elements
- [ ] Navigation menu items hidden based on CASL abilities
- [ ] Unauthorized API calls return 403 → show "not authorized" message

### ADM-003.4: Role Assignment in User Management

**Files to modify:**
- `src/admin/full-version/src/views/apps/user/view/` — add role assignment UI

**Acceptance:**
- [ ] Role selector dropdown in user edit view
- [ ] Confirmation dialog when changing role (shows permission diff)
- [ ] Audit log entry created on role change
- [ ] User's CASL abilities update on next login after role change
- [ ] Cannot remove last Super Admin role (safety check)

## .NET Backend Endpoints Required

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/admin/roles` | List all roles with user counts |
| GET | `/api/admin/roles/{id}` | Role detail with permissions |
| POST | `/api/admin/roles` | Create custom role |
| PUT | `/api/admin/roles/{id}` | Update role |
| DELETE | `/api/admin/roles/{id}` | Delete custom role |
| GET | `/api/admin/permissions` | List all permissions grouped by category |
| PUT | `/api/admin/roles/{id}/permissions` | Update role permissions (batch) |
| POST | `/api/admin/users/{id}/role` | Assign role to user |
| GET | `/api/admin/users/{id}/abilities` | Get CASL ability rules for user |

## Test

- [ ] Roles page shows all predefined roles with correct user counts
- [ ] Create custom role → assign permissions → assign to user → user gains access
- [ ] Remove permission from role → user loses access on next login
- [ ] CASL guards block unauthorized routes (returns to /not-authorized)
- [ ] Navigation menu hides items user cannot access
- [ ] Cannot delete predefined roles
- [ ] Cannot remove last Super Admin
- [ ] Permission changes propagate: modify role → all users with that role affected
