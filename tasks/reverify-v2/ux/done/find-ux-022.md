---
id: FIND-UX-022
task_id: t_9236e6014b58
severity: P0 — Critical
lens: ux
tags: [reverify, ux, a11y, wcag2.2, keyboard]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-ux-022: Student user-profile menu activator is a div not a button — sign-out unreachable for keyboard/SR users

## Summary

Student user-profile menu activator is a div not a button — sign-out unreachable for keyboard/SR users

## Severity

**P0 — Critical**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

## FIND-ux-022: User profile menu activator is a div, not a button — wrap in VBtn

**Severity**: p0
**Lens**: ux
**Category**: a11y, WCAG-2.2-AA
**Related prior finding**: FIND-ux-018 (admin "Append" — same family of issue)

## File
src/student/full-version/src/layouts/components/UserProfile.vue:48-116

## Evidence
- Snapshot: page a11y tree shows `generic [cursor=pointer]: S` (a div, not a button)
- DOM probe: `tag: 'DIV', role: null, tabindex: -1, ariaHaspopup: 'menu', ariaExpanded: 'false', keyboardReachable: false`
- axe-core: `aria-allowed-attr` critical on `.v-avatar` ("ARIA attribute is not allowed: aria-expanded='false'")

## Goal
Make the user profile menu (Profile / Settings / Sign Out) reachable for keyboard and screen-reader users.

## Definition of Done
1. Wrap `<VAvatar>` in `<VBtn icon variant="text" :aria-label="t('nav.userMenu', { name: displayName })">`:
   ```vue
   <VBtn icon variant="text" size="38"
     :aria-label="t('nav.userMenu', { name: displayName })"
     data-testid="user-profile-avatar-button">
     <VAvatar size="38" color="primary" variant="tonal">
       <span class="text-caption font-weight-bold">{{ initials }}</span>
     </VAvatar>
     <VMenu activator="parent" ...>...</VMenu>
   </VBtn>
   ```
2. Add the `nav.userMenu` i18n key to all three locale files (EN: "Account menu for {name}", AR + HE).
3. axe-core scan of /home, /tutor, /profile shows ZERO `aria-allowed-attr` violations on the avatar.
4. Add `tests/e2e/a11y-keyboard-nav.spec.ts` that:
   - Tabs through all interactive elements on /home and verifies the avatar button is reachable
   - Presses Enter/Space on it and verifies the menu opens
   - Asserts `aria-haspopup="menu"` and `aria-expanded` toggle correctly on a real button element
5. Sign-out flow still works after the wrap (no event-handler regression).

## Coordination
Coordinate with FIND-ux-025 and FIND-ux-028 — the three together are one a11y header-bar pass.

## Reporting requirements
Branch: `<worker>/find-ux-022-user-profile-button`
In `--result`, paste the axe diff + a Playwright clip of Tab→Enter opening the menu.

## Source
docs/reviews/agent-ux-reverify-2026-04-11.md#FIND-ux-022


## Evidence & context

- Lens report: `docs/reviews/agent-ux-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_9236e6014b58`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
