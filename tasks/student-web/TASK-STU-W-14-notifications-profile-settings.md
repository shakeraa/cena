# TASK-STU-W-14: Notifications, Profile, Settings

**Priority**: MEDIUM — account surface, not on the critical path but needed before launch
**Effort**: 3-4 days
**Phase**: 3
**Depends on**: [STU-W-04](TASK-STU-W-04-auth-onboarding.md)
**Backend tasks**: [STB-00](../student-backend/TASK-STB-00-me-profile-onboarding.md), [STB-07](../student-backend/TASK-STB-07-notifications.md)
**Status**: Not Started

---

## Goal

Ship the notification center, public profile, profile editor, and the six settings tabs (account, appearance, notifications, privacy, learning, home layout) — wired to STB-00 and STB-07.

## Spec

Full specification in [docs/student/13-notifications-profile.md](../../docs/student/13-notifications-profile.md). Acceptance criteria across three prefixes:
- `STU-NOT-001` through `STU-NOT-009` — notifications
- `STU-PRO-001` through `STU-PRO-007` — profile
- `STU-SET-001` through `STU-SET-012` — settings

All 28 criteria form this task's checklist.

## Scope

In scope:

### Notifications (`/notifications`)

- `<NotificationList>` grouped by day, filter tabs (learning, achievements, social, system)
- `<NotificationItem>` with type-specific icon, title, body, timestamp, primary action, read/unread state
- Swipe / hover delete + mark read
- Realtime updates via `NotificationDelivered` hub event; sidebar badge updates instantly
- Mark all read action
- Snooze menu: 1h / 4h / until tomorrow
- `<NotificationToast>` for in-app ephemeral alerts; respects receptive-timing rules (no toasts during live sessions)

### Profile (`/profile`, `/profile/edit`)

- `<ProfileHeader>` avatar + display name + role + joined date + level badge
- `<ProfileStatsRow>` KPIs (XP, streak, mastery score, sessions, flow time)
- `<ProfileBadgeShowcase>` top 6 badges
- Favorite subjects chips
- Recent activity (last 5 sessions)
- About / bio section (passes content moderation on save)
- `/profile/edit` form: avatar upload, display name, bio, favorite subjects, visibility (public/friends/class/private)
- Under-13 default visibility enforced to "Class only"
- Print-friendly version of `/profile` via `@media print` styles
- Parent/tutor share token view renders a progress dashboard variant with no edit controls

### Settings (`/settings/*`)

- Tabbed shell: `account`, `appearance`, `notifications`, `privacy`, `learning`, `home-layout`
- `/settings/account`: email (read-only), password change (Firebase reauth), linked providers (link/unlink), 2FA toggle, data export, delete account (soft-delete with 30-day recovery)
- `/settings/appearance`: theme (system/light/dark), language (en/ar/he with hide-Hebrew toggle outside Israel), font size, high-contrast, reduced motion, dense layout
- `/settings/notifications`: category toggles, channel toggles (in-app/desktop/email/SMS), digest frequency, quiet hours, "send test notification"
- `/settings/privacy`: profile visibility, anonymize peer solutions, show in leaderboards, telemetry opt-in, data export, delete, consent log
- `/settings/learning`: default session length, default subjects, difficulty preference, training wheels defaults, ambient sounds, daily time goal
- `/settings/home-layout`: widget visibility toggles + drag-to-reorder (implemented in STU-W-05; this tab just surfaces it)
- Keyboard shortcut cheatsheet modal opened with `?` key (shortcut registration in STU-W-15; modal component here)
- Connected devices list with revoke buttons (consumes `GET /api/me/devices` + `POST /api/me/devices/{id}/revoke`)
- Consent log table showing what was agreed to and when

Out of scope:

- Actually implementing email / SMS / push delivery — backend (STB-07 + existing infrastructure)
- 2FA enrollment flow — stretch, defer (just expose the toggle)
- GDPR data export ZIP generation — existing backend endpoint

## Definition of Done

- [ ] All 28 `STU-NOT-*` / `STU-PRO-*` / `STU-SET-*` acceptance criteria in [13-notifications-profile.md](../../docs/student/13-notifications-profile.md) pass
- [ ] Notification badge updates instantly via SignalR
- [ ] Quiet hours prevent in-app toasts during the configured window
- [ ] Profile edit enforces content moderation on bio (blocked words surface inline)
- [ ] Under-13 account defaults to "Class only" visibility and cannot set it to public
- [ ] All settings persist to backend and are reflected on mobile within one refresh
- [ ] Desktop notification permission requested only on explicit opt-in click
- [ ] Send-test-notification button works for each enabled channel (in-app is the only channel required in v1; others can stub if backend not ready)
- [ ] Print profile produces a clean printable layout
- [ ] Parent/tutor share token renders a progress variant with no edit buttons
- [ ] Account delete triggers soft-delete + recovery email + logout within 5 seconds
- [ ] Playwright covers: notification mark-read, notification snooze, profile edit visibility change, settings theme switch, settings language switch with RTL, device revoke
- [ ] Cross-cutting concerns from the bundle README apply

## Risks

- **Firebase reauth flow** — password change requires reauth; Firebase's modal interrupts the page. Use the redirect-style reauth for consistent UX.
- **Email verification on email change** — Firebase requires re-verification. Surface this clearly; do not silently break the account.
- **Consent log truthfulness** — the consent log must come from the server, not the client. Never invent entries.
- **Print styles drift** — print is easy to forget. Add a dedicated print-preview test that renders the profile in a headless browser with `emulateMedia('print')` and snapshots the DOM.
- **Linked providers edge cases** — unlinking the last provider would orphan the account. Block this with a server-side check and mirror it in the UI.
- **Device list staleness** — the list may include long-stale sessions. Show `lastSeen` relative time and hint that old ones can be safely revoked.
