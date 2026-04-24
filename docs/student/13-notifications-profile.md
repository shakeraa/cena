# 13 — Notifications & Profile

## Overview

The account surface: how the student stays informed (notifications) and how they manage their identity and preferences (profile, settings).

## Mobile Parity

- [notification_center_screen.dart](../../src/mobile/lib/features/notifications/notification_center_screen.dart)
- [profile_screen.dart](../../src/mobile/lib/features/profile/profile_screen.dart)
- [core/services/notification_intelligence_service.dart](../../src/mobile/lib/core/services/notification_intelligence_service.dart)

---

## Notifications

### `/notifications`

Full-screen list of notifications grouped by day, with filter tabs.

Categories:
- **Learning** — session reminders, review-due alerts, streak at risk
- **Achievements** — XP milestones, badges, level-ups, quest completions
- **Social** — friend requests, comments, reactions, class announcements
- **System** — account, security, maintenance

Per item:
- Icon, title, body, timestamp
- Read / unread state
- Primary action (e.g. "Review now", "View badge", "Reply")
- Swipe / hover to delete or mark read

Realtime: new notifications arrive via SignalR `NotificationDelivered` event. The sidebar badge updates instantly.

### Delivery Channels

Mobile sends push notifications via FCM. Web adds:
- **In-app** — always on
- **Desktop browser** — opt-in via Notification API permission
- **Email** — opt-in, digest options (instant / daily / weekly / off)
- **SMS** — opt-in for critical alerts only (streak-at-risk)

Configured in `/settings/notifications`.

### Intelligent Timing

Backend `notification_intelligence_service.dart` equivalent holds notifications until a receptive moment (matches learning science research):
- Don't interrupt a live session
- Prefer after-session or morning delivery
- Respect quiet hours set by the student
- Cap frequency per category

The web client must respect the same "receptive timing" rules when rendering in-app toasts.

---

## Profile

### `/profile`

Public-ish view of the student: display name, avatar, badges, stats, streak, level.

Sections:
- **Header** — avatar, display name, role, joined date, level badge
- **Stats** — total XP, streak, mastery score, sessions completed, flow time
- **Badge showcase** — top 6 earned badges
- **Favorite subjects** — student-curated chips
- **Recent activity** — last 5 sessions (similar to home teaser)
- **About** — short bio (optional, moderated)

When visited by the student themselves → full view + Edit button.
When visited by a friend → public-safe view respecting privacy settings.
When visited via parent/tutor share token → progress dashboard variant.

### `/profile/edit`

Edit avatar, display name, bio, favorite subjects, and visibility settings:
- Public (anyone with link)
- Friends only
- Class only
- Private (only the student)

Default for under-13 is "Class only".

---

## Settings

Settings are split into tabs matching the admin pattern:

### `/settings/account`

- Email (view only; change via Firebase flow)
- Password change (Firebase reauth required)
- Linked providers (Google, Apple, Microsoft) — link / unlink
- Two-factor authentication toggle
- Download my data (GDPR)
- Delete account (soft-delete with 30-day recovery)

### `/settings/appearance`

- Theme: system / light / dark
- Language: en / ar / he (+ hide Hebrew toggle where applicable)
- Font size: default / large / larger
- High-contrast mode
- Reduced motion
- Dense layouts (less padding, more data)

### `/settings/notifications`

- Category toggles (learning, achievements, social, system)
- Channel toggles (in-app, desktop, email, SMS)
- Digest frequency for email
- Quiet hours start / end
- "Send a test notification"

### `/settings/privacy`

- Profile visibility (public / friends / class / private)
- Anonymize peer solutions
- Show in leaderboards
- Share usage telemetry (opt-in)
- Download my data
- Delete account
- Consent log (what the student has agreed to, with dates)

### `/settings/learning`

- Default session length
- Default subjects
- Preferred difficulty (easy / normal / hard / adaptive)
- Training wheels defaults
- Hint generosity default
- Confidence prompts default
- Audio feedback (on / off)
- Ambient sounds (mobile has lofi / rain / library)
- Daily time goal

### `/settings/home-layout`

- Toggle visibility per home widget
- Drag to reorder widgets
- Reset to defaults

---

## Web-Specific Enhancements

- **Keyboard shortcut cheatsheet** — press `?` anywhere to open a modal listing all shortcuts (see [14-web-enhancements](14-web-enhancements.md)).
- **Session import / export** — export all personal data as JSON; import a backup on a new account.
- **Connected devices** — list of active sessions (web, mobile) with revoke buttons.
- **Print profile page** — printable version for portfolios.
- **Notification snooze** — snooze notifications for 1h / 4h / until tomorrow.
- **Custom notification sounds** — pick from a short library.
- **Language switcher in top bar** — quick language change without leaving page.

