# 03 — Auth & Onboarding

## Overview

First impression features. A student opens the web app for the first time and must get from "cold start" to "first question answered" in under 3 minutes.

## Mobile Parity

Source of truth:
- [src/mobile/lib/features/auth/auth_screen.dart](../../src/mobile/lib/features/auth/auth_screen.dart)
- [src/mobile/lib/features/auth/try_question_screen.dart](../../src/mobile/lib/features/auth/try_question_screen.dart)
- [src/mobile/lib/features/onboarding/onboarding_screen.dart](../../src/mobile/lib/features/onboarding/onboarding_screen.dart)
- [src/mobile/lib/features/onboarding/onboarding_state.dart](../../src/mobile/lib/features/onboarding/onboarding_state.dart)
- [src/mobile/lib/features/onboarding/widgets/role_selector.dart](../../src/mobile/lib/features/onboarding/widgets/role_selector.dart)
- [src/mobile/lib/features/onboarding/widgets/goal_setting_card.dart](../../src/mobile/lib/features/onboarding/widgets/goal_setting_card.dart)

## Pages

### `/login`

Layout: `auth` (centered card + hero illustration).

Providers (using Firebase Auth web SDK):

| Provider | Why |
|----------|-----|
| Email + password | Primary, schools often block OAuth |
| Google | Most common for consumers |
| Apple | Required where iOS is present |
| Microsoft | Schools on M365 |
| Phone (SMS) | Fallback for regions without email |

Components:
- `<StudentAuthCard>` — wraps the form with hero image
- `<AuthProviderButtons>` — renders enabled providers
- `<EmailPasswordForm>` — email + password + reCAPTCHA
- `<PhoneAuthFlow>` — phone input → SMS code → verify

Behavior:
- On success → fetch `/api/me` (see backend integration doc).
- If first login → navigate to `/onboarding`.
- If returning → navigate to `returnTo` query param or `/home`.
- Failed login → inline error message, no toast.
- Too many attempts → soft lockout with exponential backoff, message shown inline.

### `/register`

Same layout, prompts for email + password + display name + age gate (COPPA check: if under 13, require parental consent flow).

### `/forgot-password`

Vuexy-style "check your email" card → Firebase password reset email.

### `/onboarding`

Full-screen multi-step wizard, layout: `blank`, cannot be skipped on first launch.

Steps:

1. **Welcome** — one-screen hero, single CTA "Let's get started".
2. **Role** — `student` / `self-learner` / `test-prep` / `homeschool` (matches mobile role selector). Determines default subject selection and navigation emphasis.
3. **Language** — English / Arabic / Hebrew (Hebrew hidden outside Israel unless admin-enabled). Confirms `Intl` locale + font + RTL.
4. **Subjects** — multi-select from `/api/content/subjects`. At least one required.
5. **Goals** — daily time goal (5, 10, 15, 30, 45, 60 min) + target subjects for the week (mirrors mobile `goal_setting_card.dart`).
6. **Diagnostic (optional)** — 5-question quick assessment to seed mastery. Can be skipped.
7. **Confirm** — summary + "Go to home" CTA.

State: persisted to a Pinia `onboardingStore` and flushed to backend on completion.

Each step has a back button and a progress bar at the top (`<OnboardingStepper>`).

## Web-Specific Enhancements

- **Classroom code entry** — a student can enter a 6-digit code printed on the teacher's whiteboard to auto-join a class, auto-set subjects, and apply teacher overrides (mobile has no analogue yet).
- **Parental gate (COPPA / GDPR-K)** — if under 13, require parental email + consent checkbox before account activation.
- **LMS SSO placeholder** — prepare hook points for LTI 1.3 so schools can deep-link from Moodle / Canvas / Schoology (deferred implementation, but contract slot reserved).
- **"Continue as guest"** — 10-question trial session without an account, results discarded unless the student registers within 30 minutes (matches mobile `try_question_screen.dart` behavior).
- **Multi-tab warning** — if the student opens onboarding in two tabs, the second shows "Another session is in progress" and syncs when the first completes.

## Data Model

```ts
interface OnboardingState {
  step: 'welcome' | 'role' | 'language' | 'subjects' | 'goals' | 'diagnostic' | 'confirm'
  role: StudentRole | null
  locale: 'en' | 'ar' | 'he'
  subjects: string[]
  dailyTimeGoalMinutes: number
  weeklySubjectTargets: Array<{ subject: string, accuracyTarget: number }>
  diagnosticResults: DiagnosticResult[] | null
  classroomCode?: string
  completedAt: string | null
}
```

Backend endpoint: `POST /api/me/onboarding` with the payload above.

## Acceptance Criteria

- [ ] `STU-AUTH-001` — Login page with all 5 providers (email, Google, Apple, Microsoft, phone).
- [ ] `STU-AUTH-002` — Firebase ID token is attached to every `$api` request and SignalR handshake.
- [ ] `STU-AUTH-003` — `/register` enforces age gate; under-13 users enter parental consent flow.
- [ ] `STU-AUTH-004` — Forgot password sends Firebase reset email and returns to `/login` with toast.
- [ ] `STU-AUTH-005` — First-login users are automatically redirected to `/onboarding`.
- [ ] `STU-AUTH-006` — Onboarding 7-step wizard is implemented with back/forward, progress bar, and state persistence.
- [ ] `STU-AUTH-007` — Role selector values match mobile enum.
- [ ] `STU-AUTH-008` — Language selector persists to user profile and switches UI instantly including RTL.
- [ ] `STU-AUTH-009` — Subject multi-select fetches from `/api/content/subjects` and requires ≥ 1 selection.
- [ ] `STU-AUTH-010` — Daily time goal and weekly subject targets match mobile's `goal_setting_card` options (5/10/15/30/45/60 min).
- [ ] `STU-AUTH-011` — Diagnostic assessment is skippable; if completed, seeds mastery via backend.
- [ ] `STU-AUTH-012` — Onboarding payload is POSTed to `/api/me/onboarding` and locally cleared on success.
- [ ] `STU-AUTH-013` — Classroom code entry field on onboarding and on home CTA; code is validated via `/api/classrooms/join`.
- [ ] `STU-AUTH-014` — "Continue as guest" flow serves 10 sample questions without requiring auth and prompts registration at the end.
- [ ] `STU-AUTH-015` — Multi-tab onboarding shows a "another session in progress" message in the second tab.
- [ ] `STU-AUTH-016` — All auth pages pass axe-core accessibility audit in CI.

## Backend Dependencies

- `POST /api/me/onboarding` — new, may need backend work
- `POST /api/classrooms/join` — new, may need backend work
- `GET /api/content/subjects` — exists (ContentEndpoints.cs)
- `GET /api/me` — new, needed to bootstrap the client with the student profile after login
