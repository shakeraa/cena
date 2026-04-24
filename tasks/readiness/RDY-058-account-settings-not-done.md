# RDY-058: Account Settings + Security page is a stub (ADM-016 claims-done)

- **Status**: Lying-label flagged 2026-04-18 — page is literally placeholder text
- **Priority**: High — user-facing surface that claims done but shows "coming soon"
- **Source**: Shaker screenshot 2026-04-18 while walking the admin shell
- **Depends on**: Firebase Auth emulator (RDY-056 Phase 2 — done)
- **Effort**: 3-5 days to actually build

## The misrepresentation

- [tasks/admin/done/ADM-016-account-linking.md](../admin/done/ADM-016-account-linking.md)
  is filed under `done/` and claims the Account Linking + Multi-Provider Auth
  feature shipped.
- The referenced view file `src/admin/full-version/src/views/apps/account-settings/AccountSettingsSecurity.vue`
  **does not exist**.
- The actual page at `src/admin/full-version/src/pages/pages/account-settings/[tab].vue`
  is a ten-line placeholder that renders:
  - Account tab: `<VCardText>Account settings coming soon.</VCardText>`
  - Security tab: `<VCardText>Security settings coming soon.</VCardText>`
- No linked-provider management, no MFA enrolment, no email-change flow, no
  password-change flow, no sign-out-everywhere button, no account-delete flow.

This is the exact pattern banned on 2026-04-11 (no stubs — production grade) and
2026-04-13 (labels must match data). ADM-016's done/ placement is the bug; the
task is not actually done.

## Scope to make it real

### Account tab (`AccountSettingsAccount.vue` — new)

- Profile card: display-name edit (Firebase `updateProfile`), email display
  (read-only; changes go through verified-email flow), photo upload (stretch —
  CSAM-gated per RDY-001).
- Locale selector: overrides user's default language (writes `lang` custom claim).
- Timezone selector: writes to user profile in Marten.
- "Delete my account" button: triggers GDPR Article 17 erasure flow
  (`/api/admin/me/erase` — route + service exist per RDY-Right-to-Erasure).

### Security tab (`AccountSettingsSecurity.vue` — new)

- **Linked providers**: list of currently-linked Firebase providers (email,
  google.com, apple.com, microsoft.com, phone). Link/unlink buttons per provider.
  Disable unlink when it's the last sign-in method.
- **Password change**: requires recent sign-in; email/password provider only.
- **MFA enrolment**: phone-number-based TOTP via Firebase
  (`multiFactor().enroll()`). Unenroll option.
- **Active sessions**: list devices with "sign out everywhere"
  (`revokeRefreshTokens`).
- **Login history**: last N sign-ins from the audit stream. Read-only.
- **Recovery codes**: generate + download TXT. One-time view per code.

### API surface

- `GET /api/admin/me/profile` — hydrate the Account tab
- `PATCH /api/admin/me/profile` — display-name / locale / tz updates
- `POST /api/admin/me/sign-out-everywhere` — revoke tokens
- `GET /api/admin/me/sign-in-history` — last 10 events from `[AUDIT]` stream
- (Linked-providers + MFA + password change are client-side Firebase SDK calls;
  server only records the audit event)

### Tests

- The existence of a real view file is a CI guard: add an architecture test
  that fails the build if any `pages/*.vue` matches the regex
  `coming soon|TODO|placeholder` outside of an explicitly allowlisted dev area.
  This is the mechanical prevention for the pattern that produced this bug.

## Acceptance criteria

- [ ] `src/admin/full-version/src/views/apps/account-settings/AccountSettingsAccount.vue` exists and renders the full Account tab.
- [ ] `src/admin/full-version/src/views/apps/account-settings/AccountSettingsSecurity.vue` exists and renders the full Security tab.
- [ ] `pages/pages/account-settings/[tab].vue` imports both components; no "coming soon" strings remain.
- [ ] All five Security sections functional against the Firebase Auth emulator
  (link google.com provider, enrol MFA, sign out everywhere, etc.).
- [ ] Architecture test catches future "coming soon" regressions in pages/.
- [ ] ADM-016 gets moved back out of `tasks/admin/done/` into `tasks/admin/`
  **OR** a replacement ticket closes it once the above is actually shipped.

## Cross-cutting corrective

Grep the repo for other done/ tasks that claim SPA pages. Sample check:

```bash
for task in tasks/admin/done/*.md tasks/readiness/done/*.md; do
  grep -l "src/.*views/.*\.vue\|src/.*pages/.*\.vue" "$task" 2>/dev/null
done | while read task; do
  grep -oE "src/[^ ]+\.vue" "$task" | while read path; do
    [ -f "/Users/shaker/edu-apps/cena/$path" ] || echo "MISSING: $path (claimed by $task)"
  done
done
```

Run that and triage any other claims-done tasks whose view files don't exist.
Each one gets either moved back to pending or a replacement RDY filed.

## Why this matters beyond one page

The pattern — task filed in done/ while the actual feature is a placeholder — is
precisely the failure mode Rami's adversarial review lens catches ("If it can't
be verified, it doesn't exist"). The task-index integrity is load-bearing for
the pilot-readiness audit. Every lying label erodes trust in the overall
readiness number.
