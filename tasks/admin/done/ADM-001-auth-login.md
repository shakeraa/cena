# ADM-001: Authentication & Login System (Firebase Auth)

**Priority:** P0 — gates all other admin features
**Blocked by:** None (uses Vuexy built-in auth scaffolding)
**Estimated effort:** 3 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, Vuexy Full v10.11.1
**Contract:** `contracts/backend/firebase-auth.md`

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

Admin/moderator dashboard authenticates via **Firebase Authentication** (Google Identity Platform). Vuexy ships with auth pages (login v1/v2, forgot password, 2FA) and CASL-based ACL guards. This task wires them to Firebase Auth SDK on the client and Firebase JWT validation on the .NET backend.

**Roles with admin access:** `MODERATOR`, `ADMIN`, `SUPER_ADMIN` (per `contracts/backend/firebase-auth.md`).
Students, parents, and teachers use the student-facing app — not this dashboard.

## Subtasks

### ADM-001.1: Login Page Configuration

**Files to modify:**
- `src/admin/full-version/src/pages/login.vue` — primary login entry
- `src/admin/full-version/src/pages/pages/authentication/login-v2.vue` — styled login page
- `src/admin/full-version/themeConfig.ts` — branding (Cena logo, title, colors)

**Acceptance:**
- [ ] Login page shows Cena branding (logo, colors, Arabic RTL support)
- [ ] Email + password fields with validation
- [ ] "Remember me" checkbox
- [ ] "Forgot password" link routes to forgot-password page
- [ ] Login calls `POST /api/auth/login` on .NET backend
- [ ] Successful login stores JWT token + user data in cookies
- [ ] Failed login shows error message (invalid credentials, account locked, etc.)
- [ ] Redirect to dashboard after successful login

### ADM-001.2: API Client Configuration

**Files to modify:**
- `src/admin/full-version/src/utils/api.ts` — point to .NET backend base URL
- `src/admin/full-version/src/composables/useApi.ts` — configure auth header injection
- `src/admin/full-version/.env` — `VITE_API_BASE_URL=http://localhost:5000/api`

**Acceptance:**
- [ ] `VITE_API_BASE_URL` points to .NET backend
- [ ] All API calls auto-inject `Authorization: Bearer {token}` header
- [ ] 401 responses trigger redirect to login page
- [ ] Token refresh flow handles expired tokens gracefully

### ADM-001.3: Auth Guards & Route Protection

**Files to modify:**
- `src/admin/full-version/src/plugins/1.router/guards.ts` — wire to real token validation
- `src/admin/full-version/src/plugins/casl/ability.ts` — define admin/moderator abilities

**Acceptance:**
- [ ] All routes except login/forgot-password require authentication
- [ ] Token presence + validity checked on every route navigation
- [ ] CASL abilities loaded from user role returned by backend
- [ ] Unauthorized access redirects to `/not-authorized`

### ADM-001.4: Forgot Password & Reset Flow

**Files to modify:**
- `src/admin/full-version/src/pages/pages/authentication/forgot-password-v2.vue`
- `src/admin/full-version/src/pages/pages/authentication/reset-password-v2.vue`

**Acceptance:**
- [ ] Forgot password sends email via `POST /api/auth/forgot-password`
- [ ] Reset password page accepts token from email link
- [ ] Password reset calls `POST /api/auth/reset-password` with token + new password
- [ ] Success redirects to login with confirmation message

### ADM-001.5: Two-Factor Authentication (Optional)

**Files to modify:**
- `src/admin/full-version/src/pages/pages/authentication/two-steps-v2.vue`
- `src/admin/full-version/src/components/dialogs/TwoFactorAuthDialog.vue`

**Acceptance:**
- [ ] 2FA setup via authenticator app (TOTP)
- [ ] Login flow prompts for 2FA code when enabled
- [ ] Backup codes generation and display

## .NET Backend Endpoints Required

| Method | Endpoint | Request | Response |
|--------|----------|---------|----------|
| POST | `/api/auth/login` | `{ email, password }` | `{ token, refreshToken, user, abilities }` |
| POST | `/api/auth/refresh` | `{ refreshToken }` | `{ token, refreshToken }` |
| POST | `/api/auth/forgot-password` | `{ email }` | `{ message }` |
| POST | `/api/auth/reset-password` | `{ token, newPassword }` | `{ message }` |
| POST | `/api/auth/verify-2fa` | `{ code }` | `{ token }` |
| GET | `/api/auth/me` | — | `{ user, abilities }` |

## Test

- [ ] Login with valid credentials → dashboard
- [ ] Login with invalid credentials → error message
- [ ] Access protected route without token → redirect to login
- [ ] Token expiry → automatic refresh or redirect to login
- [ ] Forgot password email → reset link → new password works
- [ ] Arabic RTL layout renders correctly on all auth pages
