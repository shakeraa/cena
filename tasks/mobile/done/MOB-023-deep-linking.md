# MOB-023: Deep Linking (iOS, Android, Web Fallback)

**Priority:** P0 — BLOCKER (push notifications without deep links lead to dead-end UX)
**Blocked by:** SEC-009 (Firebase Auth — authenticated deep link resolution), MOB-001 (Flutter app shell)
**Blocks:** Push notification engagement flow, teacher-shared concept links, marketing campaigns
**Estimated effort:** 3 days
**Contract:** `contracts/frontend/signalr-messages.ts` (StartSessionPayload.conceptId, session/concept IDs), `contracts/mobile/pubspec.yaml`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
Cena's engagement loop depends on push notifications that land the student directly in the right learning context: "Review algebra-1 before it decays" should open the app on the exact concept with a pre-started review session. Without deep linking, push notifications open the home screen and students must manually navigate — a 40-60% drop-off per extra tap (Localytics benchmark). Deep links must work across iOS (Associated Domains), Android (App Links), and degrade gracefully to web for users without the app installed.

## Subtasks

### MOB-023.1: iOS Associated Domains + Universal Links
**Files:**
- `ios/Runner/Runner.entitlements` — Associated Domains entitlement
- `config/deeplink/apple-app-site-association` — AASA file for `app.cena.co.il`
- `lib/core/routing/deep_link_handler.dart` — Flutter deep link parser
- `lib/core/routing/app_router.dart` — route registration for deep link paths

**Acceptance:**
- [ ] AASA file hosted at `https://app.cena.co.il/.well-known/apple-app-site-association`
- [ ] AASA covers paths: `/concept/*`, `/session/*`, `/review/*`, `/invite/*`, `/class/*`
- [ ] AASA `applinks` entry: `appID: {TEAM_ID}.co.il.cena.app`
- [ ] `flutter_deep_linking` or `go_router` handles incoming URI
- [ ] URI schema: `https://app.cena.co.il/concept/{conceptId}?action=review&sessionType=spaced-repetition`
- [ ] Deep link opens app directly to concept screen (no intermediate navigation)
- [ ] If app not installed: fallback to App Store page (AASA serves web content as fallback)
- [ ] If user not authenticated: deep link stored, resume after login
- [ ] `uni_links` package version pinned in `pubspec.yaml`

**Test:**
```dart
// test/core/routing/deep_link_handler_test.dart
group('DeepLinkHandler', () {
  late DeepLinkHandler handler;
  late MockRouter mockRouter;
  late MockAuthService mockAuth;

  setUp(() {
    mockRouter = MockRouter();
    mockAuth = MockAuthService();
    handler = DeepLinkHandler(router: mockRouter, auth: mockAuth);
  });

  test('concept deep link navigates to concept screen', () async {
    when(mockAuth.isAuthenticated).thenReturn(true);

    await handler.handleUri(Uri.parse(
      'https://app.cena.co.il/concept/algebra-1?action=review'
    ));

    verify(mockRouter.navigateTo(
      '/concept/algebra-1',
      extra: {'action': 'review'},
    )).called(1);
  });

  test('session deep link navigates to active session', () async {
    when(mockAuth.isAuthenticated).thenReturn(true);

    await handler.handleUri(Uri.parse(
      'https://app.cena.co.il/session/sess-123'
    ));

    verify(mockRouter.navigateTo('/session/sess-123')).called(1);
  });

  test('unauthenticated user stores deep link and redirects to login', () async {
    when(mockAuth.isAuthenticated).thenReturn(false);

    await handler.handleUri(Uri.parse(
      'https://app.cena.co.il/concept/algebra-1?action=review'
    ));

    verify(mockRouter.navigateTo('/login')).called(1);
    expect(handler.pendingDeepLink, isNotNull);
    expect(handler.pendingDeepLink!.path, '/concept/algebra-1');
  });

  test('pending deep link resumed after login', () async {
    when(mockAuth.isAuthenticated).thenReturn(false);
    await handler.handleUri(Uri.parse('https://app.cena.co.il/concept/algebra-1'));

    // Simulate login
    when(mockAuth.isAuthenticated).thenReturn(true);
    await handler.onAuthStateChanged(authenticated: true);

    verify(mockRouter.navigateTo('/concept/algebra-1')).called(1);
    expect(handler.pendingDeepLink, isNull);
  });

  test('invalid deep link path shows error screen', () async {
    when(mockAuth.isAuthenticated).thenReturn(true);

    await handler.handleUri(Uri.parse(
      'https://app.cena.co.il/nonexistent/path'
    ));

    verify(mockRouter.navigateTo('/error', extra: {'code': 'INVALID_DEEP_LINK'})).called(1);
  });

  test('malicious path traversal is sanitized', () async {
    when(mockAuth.isAuthenticated).thenReturn(true);

    await handler.handleUri(Uri.parse(
      'https://app.cena.co.il/concept/../../etc/passwd'
    ));

    verify(mockRouter.navigateTo('/error', extra: {'code': 'INVALID_DEEP_LINK'})).called(1);
    verifyNever(mockRouter.navigateTo(argThat(contains('../'))));
  });
});
```

