# Cena Platform — Universal Links & Deep Linking Contract

**Layer:** Mobile / Navigation | **Runtime:** React Native (iOS + Android)
**Status:** BLOCKER — push notifications and outreach have no way to open specific app screens

---

## 1. URL Scheme

### Custom Scheme (app-to-app, fallback)

```
cena://{path}
```

### Universal Links / App Links (web-to-app, push notifications)

```
https://app.cena.edu/{path}
```

### Route Table

| Path | Screen | Parameters | Example |
|------|--------|------------|---------|
| `/session/{conceptId}` | Learning session | `conceptId` (required) | `cena://session/quad-equations-01` |
| `/session/{conceptId}/review` | Review session (spaced rep) | `conceptId` (required) | `cena://session/trig-basics-02/review` |
| `/graph` | Knowledge graph view | none | `cena://graph` |
| `/graph/{conceptId}` | Knowledge graph focused on concept | `conceptId` (required) | `cena://graph/algebra-functions-03` |
| `/profile` | Student profile | none | `cena://profile` |
| `/profile/streak` | Streak detail view | none | `cena://profile/streak` |
| `/analytics` | Parent analytics dashboard | none | `cena://analytics` |
| `/analytics/{studentId}` | Parent view of specific child | `studentId` (required) | `cena://analytics/stu_001` |
| `/settings` | App settings | none | `cena://settings` |
| `/upgrade` | Subscription upgrade screen | `source` (optional) | `cena://upgrade?source=trial_ending` |
| `/quiz/{quizId}` | WhatsApp-triggered quiz | `quizId` (required) | `cena://quiz/quiz-abc123` |

---

## 2. iOS Configuration: Associated Domains

### Entitlements

```xml
<key>com.apple.developer.associated-domains</key>
<array>
  <string>applinks:app.cena.edu</string>
  <string>webcredentials:app.cena.edu</string>
</array>
```

### apple-app-site-association (hosted at `https://app.cena.edu/.well-known/apple-app-site-association`)

```json
{
  "applinks": {
    "details": [
      {
        "appIDs": ["TEAM_ID.edu.cena.app"],
        "components": [
          { "/": "/session/*", "comment": "Learning sessions" },
          { "/": "/graph", "comment": "Knowledge graph" },
          { "/": "/graph/*", "comment": "Knowledge graph focused" },
          { "/": "/profile", "comment": "Profile" },
          { "/": "/profile/*", "comment": "Profile sub-screens" },
          { "/": "/analytics", "comment": "Analytics" },
          { "/": "/analytics/*", "comment": "Analytics detail" },
          { "/": "/quiz/*", "comment": "WhatsApp quiz" },
          { "/": "/upgrade", "comment": "Upgrade" },
          { "/": "/settings", "comment": "Settings" }
        ]
      }
    ]
  },
  "webcredentials": {
    "apps": ["TEAM_ID.edu.cena.app"]
  }
}
```

### Notes

- File must be served with `Content-Type: application/json` (no `.json` extension).
- Apple CDN caches this file; updates may take 24-48 hours to propagate.
- File must be accessible without redirects at the exact `.well-known` path.

---

## 3. Android Configuration: App Links

### AndroidManifest.xml Intent Filters

```xml
<intent-filter android:autoVerify="true">
  <action android:name="android.intent.action.VIEW" />
  <category android:name="android.intent.category.DEFAULT" />
  <category android:name="android.intent.category.BROWSABLE" />
  <data android:scheme="https" android:host="app.cena.edu" />
</intent-filter>

<intent-filter>
  <action android:name="android.intent.action.VIEW" />
  <category android:name="android.intent.category.DEFAULT" />
  <category android:name="android.intent.category.BROWSABLE" />
  <data android:scheme="cena" android:host="*" />
</intent-filter>
```

### assetlinks.json (hosted at `https://app.cena.edu/.well-known/assetlinks.json`)

```json
[
  {
    "relation": ["delegate_permission/common.handle_all_urls"],
    "target": {
      "namespace": "android_app",
      "package_name": "edu.cena.app",
      "sha256_cert_fingerprints": [
        "AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99"
      ]
    }
  }
]
```

---

## 4. Push Notification Deep Links

### Notification Payload Structure

```json
{
  "notification": {
    "title": "Time to review!",
    "body": "Quadratic equations needs a quick review",
    "badge": 1
  },
  "data": {
    "type": "ReviewDue",
    "url": "cena://session/quad-equations-01/review",
    "concept_id": "quad-equations-01",
    "concept_name_he": "משוואות ריבועיות",
    "action": "open_session"
  }
}
```

