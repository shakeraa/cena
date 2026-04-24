# TASK-STB-00: `/api/me/*` — Bootstrap, Profile, Settings, Onboarding

**Priority**: HIGH — blocks every student UI feature task
**Effort**: 3-4 days
**Depends on**: [DB-05](../../docs/tasks/infra-db-migration/TASK-DB-05-contracts-library.md), [DB-06](../../docs/tasks/infra-db-migration/TASK-DB-06-split-hosts.md)
**UI consumers**: [STU-W-04](../student-web/TASK-STU-W-04-auth-onboarding.md), [STU-W-05](../student-web/TASK-STU-W-05-home-dashboard.md), [STU-W-14](../student-web/TASK-STU-W-14-notifications-profile-settings.md)
**Status**: Not Started

---

## Goal

Implement the student self-service endpoint group that the web app calls on every bootstrap and that the mobile app will also consume. This is the "who am I, what are my preferences, onboard me, let me edit my profile" surface.

## Endpoints

| Method | Path | Purpose | Rate limit | Auth |
|---|---|---|---|---|
| `GET` | `/api/me` | Bootstrap payload: identity, role, onboarded flag, locale, subjects | `api` (120/min) | JWT |
| `GET` | `/api/me/profile` | Public-safe profile (display name, avatar, level, streak, badges showcase) | `api` | JWT |
| `PATCH` | `/api/me/profile` | Update display name, avatar, bio, favorite subjects, visibility | `api` (stricter: 20/min) | JWT |
| `GET` | `/api/me/settings` | All preferences (appearance, notifications, privacy, learning, home layout) | `api` | JWT |
| `PATCH` | `/api/me/settings` | Update a subset of preferences (JSON merge patch) | `api` (20/min) | JWT |
| `POST` | `/api/me/onboarding` | Submit onboarding wizard result (role, locale, subjects, goals, diagnostic) | `api` (5/min, idempotent) | JWT |
| `POST` | `/api/classrooms/join` | Join a class via 6-digit code | `api` (10/min) | JWT |
| `PUT` | `/api/me/preferences/home-layout` | Save widget order + visibility | `api` | JWT |
| `GET` | `/api/me/devices` | List active sessions across web + mobile | `api` | JWT |
| `POST` | `/api/me/devices/{id}/revoke` | Revoke a device session | `api` (10/min) | JWT |
| `POST` | `/api/me/share-tokens` | Create a scoped view-only token for a parent or tutor | `api` (5/min) | JWT |

## Data Access

- **Reads**:
  - `StudentProfileSnapshot` (Marten doc — already exists)
  - `StudentPreferencesDocument` (new Marten doc)
  - `ClassroomEnrollmentDocument` (new)
  - `DeviceSessionDocument` (new)
  - `ShareTokenDocument` (new)
- **Writes**:
  - Append `OnboardingCompleted_V1` event to the student stream
  - Append `PreferencesUpdated_V1` event
  - Append `ProfileUpdated_V1` event
  - Append `ClassroomJoined_V1` event
  - Append `DeviceRevoked_V1` event
- **Projections**: document types are updated inline by Marten projections; all reads stay within the 5 s `cena_student` statement timeout because the documents are indexed by student ID.

## Hub Events

None for this task — settings changes are not pushed in realtime. If two devices have the same student logged in they will see the update on their next fetch. A future task may add `PreferencesChanged` for true multi-device sync.

## Contracts

Add to `Cena.Api.Contracts/Dtos/Me/`:

- `MeBootstrapDto` — `{ studentId, displayName, role, locale, onboardedAt, subjects[], level, streakDays, avatarUrl }`
- `ProfileDto` — full profile view
- `ProfilePatchDto` — partial update
- `SettingsDto` — nested: `{ appearance, notifications, privacy, learning, homeLayout }`
- `SettingsPatchDto` — JSON merge patch shape
- `OnboardingRequest` — mirrors the wizard state from [03-auth-onboarding.md](../../docs/student/03-auth-onboarding.md)
- `OnboardingResponse` — `{ success, redirectTo }`
- `ClassroomJoinRequest` — `{ code }`
- `ClassroomJoinResponse` — `{ classroomId, classroomName, teacherName }`
- `DeviceDto` — `{ id, platform, firstSeen, lastSeen, location, current }`
- `ShareTokenRequest` — `{ audience: 'parent' | 'tutor', expiresAt, scopes[] }`
- `ShareTokenResponse` — `{ token, url, expiresAt }`

All DTOs are records, camelCase serialization, shared with mobile.

## Auth & Authorization

- Every endpoint calls `ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId)` where `studentId` comes from claims, not the URL — students cannot ask for anyone else's `/api/me`.
- Share token creation logs to `StudentRecordAccessLog` for FERPA compliance.

## Cross-Cutting Concerns

- Rate limits applied per-endpoint as noted.
- Every query fits within the 5 s `cena_student` statement timeout.
- Idempotency keys accepted on `POST /api/me/onboarding` (prevents double-enrollment on retry).
- Correlation IDs logged on every request.
- Sentry tags include `endpoint=me.*` so errors group cleanly.
- Mobile contract review: confirm the mobile lead can consume all new DTOs without breaking existing code.

## Definition of Done

- [ ] All 11 endpoints implemented and registered in `Cena.Student.Api.Host`
- [ ] DTOs land in `Cena.Api.Contracts/Dtos/Me/`
- [ ] Marten document types and projections registered in `MartenConfiguration`
- [ ] New event types registered with upcaster stubs
- [ ] Unit tests on each handler covering happy path + unauthorized + bad request
- [ ] Integration tests hit a real Marten store via test container
- [ ] OpenAPI spec updated and committed
- [ ] TypeScript types regenerated via the DB-05 codegen
- [ ] Mobile lead has reviewed DTO shapes
- [ ] All endpoints visible in Swagger UI on dev
- [ ] Rate limit policies assigned
- [ ] Every response includes `ETag` where applicable
- [ ] Share token endpoint logs to `StudentRecordAccessLog`
- [ ] PR references the `STU-W-*` tasks that consume this work

## Out of Scope

- Email / SMS / push notification delivery (STB-07)
- 2FA setup flow (future task)
- Account delete / data export execution (existing GDPR endpoints)
- Parent / tutor dashboard pages (out of scope for student app entirely)
