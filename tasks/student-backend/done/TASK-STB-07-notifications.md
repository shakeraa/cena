# TASK-STB-07: Notifications (REST + Receptive Timing + Channels)

**Priority**: MEDIUM
**Effort**: 3-4 days
**Depends on**: [STB-00](TASK-STB-00-me-profile-onboarding.md)
**UI consumers**: [STU-W-14](../student-web/TASK-STU-W-14-notifications-profile-settings.md)
**Status**: Not Started

---

## Goal

Stand up the notification center REST surface, receptive-timing delivery engine, channel fan-out (in-app / desktop web push / email / SMS), and quiet-hours enforcement. Mirrors the research in mobile's `notification_intelligence_service.dart`.

## Endpoints

| Method | Path | Purpose | Rate limit | Auth |
|---|---|---|---|---|
| `GET` | `/api/notifications?filter=&page=` | Paginated notification list | `api` | JWT |
| `POST` | `/api/notifications/{id}/read` | Mark a single notification read | `api` | JWT |
| `POST` | `/api/notifications/mark-all-read` | Mark everything read | `api` | JWT |
| `DELETE` | `/api/notifications/{id}` | Delete a notification | `api` | JWT |
| `POST` | `/api/notifications/snooze` | Snooze non-urgent notifications | `api` | JWT |
| `POST` | `/api/notifications/test` | Send a test notification (for settings UI) | `api` (5/min) | JWT |
| `POST` | `/api/notifications/subscribe/web-push` | Register a browser push subscription | `api` | JWT |
| `DELETE` | `/api/notifications/subscribe/web-push` | Unsubscribe a browser push subscription | `api` | JWT |

## Data Access

- **Reads**: `NotificationDocument` (new, per student), `WebPushSubscriptionDocument`
- **Writes**: append `NotificationDelivered_V1`, `NotificationRead_V1`, `NotificationDeleted_V1`, `NotificationSnoozed_V1`
- **Projection**: `StudentNotificationInboxProjection` (new async) — maintains a fast paginated inbox per student

## Delivery Engine

A new `NotificationDispatcher` service runs continuously and:

1. Listens for notification-worthy events on NATS (`XpAwarded`, `StreakBroken`, `QuestCompleted`, `BadgeEarned`, `ReviewDue`, `FriendRequestReceived`, etc.)
2. For each, decides which notification(s) to create based on the student's preferences
3. Applies receptive-timing rules:
   - Never deliver during a live session
   - Prefer after-session or morning delivery
   - Respect quiet hours configured by the student
   - Cap frequency per category (e.g. at most 3 XP milestones per day)
4. Fans out to enabled channels for the student:
   - In-app: always, immediate via `NotificationDelivered` hub event
   - Desktop web push: via web push API with VAPID keys, using the subscription from `POST /api/notifications/subscribe/web-push`
   - Email: via existing email service, digest-aware
   - SMS: via existing SMS service, critical alerts only

## Hub Events (additive, land in STB-10)

- `NotificationDelivered` — new notification for the student
- `NotificationInboxChanged` — counts changed (read, delete, bulk)

## Preferences (read from `/api/me/settings`)

Settings read from STB-00 and honored by the dispatcher:

- Categories: learning, achievements, social, system
- Channels: in-app, desktop, email, SMS
- Digest frequency for email: instant / daily / weekly / off
- Quiet hours start + end + timezone
- Frequency caps per category

## Contracts

Add to `Cena.Api.Contracts/Dtos/Notifications/`:

- `NotificationDto` with discriminator by category
- `NotificationActionDto` — primary action (deep link + label)
- `NotificationSnoozeRequest` — duration
- `WebPushSubscriptionDto`

## Auth & Authorization

- Firebase JWT
- `ResourceOwnershipGuard` — student can only touch their own notifications
- Test-notification endpoint rate-limited

## Cross-Cutting

- Handler logs with `correlationId`, `endpoint=notifications.*`
- Dispatcher logs every delivery decision with: triggerEvent, student, channel, delivered/suppressed, reason
- Email + SMS delivery uses existing infrastructure; no new vendor integrations
- Web Push VAPID keys stored as secrets; key rotation documented in the runbook

## Definition of Done

- [ ] Eight endpoints implemented and registered in `Cena.Student.Api.Host`
- [ ] DTOs in `Cena.Api.Contracts/Dtos/Notifications/`
- [ ] `NotificationDispatcher` service running in `Cena.Actors.Host`
- [ ] Receptive-timing rules verified: live-session suppression, quiet-hours honor, frequency caps
- [ ] Hub event `NotificationDelivered` fires within 100 ms of the triggering event outside quiet hours
- [ ] Web push delivers to Chrome + Edge + Safari (macOS 16+)
- [ ] Email digest batches correctly for "daily" and "weekly" frequencies
- [ ] SMS delivery only fires for critical alerts (streak-at-risk confirmed as v1's only critical)
- [ ] Integration tests cover: basic delivery, quiet-hours suppression, in-session suppression, digest batching
- [ ] Test notification endpoint works for each enabled channel
- [ ] OpenAPI spec updated
- [ ] TypeScript types regenerated
- [ ] Mobile lead review: mobile FCM push continues to work in parallel; web push is additive

## Out of Scope

- Vendor switch for email or SMS — use existing
- Rich push notifications with images — v1 is text-only
- Per-notification-type templates — use a simple title/body schema
- Delivery analytics dashboard — admin surface, separate
- Push notification A/B testing — future
