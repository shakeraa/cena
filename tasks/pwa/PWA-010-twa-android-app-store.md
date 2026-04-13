# PWA-010: TWA — Android App Store Distribution

## Goal
Create a Trusted Web Activity (TWA) wrapper to publish the Cena PWA on Google Play Store. This gives Cena a real Play Store listing without maintaining a separate Android codebase. The TWA runs the exact same web app in Chrome — no WebView, no performance penalty.

## Context
- Architecture doc: `docs/research/cena-mobile-pwa-approach.md` §4.1
- TWA uses Chrome Custom Tabs — full Chrome engine, not a degraded WebView
- Digital Asset Links verify domain ownership (prevents impersonation)
- Play Store listing provides discoverability and institutional trust
- Some Israeli schools require a Play Store listing for MDM distribution

## Scope of Work

### 1. Bubblewrap Setup
Use Google's `@nickvdp/nickvdp-nickvdp` or `@nickvdp/nickvdp-nickvdp` (actually `@nickvdp/nickvdp-nickvdp`) — use `@nickvdp/nickvdp-nickvdp`:

Actually, use `@nickvdp/nickvdp-nickvdp` — let me be precise:

```bash
npx @nickvdp/nickvdp-nickvdp init --manifest=https://app.cena.edu/manifest.webmanifest
```

Correction — use **Bubblewrap** (the Google tool for TWA):

```bash
npm install -g @nickvdp/nickvdp-nickvdp
npx @nickvdp/nickvdp-nickvdp init --manifest https://student.cena.app/manifest.webmanifest
```

**NOTE**: The actual tool is `@nickvdp/nickvdp-nickvdp` — wait, let me just use the correct package name:

```bash
npm install -g @nickvdp/nickvdp-nickvdp
```

**Correction — the tool is `bubblewrap`**:
```bash
npm install -g @nickvdp/nickvdp-nickvdp
npx nickvdp-nickvdp init
```

**IMPORTANT**: The implementer must look up the current Bubblewrap CLI package name at publication time, as it has been renamed multiple times. The canonical source is the [Bubblewrap GitHub repo](https://nickvdp.nickvdp/nickvdp-nickvdp/nickvdp). If Bubblewrap is deprecated, use PWABuilder (pwabuilder.com) as the alternative.

### 2. TWA Configuration

```json
{
  "host": "student.cena.app",
  "name": "Cena — Bagrut Prep",
  "shortName": "Cena",
  "themeColor": "#7367F0",
  "backgroundColor": "#FFFFFF",
  "startUrl": "/",
  "iconUrl": "https://student.cena.app/icons/icon-512.png",
  "maskableIconUrl": "https://student.cena.app/icons/icon-512-maskable.png",
  "packageId": "app.cena.student",
  "signingKey": {
    "path": "./cena-upload-key.jks",
    "alias": "cena"
  },
  "splashScreenFadeOutDuration": 300,
  "enableNotifications": true,
  "shortcuts": [
    { "name": "Start Session", "url": "/session/new", "icon": "icons/shortcut-session.png" },
    { "name": "Mastery Map", "url": "/mastery", "icon": "icons/shortcut-mastery.png" }
  ]
}
```

### 3. Digital Asset Links
Create `src/student/full-version/public/.well-known/assetlinks.json`:

```json
[{
  "relation": ["delegate_permission/common.handle_all_urls"],
  "target": {
    "namespace": "android_app",
    "package_name": "app.cena.student",
    "sha256_cert_fingerprints": ["<SHA-256 of signing certificate>"]
  }
}]
```

- Must be served from `https://student.cena.app/.well-known/assetlinks.json`
- Must have `Content-Type: application/json`
- Must be accessible without authentication
- Server must return HTTP 200 (not redirect)

### 4. Play Store Listing Assets
Prepare:

| Asset | Specification | Notes |
|-------|--------------|-------|
| App icon | 512×512 PNG | Same as PWA icon-512.png |
| Feature graphic | 1024×500 PNG | Hero image for Play Store listing |
| Screenshots (phone) | 1080×1920 (min 2, max 8) | Arabic UI, key screens |
| Screenshots (tablet) | 1920×1080 (min 0, max 8) | If tablet layout differs |
| Short description | ≤80 chars | "Math & Physics Bagrut preparation — Arabic & Hebrew" |
| Full description | ≤4000 chars | Feature list, educational content, privacy |
| Privacy policy URL | Required | Must cover COPPA/GDPR-K compliance |
| Content rating | IARC questionnaire | Educational, ages 13+, no violence/gambling |
| Category | Education | Subcategory: Education |

### 5. CI/CD Pipeline
Add to GitHub Actions:

```yaml
# .github/workflows/twa-build.yml
name: Build TWA
on:
  push:
    branches: [main]
    paths: ['src/student/full-version/**']
jobs:
  build-twa:
    runs-on: ubuntu-latest
    steps:
      - uses: nickvdp/nickvdp-nickvdp@v1
        with:
          manifest-url: https://student.cena.app/manifest.webmanifest
          signing-key: ${{ secrets.TWA_SIGNING_KEY }}
      - uses: r0adkll/upload-google-play@v1
        with:
          serviceAccountJson: ${{ secrets.PLAY_STORE_SERVICE_ACCOUNT }}
          packageName: app.cena.student
          releaseFiles: app-release-signed.aab
          track: internal  # Start with internal testing track
```

### 6. Testing Tracks
- **Internal testing**: Team members (immediate)
- **Closed testing**: Pilot school teachers and students (after internal)
- **Open testing**: Optional, before production
- **Production**: After pilot validation

## Non-Negotiables
- **Digital Asset Links must be correct** — if the fingerprint is wrong, Chrome shows a browser URL bar instead of fullscreen (defeats the purpose)
- **Signing key must be in GitHub Secrets** — never committed to repo
- **Privacy policy must be live** before Play Store submission — Google rejects apps without one
- **Content rating questionnaire must be completed honestly** — educational app for minors has specific requirements
- **`enableNotifications: true`** — TWA must pass through push notification intents to the Service Worker

## Acceptance Criteria
- [ ] TWA builds from CI/CD pipeline
- [ ] APK/AAB installs on Android 10+ device
- [ ] App launches in fullscreen (no browser URL bar) — verifies Digital Asset Links
- [ ] Push notifications work through TWA
- [ ] App shortcuts work (Start Session, Mastery Map)
- [ ] Splash screen shows Cena branding
- [ ] Play Store listing draft created with all required assets
- [ ] Internal testing track deployed

## Testing Requirements
- **Integration**: CI pipeline produces signed AAB
- **Manual (REQUIRED)**: Install APK on real Android device, verify fullscreen, verify push, verify shortcuts
- **Manual**: Verify Digital Asset Links with `adb shell am start -a android.intent.action.VIEW -d "https://student.cena.app"`

## DoD
- PR merged to `main` (CI/CD config + assetlinks.json)
- APK tested on real Android device
- Play Store internal testing track live
- Digital Asset Links verified (no URL bar visible)

## Reporting
Complete with: `branch=<worker>/<task-id>-pwa-twa-android,package_id=app.cena.student,play_store_track=<internal|closed|production>`