---

## Components

| Component | Purpose |
|-----------|---------|
| `<NotificationList>` | Grouped, filtered list |
| `<NotificationItem>` | Single notification with actions |
| `<NotificationToast>` | In-app toast |
| `<ProfileHeader>` | Avatar + name + level badge |
| `<ProfileStatsRow>` | KPI row |
| `<ProfileBadgeShowcase>` | Top-6 badge carousel |
| `<ProfileEditForm>` | Edit fields + visibility controls |
| `<SettingsShell>` | Tabbed settings container |
| `<SettingsSection>` | Section within a tab |
| `<KeyboardShortcutCheatsheet>` | Modal listing shortcuts |
| `<ConnectedDevicesList>` | Active session list with revoke |
| `<ConsentLog>` | Consent history table |

---

## Acceptance Criteria

- [ ] `STU-NOT-001` — `/notifications` lists notifications grouped by day with filter tabs.
- [ ] `STU-NOT-002` — SignalR `NotificationDelivered` event updates the list and sidebar badge in realtime.
- [ ] `STU-NOT-003` — Mark all read, delete, per-item action buttons.
- [ ] `STU-NOT-004` — Channel toggles in `/settings/notifications` (in-app, desktop, email, SMS).
- [ ] `STU-NOT-005` — Desktop browser notification permission requested on toggle.
- [ ] `STU-NOT-006` — Quiet hours block non-urgent notifications during the set window.
- [ ] `STU-NOT-007` — Receptive-timing rules prevent in-app toasts during live sessions.
- [ ] `STU-NOT-008` — "Send test notification" button works for each enabled channel.
- [ ] `STU-NOT-009` — Notification snooze menu (1h / 4h / tomorrow) works.

- [ ] `STU-PRO-001` — `/profile` shows header, stats, badge showcase, favorite subjects, recent activity.
- [ ] `STU-PRO-002` — Own view shows Edit button; friend / public views respect visibility settings.
- [ ] `STU-PRO-003` — `/profile/edit` updates avatar, name, bio, subjects, visibility.
- [ ] `STU-PRO-004` — Under-13 default visibility is "Class only".
- [ ] `STU-PRO-005` — Bio passes through content moderation before save.
- [ ] `STU-PRO-006` — Parent/tutor share token renders a progress dashboard variant.
- [ ] `STU-PRO-007` — Print profile page renders a print-friendly version.

- [ ] `STU-SET-001` — Settings shell with all 6 tabs.
- [ ] `STU-SET-002` — Account tab: email, password change, linked providers, 2FA, data export, delete.
- [ ] `STU-SET-003` — Appearance tab: theme, language, font size, high-contrast, reduced motion, dense layout.
- [ ] `STU-SET-004` — Notifications tab: all categories and channels configurable.
- [ ] `STU-SET-005` — Privacy tab: visibility, anonymization, telemetry, data export, delete, consent log.
- [ ] `STU-SET-006` — Learning tab: default session length, difficulty, training wheels, ambient sounds, daily goal.
- [ ] `STU-SET-007` — Home layout tab: toggle and reorder widgets.
- [ ] `STU-SET-008` — All settings persist to backend and sync to mobile.
- [ ] `STU-SET-009` — Keyboard shortcut cheatsheet (`?`) lists all shortcuts grouped by area.
- [ ] `STU-SET-010` — Connected devices list shows all active sessions with revoke.
- [ ] `STU-SET-011` — Data export returns a ZIP with JSON + media within 10 minutes.
- [ ] `STU-SET-012` — Account delete starts a 30-day soft-delete with a recovery email.

## Backend Dependencies

- `GET /api/notifications` — new
- `POST /api/notifications/{id}/read` — new
- `POST /api/notifications/mark-all-read` — new
- `DELETE /api/notifications/{id}` — new
- `GET /api/me/profile` — new
- `PATCH /api/me/profile` — new
- `GET /api/me/settings` — new
- `PATCH /api/me/settings` — new
- `GET /api/me/devices` — new
- `POST /api/me/devices/{id}/revoke` — new
- `POST /api/me/data-export` — exists (GdprEndpoints.cs — shared with admin)
- `POST /api/me/delete` — exists (GdprEndpoints.cs)
- Hub event: `NotificationDelivered`