---

### MOB-023.2: Android App Links + Intent Filters
**Files:**
- `android/app/src/main/AndroidManifest.xml` — intent filter for App Links
- `config/deeplink/assetlinks.json` — Digital Asset Links file for `app.cena.co.il`
- `lib/core/routing/deep_link_handler.dart` — shared with iOS (platform-agnostic)

**Acceptance:**
- [ ] Digital Asset Links file hosted at `https://app.cena.co.il/.well-known/assetlinks.json`
- [ ] `assetlinks.json` contains SHA-256 certificate fingerprint for release and debug builds
- [ ] Intent filter in `AndroidManifest.xml`:
  ```xml
  <intent-filter android:autoVerify="true">
    <action android:name="android.intent.action.VIEW" />
    <category android:name="android.intent.category.DEFAULT" />
    <category android:name="android.intent.category.BROWSABLE" />
    <data android:scheme="https" android:host="app.cena.co.il" android:pathPrefix="/concept" />
    <data android:scheme="https" android:host="app.cena.co.il" android:pathPrefix="/session" />
    <data android:scheme="https" android:host="app.cena.co.il" android:pathPrefix="/review" />
    <data android:scheme="https" android:host="app.cena.co.il" android:pathPrefix="/invite" />
    <data android:scheme="https" android:host="app.cena.co.il" android:pathPrefix="/class" />
  </intent-filter>
  ```
- [ ] `android:autoVerify="true"` triggers domain verification at install time
- [ ] Cold start deep link: app launched from killed state, deep link resolved after init
- [ ] Warm start deep link: app in background, deep link brings to foreground on correct screen

**Test:**
```dart
// test/core/routing/android_deep_link_test.dart
group('Android App Links', () {
  test('cold start deep link resolves after initialization', () async {
    // Simulate cold start with initial link
    final handler = DeepLinkHandler(
      router: mockRouter,
      auth: MockAuthService(authenticated: true),
    );

    // Simulate platform channel delivering initial link
    await handler.handleInitialLink(
      Uri.parse('https://app.cena.co.il/concept/calculus-chain-rule?action=review')
    );

    verify(mockRouter.navigateTo(
      '/concept/calculus-chain-rule',
      extra: {'action': 'review'},
    )).called(1);
  });

  test('warm start deep link brings correct screen to foreground', () async {
    final handler = DeepLinkHandler(
      router: mockRouter,
      auth: MockAuthService(authenticated: true),
    );

    // Simulate stream link (app already running)
    handler.onNewLink(
      Uri.parse('https://app.cena.co.il/review/algebra-1')
    );

    verify(mockRouter.navigateTo('/review/algebra-1')).called(1);
  });

  test('custom scheme cena:// also works as fallback', () async {
    final handler = DeepLinkHandler(
      router: mockRouter,
      auth: MockAuthService(authenticated: true),
    );

    await handler.handleUri(Uri.parse('cena://concept/geometry-1'));

    verify(mockRouter.navigateTo('/concept/geometry-1')).called(1);
  });
});
```

