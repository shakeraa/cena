# Cena Mobile: PWA Approach

> **Date**: 2026-04-13
> **Status**: Proposed
> **Related**: [cena-question-engine-architecture-2026-04-12.md](cena-question-engine-architecture-2026-04-12.md) §42.6

## 1. Overview

Build the Vue 3 student web app as a Progressive Web App (PWA) that serves both desktop browsers and mobile devices. No separate native mobile codebase.

**Core thesis**: Every rendering library in Cena's stack (KaTeX, function-plot.js, JSXGraph, D3/SVG) is browser-native. A PWA runs the identical code on every device — one codebase, one renderer, one CI pipeline.

## 2. Architecture

```
┌─────────────────────────────────────────────────────┐
│              STUDENT DEVICES                         │
│  Vue 3 PWA (Web + Mobile)                           │
│  KaTeX · function-plot.js · JSXGraph · SVG          │
│  Service Worker · Web App Manifest · IndexedDB      │
└──────────────────┬──────────────────────────────────┘
                   │ HTTPS / WSS (TLS 1.3)
                   │ Firebase Auth JWT
                   ▼
┌─────────────────────────────────────────────────────┐
│              EDGE / GATEWAY                          │
│  NGINX (rate limit, geo-fence IL, TLS term)          │
└──────────────────┬──────────────────────────────────┘
                   │
    ┌──────────────┼──────────────┐
    ▼              ▼              ▼
┌──────────┐ ┌──────────┐ ┌─────────────┐
│Student API│ │ Admin API│ │ Actor Host  │
│ (.NET 8) │ │ (.NET 8) │ │(Proto.Actor)│
└──────────┘ └──────────┘ └─────────────┘
```

### 2.1 PWA Infrastructure

| Component | Implementation | Notes |
|-----------|---------------|-------|
| Service Worker | Workbox (via `vite-plugin-pwa`) | Cache-first for static assets, network-first for API |
| App Manifest | `manifest.webmanifest` | `display: standalone`, theme color #7367F0, icons 192/512 |
| Offline cache | Service Worker + IndexedDB | Question cache (last 20 questions), session state, BKT snapshot |
| Install prompt | `beforeinstallprompt` event | Custom install banner after 2nd visit |
| Updates | Service Worker `skipWaiting` + user prompt | "New version available — reload?" |

### 2.2 Offline Strategy

| Data | Storage | Sync |
|------|---------|------|
| Current session state | IndexedDB | SignalR reconnect sends full snapshot |
| Draft step input | localStorage (debounced 2s) | Restored on reload, cleared on submit |
| Question cache | IndexedDB (last 20 questions) | Background sync when online |
| BKT mastery snapshot | IndexedDB | Refreshed on session start |
| Figure assets | Service Worker cache | Cache-first, 7-day TTL |

**Offline capability**: Students can review cached questions and their mastery map. New question requests require connectivity (CAS verification is server-side). Draft inputs are preserved across network drops.

### 2.3 Camera Access (Photo Ingestion)

```javascript
// getUserMedia for camera capture
const stream = await navigator.mediaDevices.getUserMedia({
  video: { facingMode: 'environment', width: { ideal: 1920 } }
});

// Alternative: file input with capture attribute
<input type="file" accept="image/*" capture="environment" />
```

Both approaches work on iOS Safari 16+ and Android Chrome. The ephemeral image pipeline (1.5s volatile, no disk — §27) operates identically — the image is uploaded via `fetch()`, processed server-side, and never stored on the client beyond the upload buffer.

### 2.4 RTL & Bidi (Unchanged)

All RTL/bidi engineering (§23) works identically in a PWA:
- `<bdi dir="ltr">` for inline KaTeX
- CSS logical properties (`margin-inline-start` etc.)
- SVG text auto-detect script direction
- Eastern Arabic digit normalization

No platform-specific RTL work needed — it's all CSS/HTML.

## 3. Capabilities Matrix

| Capability | PWA Support | Cena Requirement | Status |
|------------|------------|-------------------|--------|
| Offline content review | Service Worker + IndexedDB | Review cached questions when offline | ✅ Full |
| Camera access | `getUserMedia` / `<input capture>` | Photo ingestion pipeline | ✅ Full |
| Push notifications | Web Push API | Session reminders, assignment alerts | ⚠️ Partial (iOS limitations) |
| Install to home screen | Web App Manifest | Native-like launch experience | ✅ Full |
| Touch gestures | Pointer Events API | FBD Construct mode (drag force arrows) | ✅ Full |
| Fullscreen | Fullscreen API | Exam simulation mode | ✅ Full |
| Background sync | Background Sync API | Queue submissions during offline | ✅ Full (Android), ⚠️ iOS limited |
| Biometric auth | WebAuthn | Not needed (Firebase Auth via email/phone) | N/A |
| File system access | Not available | Not needed | N/A |
| Haptic feedback | Vibration API | Nice-to-have for correct/incorrect | ✅ Android, ❌ iOS |

### 3.1 iOS Push Notification Limitations

Since iOS 16.4 (March 2023), Safari supports Web Push for installed PWAs. Limitations:
- No badge count on app icon
- No silent push (background refresh)
- User must explicitly enable in Settings → Notifications
- Requires the PWA to be added to Home Screen (not just a bookmark)

**Impact on Cena**: Low. Session reminders and assignment alerts are supplementary. Critical notifications (exam dates, teacher messages) can use email or SMS as fallback. The tutoring session itself is interactive — push is not on the critical path.

## 4. App Store Presence

### 4.1 Google Play — TWA (Trusted Web Activity)

A TWA wraps the PWA in a thin Android shell using Chrome Custom Tabs. No WebView, no performance penalty. The Play Store listing is a real app entry.

