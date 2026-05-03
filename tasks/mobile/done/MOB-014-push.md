# MOB-014: Firebase Messaging, Deep Links, Notification Channels

**Priority:** P2 — blocks outreach delivery
**Blocked by:** SEC-001 (Firebase Auth), ACT-005 (Outreach)
**Estimated effort:** 2 days
**Contract:** `contracts/mobile/lib/core/services/websocket_service.dart`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Subtasks

### MOB-014.1: Firebase Cloud Messaging Setup
- [ ] FCM token registration on login
- [ ] Token refresh handling
- [ ] Background message handler for data messages
- [ ] iOS: APNs configuration + permission prompt

### MOB-014.2: Deep Links
- [ ] Notification tap -> deep link to relevant screen
- [ ] Links: `/session/start`, `/graph/concept/{id}`, `/review/{conceptId}`
- [ ] Universal links (iOS) / App Links (Android) for web-to-app

### MOB-014.3: Notification Channels (Android)
- [ ] `streak_reminders` — streak expiry warnings (high priority)
- [ ] `review_due` — spaced repetition reminders (default priority)
- [ ] `general` — announcements (low priority)
- [ ] User can disable per-channel in Android settings

**Test:**
```dart
test('Deep link routes to correct screen', () {
  final route = parseDeepLink('cena://session/start?subject=math');
  expect(route.screen, equals('SessionScreen'));
  expect(route.params['subject'], equals('math'));
});
```

---

## Definition of Done
- [ ] Push notifications received on iOS and Android
- [ ] Deep links route correctly
- [ ] PR reviewed by architect
