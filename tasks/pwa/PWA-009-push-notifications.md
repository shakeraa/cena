# PWA-009: Web Push Notifications

## Goal
Implement Web Push notifications for session reminders, assignment alerts, and teacher messages. Handle the iOS Safari limitations gracefully — this is a supplementary channel, not the primary communication mechanism.

## Context
- Architecture doc: `docs/research/cena-mobile-pwa-approach.md` §3.1
- iOS 16.4+ supports Web Push for installed PWAs, but with limitations (no badge, no silent push)
- Android Chrome: full Web Push support
- Desktop: full Web Push support (Chrome, Firefox, Edge)
- Students are minors — notification content must be appropriate, frequency must be reasonable

## Scope of Work

### 1. Push Subscription
Create `src/student/full-version/src/services/pushNotifications.ts`:

- VAPID key pair (server generates, public key deployed to client)
- Subscribe via `PushManager.subscribe()` with `applicationServerKey`
- Send subscription object to backend: `POST /api/notifications/subscribe`
- Handle subscription refresh (push subscriptions can expire)

### 2. Notification Permission Flow
Create `src/student/full-version/src/composables/usePushPermission.ts`:

- Pre-permission dialog (before browser prompt): explain what notifications are for
- "Get reminders before study sessions" — not "Enable notifications" (tell the value, not the mechanism)
- Ask on 3rd session start (not first visit — build trust first)
- If denied: never ask again. Show subtle settings link in profile page.
- If granted: subscribe immediately
- iOS: check `'PushManager' in window` — only available for installed PWAs. If not installed, show "Install the app first to get reminders"

### 3. Notification Types

| Type | Trigger | Content | Priority |
|------|---------|---------|----------|
| Session reminder | 15 min before scheduled session | "Ready for math? Your session starts soon" | Normal |
| Assignment due | Teacher sets deadline | "Assignment due tomorrow: Derivatives" | Normal |
| Teacher message | Teacher sends via admin | Custom message text | High |
| Weekly summary | Sunday morning | "This week: 3 sessions, 87% accuracy" | Low |
| Mastery milestone | Student masters a skill | "You mastered Integrals!" | Normal |

### 4. Notification Click Handling
In Service Worker:
```javascript
self.addEventListener('notificationclick', (event) => {
  const { type, targetUrl } = event.notification.data;
  event.notification.close();
  event.waitUntil(
    clients.matchAll({ type: 'window' }).then(windowClients => {
      // Focus existing window or open new one
      const client = windowClients.find(c => c.url === targetUrl);
      if (client) return client.focus();
      return clients.openWindow(targetUrl);
    })
  );
});
```

### 5. Backend Integration
- Endpoint: `POST /api/notifications/subscribe` — stores subscription
- Endpoint: `DELETE /api/notifications/subscribe` — unsubscribes
- Endpoint: `GET /api/notifications/preferences` — get/set notification preferences
- Server uses `web-push` library (Node.js) or `WebPush` NuGet package (.NET)
- Store subscriptions per student, per device (a student may have multiple devices)

### 6. Preferences UI
In settings page:
- Toggle per notification type (session reminders, assignments, teacher messages, weekly summary, milestones)
- "Test notification" button — sends a test push to verify setup
- Clear explanation that iOS requires the app to be installed

## Non-Negotiables
- **No dark patterns** in permission flow — "Not now" is equally prominent as "Enable"
- **No spam** — maximum 3 notifications per day, maximum 10 per week
- **Notification content appropriate for minors** — no urgency language ("You're falling behind!")
- **Graceful iOS degradation** — if push isn't available, don't show error; use email/SMS fallback silently
- **VAPID keys never in client code** — only the public key

## Acceptance Criteria
- [ ] Push subscription works on Android Chrome
- [ ] Push subscription works on iOS Safari 16.4+ (installed PWA only)
- [ ] Push subscription works on desktop Chrome/Firefox/Edge
- [ ] Pre-permission dialog shown on 3rd session (not before)
- [ ] Notification click opens the correct page
- [ ] All 5 notification types send and display correctly
- [ ] Preference toggles work (disable/enable per type)
- [ ] Rate limiting: max 3/day, 10/week enforced server-side
- [ ] All notification text i18n'd (Arabic + Hebrew)
- [ ] iOS: "Install app first" message when not installed

## Testing Requirements
- **Unit**: `usePushPermission.ts` — mock PushManager, test permission states
- **Unit**: `pushNotifications.ts` — test subscribe/unsubscribe flows
- **Integration**: Test notification click routing
- **Manual (REQUIRED)**: Real device push testing — emulators don't support Web Push
  - Android Chrome (notification + click)
  - iOS Safari installed PWA (notification + click)
  - Desktop Chrome (notification + click)

## DoD
- PR merged to `main`
- Real device push notification screenshot (Android + iOS)
- Rate limiting test results
- Permission flow video

## Reporting
Complete with: `branch=<worker>/<task-id>-pwa-push-notifications,platforms_tested=<n>,notification_types=<n>`