```
Bubblewrap CLI → generates Android project → signs APK → publishes to Play Store
```

- Full Chrome engine (not a degraded WebView)
- Digital Asset Links verify domain ownership
- Play Store listing with screenshots, description, ratings
- Auto-updates when the PWA updates (no app store review cycle for content changes)
- Effort: ~2 days to set up, then automated

### 4.2 iOS App Store — Not Available via PWA

Apple does not allow PWA wrappers in the App Store. The only options:
1. **Safari "Add to Home Screen"** — works, but no App Store discoverability
2. **Capacitor/Ionic shell** — wraps in WKWebView, can be submitted to App Store, but Apple may reject if it's "just a website"
3. **Accept no iOS App Store presence** — students install from Safari

**Recommendation for pilot**: Safari "Add to Home Screen" with a guided install flow in the onboarding. Evaluate App Store need based on pilot feedback. Israeli schools distribute apps via MDM or direct links, not App Store search.

## 5. Performance Considerations

### 5.1 Rendering Performance

All figure libraries are already optimized for mobile browsers:
- **KaTeX**: <10ms render for typical expressions
- **function-plot.js**: WebGL acceleration where available, Canvas fallback
- **JSXGraph**: Touch-optimized, responsive canvas
- **D3/SVG**: Lightweight for physics diagrams (typically <50 elements)

### 5.2 Bundle Size

| Component | Size (gzipped) | Notes |
|-----------|---------------|-------|
| Vue 3 + Router + Pinia | ~35 KB | Tree-shaken |
| KaTeX | ~85 KB | CSS + fonts loaded on demand |
| function-plot.js | ~45 KB | With d3 subset |
| JSXGraph | ~250 KB | Lazy-loaded for geometry questions only |
| MathLive (if used) | ~300 KB | Lazy-loaded for step input |
| Workbox runtime | ~5 KB | Service Worker |
| **Total initial load** | **~170 KB** | Without lazy-loaded libs |
| **Total with all libs** | **~720 KB** | After lazy loading completes |

With Service Worker caching, the initial load happens once. Subsequent visits load from cache in <100ms.

### 5.3 Mobile-Specific Optimizations

- **Viewport meta**: `<meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no">` (prevent zoom on input focus)
- **Touch targets**: Minimum 44×44px for all interactive elements (WCAG 2.5.5)
- **Virtual keyboard handling**: `visualViewport` API to adjust layout when keyboard appears (critical for step input)
- **Reduced motion**: `prefers-reduced-motion` media query for animations
- **Safe area insets**: `env(safe-area-inset-*)` for notched devices

## 6. Development & Deployment Impact

### 6.1 What Changes from Current Plan

| Aspect | Before (Vue 3 + Flutter) | After (PWA only) |
|--------|--------------------------|-------------------|
| Codebases | 2 (Vue + Flutter/Dart) | 1 (Vue) |
| CI pipelines | 2 (web + mobile) | 1 (web) |
| Figure rendering | Must match across 2 engines | One engine, guaranteed parity |
| RTL testing | 2 platforms | 1 platform |
| Developers needed | Vue + Flutter devs | Vue devs only |
| App Store updates | Review cycle per release | Instant (Service Worker update) |
| Improvement #48 | Pixel-diff CI required | **Eliminated entirely** |

### 6.2 8-Week Critical Path Impact

No Flutter work means weeks 7-8 (which would have included Flutter integration) are fully available for:
- Content authoring (the real bottleneck — 10 seed questions vs 700 needed)
- IRT calibration data collection
- Pilot school onboarding and teacher training
- Exam simulation mode

### 6.3 Cost Impact

| Item | Vue + Flutter | PWA Only | Savings |
|------|---------------|----------|---------|
| Flutter developer (8 weeks) | $15K-25K | $0 | $15K-25K |
| iOS App Store ($99/yr) | $99 | $0 | $99 |
| Google Play ($25 one-time) | $25 | $25 (TWA) | $0 |
| Dual CI (GitHub Actions) | ~$50/mo | ~$25/mo | ~$25/mo |
| Cross-platform test infra | ~$200/mo (BrowserStack) | ~$100/mo | ~$100/mo |

## 7. Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| School IT blocks PWA install | Low | High | Provide TWA for Android; work with school IT on allowlisting |
| iOS Safari rendering bugs | Medium | Medium | Test on real devices (iPhone SE, iPad); Safari-specific CSS fixes |
| Parent expectation of "real app" | Medium | Low | Guided install flow; TWA for Play Store presence |
| Apple restricts PWA capabilities further | Low | Medium | Capacitor shell as escape hatch (2-week migration) |
| Older devices (iOS 14, Android 8) | Low | Low | Minimum: iOS 15+, Android 10+ (covers 95%+ of Israeli market) |

## 8. Escape Hatch

If PWA proves insufficient during or after pilot (e.g., school requires App Store, iOS push is critical):

**Capacitor migration** — wrap the existing Vue 3 app in a Capacitor shell. This gives:
- Native App Store distribution (iOS + Android)
- Native push notifications
- Access to native APIs (if needed)
- Same Vue 3 codebase — no rewrite

Estimated effort: 2 weeks. The Vue 3 app is the same; only the shell changes. This is dramatically cheaper than maintaining a parallel Flutter codebase.

## 9. Recommendation

**Go PWA for pilot.** The advantages are structural:
1. Eliminates cross-platform rendering divergence (Improvement #48)
2. Halves RTL/bidi testing surface
3. Removes Flutter from the 8-week critical path
4. Single codebase, single CI, single deployment
5. Camera, offline, install-to-home-screen all work
6. Capacitor escape hatch available if needed post-pilot

The only meaningful trade-off is iOS App Store absence, which is mitigable via Safari install flow for pilot and Capacitor shell for scale if needed.
