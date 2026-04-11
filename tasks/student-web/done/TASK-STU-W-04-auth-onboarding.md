# TASK-STU-W-04: Auth & Onboarding

**Priority**: HIGH — gates every authenticated feature
**Effort**: 3-4 days
**Phase**: 2
**Depends on**: [STU-W-02](TASK-STU-W-02-navigation-shell-auth-guard.md), [STU-W-03](TASK-STU-W-03-api-signalr-client.md)
**Backend tasks**: [STB-00](../student-backend/TASK-STB-00-me-profile-onboarding.md)
**Status**: Not Started

---

## Goal

Ship the entire cold-start experience: a student lands on `/login`, signs in (or registers, or enters as a guest), completes the 7-step onboarding wizard, and arrives on `/home` with a fully initialized profile, locale, and subject set — in under three minutes.

## Spec

Full specification in [docs/student/03-auth-onboarding.md](../../docs/student/03-auth-onboarding.md). All `STU-AUTH-001` through `STU-AUTH-016` acceptance criteria in that file form this task's checklist.

## Scope

In scope:

- `/login` page with the five auth providers: email+password, Google, Apple, Microsoft, phone (SMS)
- `/register` page with display name, email, password, age gate (COPPA path if under 13)
- `/forgot-password` page driving Firebase password reset
- `/onboarding` 7-step wizard: Welcome → Role → Language → Subjects → Goals → Diagnostic → Confirm
- Reusable components: `<StudentAuthCard>`, `<AuthProviderButtons>`, `<EmailPasswordForm>`, `<PhoneAuthFlow>`, `<OnboardingStepper>`, `<RoleSelector>`, `<OnboardingLanguagePicker>`, `<SubjectMultiSelect>`, `<GoalSettingCard>`, `<DiagnosticRunner>`, `<OnboardingSummary>`
- Pinia `onboardingStore` with localStorage persistence so a refresh mid-wizard resumes at the current step
- Pinia `meStore` that exposes `{ user, onboardedAt, role, locale, subjects }` and hydrates from `GET /api/me` on bootstrap
- Classroom code entry field on the onboarding confirm step and as a CTA on `/home` empty state; validates via `POST /api/classrooms/join`
- Guest trial mode: anonymous Firebase sign-in → 10 sample questions via existing `/api/content/questions` → conversion prompt on completion that carries diagnostic results into the permanent account if the student registers within 30 minutes
- Multi-tab detection via `BroadcastChannel` — second tab opening `/onboarding` shows "Another session is in progress" and reacts when the first completes
- Locale switch in step 3 applies font, RTL, and all in-wizard strings instantly
- Diagnostic step uses the existing `GET /api/content/questions/{id}` endpoint with `?difficulty=diagnostic`
- Onboarding completion POSTs to `/api/me/onboarding` (STB-00) and on success navigates to `returnTo` or `/home`
- `returnTo` preserved across the entire flow and sanitized to same-origin paths
- Soft lockout with exponential backoff after N failed login attempts (client-side UX; Firebase enforces the real throttle)

Out of scope:

- LMS LTI 1.3 integration — deferred to v1.x
- 2FA enrollment — covered in `/settings/account` (STU-W-14)
- Parental consent verification workflow — legal-facing separate initiative
- Custom Firebase email templates — use defaults for v1
- Social login deep-link handoff from mobile to web

## Definition of Done

- [ ] All 16 `STU-AUTH-*` acceptance criteria in [03-auth-onboarding.md](../../docs/student/03-auth-onboarding.md) pass
- [ ] Playwright E2E covers: sign-in happy path, sign-in with wrong password, phone-auth flow with stubbed SMS, registration with age gate under/over 13, forgot-password email trigger, onboarding full 7-step flow forward, onboarding back-navigation keeps state, classroom code invalid, classroom code valid, guest trial → registration conversion with diagnostic carry-over
- [ ] All auth pages pass axe-core in light + dark across en / ar / he
- [ ] `returnTo` preserved through full sign-in → onboarding → target route
- [ ] Refresh mid-wizard resumes at the current step with no data loss
- [ ] Multi-tab wizard conflict surfaces a readable message in the second tab and unblocks when the first completes
- [ ] Cross-cutting concerns from the bundle README apply

## Risks

- **COPPA wording** is legally sensitive. Confirm copy with legal before shipping under-13 registration; if unresolved, soft-disable under-13 registration and show "ask a parent to register for you" placeholder.
- **Phone auth** requires reCAPTCHA Enterprise configuration per environment. Document the setup in the PR.
- **Guest trial identity** uses Firebase anonymous auth — confirm it's enabled in the `cena-platform` project; otherwise the guest button must be hidden.
- **localStorage quota** — onboarding state includes diagnostic answers. Keep payload slim; do not store question content, only answer IDs.
- **BroadcastChannel** is not supported in Safari private browsing. Fall back to a `storage` event listener on localStorage.
- **Diagnostic → mastery seed**: if the backend can't accept diagnostic results on day one, flag them for later ingestion; don't block the wizard.
