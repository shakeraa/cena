# ADM-016: Account Linking & Multi-Provider Auth

**Priority:** P2 — UX enhancement for admins
**Blocked by:** ADM-001 (auth system working)
**Estimated effort:** 2 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, Firebase Auth

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

Firebase supports multiple auth providers linked to a single account. Admins may sign in with Google one day and email/password another. They should also be able to add Apple Sign-In, phone number, or additional email addresses to their account. The admin dashboard needs a UI for managing linked providers in the user's account settings.

Firebase auto-links providers that share the same email by default. This task adds explicit UI for managing linked providers and handling edge cases.

## Subtasks

### ADM-016.1: Account Settings — Linked Providers

**Files to create/modify:**

- `src/admin/full-version/src/views/apps/account-settings/AccountSettingsSecurity.vue` — add linked providers section

**Acceptance:**

- [ ] Show currently linked providers: Email/Password, Google, Apple, Phone
- [ ] Each provider shows status: Linked (green) or Not Linked (gray)
- [ ] "Link" button for unlinked providers triggers Firebase `linkWithPopup` / `linkWithPhoneNumber`
- [ ] "Unlink" button for linked providers (must keep at least one provider)
- [ ] Google link: `linkWithPopup(new GoogleAuthProvider())`
- [ ] Apple link: `linkWithPopup(new OAuthProvider('apple.com'))`
- [ ] Phone link: phone number input + SMS verification code flow
- [ ] Email/Password link: set password if not already set

### ADM-016.2: Login Page — Multi-Provider Support

**Files to modify:**

- `src/admin/full-version/src/pages/login.vue` — already has Google, add Apple

**Acceptance:**

- [ ] "Sign in with Google" button (already implemented in ADM-001)
- [ ] "Sign in with Apple" button
- [ ] Both redirect to dashboard if user has admin role
- [ ] Error if user exists with different provider and emails don't match

### ADM-016.3: Phone Number as MFA (not primary login)

**Files to create:**

- `src/admin/full-version/src/composables/usePhoneMFA.ts`

**Acceptance:**

- [ ] Admin can enroll phone number as second factor in account settings
- [ ] SMS code sent to phone for verification during enrollment
- [ ] On next login, if MFA is enrolled, prompt for SMS code after password
- [ ] Firebase `multiFactor.enroll()` with phone provider
- [ ] Unenroll MFA option in account settings

### ADM-016.4: Provider Conflict Resolution

**Acceptance:**

- [ ] If user signs in with Google but account exists with email/password only → auto-link (same email)
- [ ] If emails don't match → show error "Account exists with different credentials"
- [ ] Provide "Link accounts" flow: sign in with existing provider first, then link new one
- [ ] Handle `auth/account-exists-with-different-credential` Firebase error gracefully

## Firebase Methods Used

| Action | Firebase Method |
| ------ | -------------- |
| Link Google | `linkWithPopup(user, new GoogleAuthProvider())` |
| Link Apple | `linkWithPopup(user, new OAuthProvider('apple.com'))` |
| Link phone | `linkWithPhoneNumber(user, phoneNumber, verifier)` |
| Link email | `linkWithCredential(user, EmailAuthProvider.credential(email, password))` |
| Unlink | `unlink(user, providerId)` |
| Enroll MFA | `multiFactor(user).enroll(phoneMultiFactorAssertion)` |
| Unenroll MFA | `multiFactor(user).unenroll(factorInfo)` |

## Test

- [ ] Sign in with Google → same UID as email/password account (auto-linked)
- [ ] Link Apple in account settings → provider appears as linked
- [ ] Unlink Google → can still sign in with email/password
- [ ] Cannot unlink last remaining provider (error shown)
- [ ] Enroll phone MFA → next login requires SMS code
- [ ] Unenroll phone MFA → login no longer requires SMS