### Notification Types and Deep Links

| Notification Type | Deep Link | Trigger |
|------------------|-----------|---------|
| `ReviewDue` | `cena://session/{conceptId}/review` | HLR recall below threshold |
| `StreakWarning` | `cena://profile/streak` | Streak expiring in 4 hours |
| `SessionReminder` | `cena://session/{conceptId}` | Daily session reminder |
| `TrialEnding` | `cena://upgrade?source=trial_ending` | Trial expires in 2 days |
| `MasteryAchieved` | `cena://graph/{conceptId}` | Concept mastered |
| `QuizReady` | `cena://quiz/{quizId}` | WhatsApp quiz also sent via push |
| `ParentReport` | `cena://analytics/{studentId}` | Weekly parent summary |

---

## 5. Web Fallback

When a deep link is opened but the app is not installed:

### Fallback Chain

1. Universal link opens in browser (app not installed).
2. Web page at `https://app.cena.edu/{path}` detects no app handler.
3. Display interstitial page with:
   - "Open in Cena app" button (re-tries deep link with `cena://` scheme).
   - "Download Cena" buttons linking to App Store / Google Play.
   - Brief preview of the content (concept name, session type).
4. Store the intended deep link path in a cookie/localStorage.
5. After app install and first open: read stored path and navigate.

### Smart Banner (iOS Safari)

```html
<meta name="apple-itunes-app"
  content="app-id=123456789, app-argument=cena://session/quad-equations-01/review">
```

---

## 6. State Restoration

When a deep link targets a learning session, the app must restore state intelligently.

### Restoration Rules

| Scenario | Behavior |
|----------|----------|
| Deep link to session, no prior session | Start new session for the concept |
| Deep link to session, prior session exists (< 30 min old) | Resume from last question (not restart) |
| Deep link to session, prior session exists (> 30 min old) | Start new session (old session timed out) |
| Deep link to review, no review scheduled | Show "No review needed" message |
| Deep link to quiz, quiz expired | Show "This quiz has expired" message |
| Deep link to analytics, user is STUDENT | Redirect to own profile (students cannot view analytics) |

### Session Restoration Data

On resume, load from local storage or server:

```json
{
  "session_id": "sess_abc123",
  "concept_id": "quad-equations-01",
  "question_index": 3,
  "mastery_at_start": 0.62,
  "dialogue_history": ["...last 5 turns..."],
  "methodology": "socratic",
  "started_at": "2026-03-26T14:00:00Z",
  "last_activity_at": "2026-03-26T14:12:00Z"
}
```

---

## 7. Deep Link Handler Implementation

### React Native Navigation

```
App Launch / Deep Link Received
  ├── Parse URL (custom scheme or universal link)
  ├── Extract route + parameters
  ├── Check auth state
  │   ├── Not authenticated → Navigate to login, store pending deep link
  │   └── Authenticated → Check permissions
  │       ├── Insufficient permissions → Show error toast
  │       └── Permitted → Navigate to target screen with params
  └── Handle invalid/unknown routes → Navigate to home screen
```

### Deferred Deep Links (post-install)

1. User clicks deep link, app not installed.
2. User installs app from store.
3. On first launch, check for deferred deep link via:
   - Firebase Dynamic Links (if used), or
   - Clipboard check (iOS: `UIPasteboard` with user consent), or
   - Server-side: match install attribution to original link click.
4. Navigate to intended screen after onboarding completes.

---

## 8. Testing

| Test Case | Expected |
|-----------|----------|
| `cena://session/concept-123` with app installed | Opens session screen for concept-123 |
| `https://app.cena.edu/session/concept-123` with app installed | Opens session screen (universal link) |
| `https://app.cena.edu/session/concept-123` without app | Shows web fallback with download links |
| Push notification tap with `ReviewDue` type | Opens review session for specified concept |
| Deep link while not authenticated | Login screen, then navigate to target |
| Deep link to nonexistent concept | Error message, navigate to home |
| Deep link to analytics as STUDENT role | Redirect to own profile |
| Resume session via deep link (< 30 min) | Resumes from last question |

---

## 9. Analytics

Track deep link usage for conversion and engagement analysis.

| Event | Properties |
|-------|------------|
| `deep_link_opened` | `source` (push/whatsapp/web/qr), `path`, `concept_id`, `app_installed` |
| `deep_link_fallback` | `path`, `action` (download/web_open) |
| `deep_link_deferred` | `path`, `days_to_install` |
| `session_resumed_via_deeplink` | `concept_id`, `question_index`, `time_since_last_activity` |