---

### MOB-023.3: Push Notification → Deep Link Flow
**Files:**
- `lib/core/notifications/notification_handler.dart` — FCM notification handler
- `lib/core/notifications/deep_link_builder.dart` — builds deep link URLs for outreach
- `src/Cena.Outreach/DeepLinkGenerator.cs` — server-side deep link URL generation

**Acceptance:**
- [ ] Push notification payload includes `deepLink` field: `{"deepLink": "https://app.cena.co.il/concept/algebra-1?action=review"}`
- [ ] Tapping notification in foreground: deep link handled inline (no app restart)
- [ ] Tapping notification from killed state: app launches, deep link resolved after auth
- [ ] Tapping notification from background: app brought to foreground, deep link resolved
- [ ] Notification types with deep links:
  - `review_due` → `/review/{conceptId}` (from OutreachSchedulerActor, per `cena.engagement.events.ReviewDue`)
  - `streak_expiring` → `/session?subject=last_active` (from `cena.engagement.events.StreakExpiring`)
  - `stagnation_nudge` → `/concept/{conceptId}?methodology=new` (from `cena.learner.events.StagnationDetected`)
  - `class_assignment` → `/class/{classId}` (from `cena.school.events.ClassAssigned`)
  - `trial_expiring` → `/subscription/upgrade` (from subscription lifecycle)
- [ ] Server-side `DeepLinkGenerator` creates URLs with tracking params: `?utm_source=push&utm_campaign={campaignId}&notificationId={id}`
- [ ] Web fallback: if app not installed, link opens `https://app.cena.co.il/concept/algebra-1` which serves PWA or redirects to app store
- [ ] State restoration: deep link into a concept triggers `StartSession` with `conceptId` pre-filled (per `StartSessionPayload.conceptId`)

**Test:**
```dart
// test/core/notifications/notification_deep_link_test.dart
group('Push Notification Deep Links', () {
  late NotificationHandler handler;
  late MockDeepLinkHandler mockDeepLink;

  setUp(() {
    mockDeepLink = MockDeepLinkHandler();
    handler = NotificationHandler(deepLinkHandler: mockDeepLink);
  });

  test('review_due notification opens concept review', () async {
    await handler.onNotificationTapped({
      'title': 'Time to review!',
      'body': 'Algebra concepts are getting rusty',
      'deepLink': 'https://app.cena.co.il/review/algebra-1',
      'notificationType': 'review_due',
    });

    verify(mockDeepLink.handleUri(
      Uri.parse('https://app.cena.co.il/review/algebra-1')
    )).called(1);
  });

  test('streak_expiring notification opens session', () async {
    await handler.onNotificationTapped({
      'deepLink': 'https://app.cena.co.il/session?subject=math',
      'notificationType': 'streak_expiring',
    });

    verify(mockDeepLink.handleUri(
      Uri.parse('https://app.cena.co.il/session?subject=math')
    )).called(1);
  });

  test('notification without deep link opens home', () async {
    await handler.onNotificationTapped({
      'title': 'Welcome back!',
      'body': 'Check out new content',
    });

    verify(mockDeepLink.handleUri(
      Uri.parse('https://app.cena.co.il/')
    )).called(1);
  });

  test('foreground notification shows in-app banner, tappable', () async {
    final bannerShown = Completer<Map<String, dynamic>>();
    handler.onForegroundBanner = (data) => bannerShown.complete(data);

    await handler.onForegroundNotification({
      'title': 'Review algebra-1',
      'deepLink': 'https://app.cena.co.il/review/algebra-1',
    });

    final banner = await bannerShown.future;
    expect(banner['deepLink'], 'https://app.cena.co.il/review/algebra-1');
  });
});

// test/server/outreach/deep_link_generator_test.cs
[Theory]
[InlineData("review_due", "algebra-1", "https://app.cena.co.il/review/algebra-1?utm_source=push&utm_campaign=review_due")]
[InlineData("streak_expiring", null, "https://app.cena.co.il/session?subject=last_active&utm_source=push&utm_campaign=streak_expiring")]
[InlineData("stagnation_nudge", "calculus-1", "https://app.cena.co.il/concept/calculus-1?methodology=new&utm_source=push&utm_campaign=stagnation_nudge")]
public void DeepLinkGenerator_CreatesCorrectUrls(string notificationType, string? conceptId, string expectedUrl)
{
    var generator = new DeepLinkGenerator("https://app.cena.co.il");
    var url = generator.Generate(notificationType, conceptId);

    Assert.StartsWith(expectedUrl.Split('?')[0], url);
    Assert.Contains("utm_source=push", url);
    Assert.Contains($"utm_campaign={notificationType}", url);
}

[Fact]
public void DeepLinkGenerator_IncludesNotificationId()
{
    var generator = new DeepLinkGenerator("https://app.cena.co.il");
    var url = generator.Generate("review_due", "algebra-1", notificationId: "notif-123");

    Assert.Contains("notificationId=notif-123", url);
}
```

