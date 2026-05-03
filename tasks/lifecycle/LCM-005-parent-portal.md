# LCM-005: Parent Portal — Dashboard, Freeze Controls, Time Limits

**Priority:** P2 — user value, parental oversight
**Blocked by:** LCM-001 (Actor Status Gate), LCM-004 (Plan Management — for plan display)
**Estimated effort:** 4 days
**Phase:** 5

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Context

Parent role exists (`PARENT` in `CenaRole`) with `student_ids` claim linking to children. But no parent-facing pages exist. Parents need: child activity visibility, freeze/unfreeze controls, daily time limits, study schedule, subject restrictions, and progress digests.

Parent controls are different from admin suspension: a parent freeze is immediately reversible by the parent and doesn't involve school admin. Admin suspension outranks parent freeze.

## Subtasks

### LCM-005.1: ParentControlSettings Document

**Files to create:**
- `src/shared/Cena.Infrastructure/Documents/ParentControlSettings.cs`

**Acceptance:**
- [ ] Marten document with `StudentId` as key
- [ ] Fields: `ParentId`, `IsFrozen`, `DailyTimeLimitMinutes` (null=unlimited), `AllowedStartHour` (0-23), `AllowedEndHour` (0-23), `RestrictedSubjects` (string[]), `UpdatedAt`
- [ ] Default: all null/false (no restrictions)

### LCM-005.2: Parent API Endpoints

**Files to create:**
- `src/api/Cena.Admin.Api/ParentService.cs`
- `src/api/Cena.Admin.Api/ParentEndpoints.cs`

**Endpoints:**
- `GET /api/parent/children` — list linked children with current status
- `GET /api/parent/children/{id}/activity` — last 7 days activity feed
- `GET /api/parent/children/{id}/mastery` — mastery summary
- `POST /api/parent/children/{id}/freeze` — toggle freeze
- `PUT /api/parent/children/{id}/limits` — set time/subject limits
- `GET /api/parent/children/{id}/controls` — get current control settings

**Acceptance:**
- [ ] All endpoints scoped to parent's `student_ids` claim — cannot access other children
- [ ] Freeze publishes `cena.account.status_changed` with `frozen` status
- [ ] Unfreeze publishes `cena.account.status_changed` with `active`
- [ ] Time limits stored in Marten + cached in Redis `parent_controls:{studentId}` (5-min TTL)
- [ ] Activity feed sourced from Marten events (last 7 days of student's stream)

### LCM-005.3: Actor — Parent Controls Enforcement

**Files to modify:**
- `src/actors/Cena.Actors/Students/StudentState.cs` — add parent control fields
- `src/actors/Cena.Actors/Students/StudentActor.Commands.cs` — enforce limits
- `src/actors/Cena.Actors/Bus/NatsBusRouter.cs` — subscribe to `cena.parent.controls_updated`

**Acceptance:**
- [ ] `StudentState`: add `ParentFrozen`, `DailyTimeLimitMinutes`, `TodaySessionMinutes`
- [ ] On `StartSession`: check if parent frozen → reject with `PARENT_FROZEN` error
- [ ] On `StartSession`: check time limit → reject if `TodaySessionMinutes >= DailyTimeLimitMinutes`
- [ ] On `StartSession`: check study schedule (allowed hours) → reject outside window
- [ ] Session actor tracks elapsed minutes, auto-ends when daily limit reached
- [ ] NATS event `cena.parent.controls_updated` updates actor state in-memory
- [ ] Redis key `parent_controls:{studentId}` checked by NatsBusRouter for frozen status

### LCM-005.4: Admin Dashboard — Parent Portal Pages

**Files to create:**
- `src/admin/full-version/src/pages/apps/parent/index.vue` — children list
- `src/admin/full-version/src/pages/apps/parent/child/[id].vue` — child detail
- `src/admin/full-version/src/views/apps/parent/ChildActivityFeed.vue`
- `src/admin/full-version/src/views/apps/parent/ChildControlsPanel.vue`
- `src/admin/full-version/src/views/apps/parent/ChildMasteryOverview.vue`

**Acceptance:**
- [ ] Children list: cards with name, school, last active, current session status, mastery percentage
- [ ] Activity feed: timeline of sessions, attempts, mastery milestones (last 7 days)
- [ ] Controls panel: freeze toggle, time limit slider (30-240 min), schedule picker, subject toggles
- [ ] Mastery overview: concept map with color-coded mastery levels
- [ ] Responsive design matching Vuexy theme
- [ ] Parent-only navigation item in sidebar (hidden for non-parent roles)

### LCM-005.5: Admin View of Parent Controls

**Files to modify:**
- `src/admin/full-version/src/views/apps/user/UserTabSecurity.vue` — show parent controls info

**Acceptance:**
- [ ] Admin can see "Parent Controls" section on student's security tab
- [ ] Shows: frozen status, time limits, schedule, restricted subjects, parent name
- [ ] Admin cannot modify parent controls (except SUPER_ADMIN)
- [ ] "Parent-controlled" badge on student cards in user list

### LCM-005.6: Flutter — Parent Freeze UX

**Files to create:**
- Flutter: `lib/features/session/parent_frozen_screen.dart`

**Acceptance:**
- [ ] When frozen: friendly screen "Your parent paused your learning. Talk to them!"
- [ ] When time limit reached: "You've studied for X minutes today. Great job! Come back tomorrow"
- [ ] When outside schedule: "Study hours are {start}-{end}. See you then!"
- [ ] No bypass — app enforces limits locally + server-side

### LCM-005.7: Weekly Digest Email

**Files to create:**
- `src/actors/Cena.Actors.Host/Jobs/ParentDigestJob.cs`

**Acceptance:**
- [ ] Runs weekly (Sunday 09:00 local time per parent's locale)
- [ ] For each parent: aggregate child's week — sessions, questions, mastery gains, streaks
- [ ] Send via email service (integration point — actual email provider TBD)
- [ ] Include: "Your child studied X minutes this week, mastered Y concepts"
- [ ] Opt-out via parent settings

### LCM-005.8: Tests

**Acceptance:**
- [ ] Test: parent can only access linked children
- [ ] Test: freeze blocks student sessions via actor and router
- [ ] Test: time limit auto-ends session
- [ ] Test: study schedule rejects outside-hours sessions
- [ ] Test: admin cannot override parent controls (unless SUPER_ADMIN)
- [ ] Test: unfreeze restores student access