---

## Integration Test (end-to-end push → deep link → session)

```dart
// test/integration/push_to_session_test.dart
testWidgets('push notification opens concept and starts session', (tester) async {
  // 1. App is authenticated
  when(mockAuth.isAuthenticated).thenReturn(true);
  when(mockAuth.currentUser).thenReturn(testStudent);

  await tester.pumpWidget(CenaApp());

  // 2. Simulate push notification tap
  final notificationHandler = tester.state<CenaAppState>(find.byType(CenaApp)).notificationHandler;
  await notificationHandler.onNotificationTapped({
    'deepLink': 'https://app.cena.co.il/concept/algebra-1?action=review',
    'notificationType': 'review_due',
  });
  await tester.pumpAndSettle();

  // 3. Verify concept screen is showing
  expect(find.text('Linear Equations'), findsOneWidget); // algebra-1 concept name
  expect(find.byType(ConceptReviewScreen), findsOneWidget);

  // 4. Verify SignalR StartSession was called with conceptId
  verify(mockSignalR.invoke('StartSession', argThat(
    predicate<StartSessionPayload>((p) => p.conceptId == 'algebra-1')
  ))).called(1);
});
```

## Edge Cases
- iOS: AASA file not cached by CDN → Apple may not verify domain; use Apple's CDN-ID header
- Android: auto-verify fails silently → fallback to custom scheme `cena://`
- Deep link into removed concept → show "concept no longer available" with navigation to parent subject
- Deep link with Hebrew characters → percent-encode concept names, decode on receipt
- Race condition: deep link arrives before Flutter engine initialized → queue in platform channel, deliver after init

## Rollback Criteria
- If universal links fail on >10% of iOS devices: fall back to custom URI scheme `cena://` with manual app association
- If Android auto-verify fails: use deferred deep linking via Firebase Dynamic Links (deprecated but functional)
- If push → deep link latency >3s: preload concept data during push receipt (background handler)

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] AASA file validates via `https://app-site-association.cdn-apple.com` checker
- [ ] `assetlinks.json` validates via Google Digital Asset Links API
- [ ] iOS: URL in Safari opens app on correct screen
- [ ] Android: URL in Chrome opens app on correct screen
- [ ] Web: URL without app installed shows web fallback or app store redirect
- [ ] Push notification tap opens correct deep link screen (all 5 notification types)
- [ ] State restoration after authentication works
- [ ] `flutter test test/core/routing/ test/core/notifications/` → 0 failures
- [ ] PR reviewed by architect (you)
