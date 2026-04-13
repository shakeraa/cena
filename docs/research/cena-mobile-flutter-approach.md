# Cena Mobile: Flutter Approach

> **Date**: 2026-04-13
> **Status**: Alternative (original decision)
> **Related**: [cena-question-engine-architecture-2026-04-12.md](cena-question-engine-architecture-2026-04-12.md) §42.6

## 1. Overview

Build a native mobile app using Flutter (Dart) alongside the Vue 3 web student app. Two separate codebases targeting the same backend APIs.

**Core thesis**: Flutter provides native performance, full platform API access, and App Store distribution — at the cost of maintaining a parallel codebase and solving cross-platform rendering parity.

## 2. Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                    STUDENT DEVICES                            │
│  Vue 3 (Web)              ←→        Flutter (iOS/Android)    │
│  KaTeX · function-plot.js           flutter_math · CustomPaint│
│  JSXGraph · SVG                     flutter_svg · Canvas      │
└───────────┬──────────────────────────────┬───────────────────┘
            │                              │
            │     HTTPS / WSS (TLS 1.3)    │
            │     Firebase Auth JWT        │
            ▼                              ▼
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

### 2.1 Flutter Rendering Stack

The web app uses browser-native libraries. Flutter cannot use any of them — it needs parallel implementations:

| Web Library | Flutter Equivalent | Parity Risk |
|------------|-------------------|-------------|
| KaTeX | `flutter_math_fork` | Medium — subset of KaTeX commands, RTL support unclear |
| function-plot.js | `fl_chart` or `CustomPaint` | High — no direct equivalent, must reimplement |
| JSXGraph | `CustomPaint` (manual) | High — interactive geometry from scratch |
| D3/SVG (physics) | `flutter_svg` + `CustomPaint` | Medium — static SVG ok, interactive needs work |
| MathLive | Custom `TextField` + `flutter_math` | High — no equivalent math input widget |
| CSS RTL (`dir`, logical properties) | `Directionality` widget | Low — Flutter has good RTL support |

### 2.2 Flutter-Specific Components

```
Flutter App
├── lib/
│   ├── features/
│   │   ├── session/        — Learning session UI
│   │   ├── step_solver/    — Step-by-step solver
│   │   ├── figures/        — Figure rendering (CustomPaint)
│   │   ├── photo/          — Camera capture + upload
│   │   ├── mastery/        — Mastery map
│   │   └── exam_sim/       — Exam simulation mode
│   │
│   ├── core/
│   │   ├── api/            — REST client (dio)
│   │   ├── realtime/       — SignalR client (signalr_netcore)
│   │   ├── auth/           — Firebase Auth
│   │   ├── storage/        — Hive/Isar for offline
│   │   └── l10n/           — Arabic/Hebrew localization
│   │
│   └── shared/
│       ├── math_renderer/  — flutter_math wrapper
│       ├── rtl/            — RTL utilities
│       └── theme/          — Vuexy-aligned theme
```

### 2.3 Offline Strategy

| Data | Storage | Sync |
|------|---------|------|
| Current session state | Hive/Isar | API sync on reconnect |
| Draft step input | In-memory + Hive | Restored on app resume |
| Question cache | Isar (last 20 questions) | Background fetch |
| BKT mastery snapshot | Hive | Refreshed on session start |
| Figure data | Isar + file cache | Pre-rendered on download |

Flutter has full offline capabilities via native storage APIs. Background sync works natively on both iOS and Android.

### 2.4 Camera Access (Photo Ingestion)

```dart
// Native camera access via image_picker
final XFile? photo = await ImagePicker().pickImage(
  source: ImageSource.camera,
  preferredCameraDevice: CameraDevice.rear,
  maxWidth: 1920,
  imageQuality: 85,
);
```

Native camera access is mature and reliable. The `camera` package provides lower-level control if needed (live preview, custom overlay for alignment guides).

## 3. Capabilities Matrix

| Capability | Flutter Support | Cena Requirement | Status |
|------------|----------------|-------------------|--------|
| Offline content review | Hive/Isar | Review cached questions when offline | ✅ Full |
| Camera access | `image_picker` / `camera` | Photo ingestion pipeline | ✅ Full (superior) |
| Push notifications | Firebase Cloud Messaging | Session reminders, assignment alerts | ✅ Full (both platforms) |
| App Store presence | Native builds | iOS App Store + Google Play | ✅ Full |
| Touch gestures | `GestureDetector` | FBD Construct mode | ✅ Full |
| Background sync | Native APIs | Queue submissions during offline | ✅ Full |
| Biometric auth | `local_auth` | Not needed | ✅ Available |
| Haptic feedback | `HapticFeedback` | Correct/incorrect feedback | ✅ Full (both platforms) |

## 4. Cross-Platform Rendering Parity (Improvement #48)

This is the central challenge of the Flutter approach. Two different rendering engines must produce visually identical output for every question type.

### 4.1 The Problem

- **KaTeX vs flutter_math**: Different font metrics, different line-breaking, different spacing for complex expressions
- **function-plot.js vs fl_chart**: Different curve sampling, different axis rendering, different interaction models
- **JSXGraph vs CustomPaint**: JSXGraph is a mature geometry library; CustomPaint is a raw drawing API — everything must be reimplemented
- **SVG vs flutter_svg**: Static rendering works; interactive SVG (click, drag) needs custom implementation

### 4.2 The Solution (from Rami's Review)

```
CI Pipeline:
├── Playwright → screenshot web figures → golden images
├── Flutter integration test → screenshot Flutter figures → test images
├── Pixel-diff comparison (tolerance: 2% SSIM)
└── Fail build if any figure diverges beyond threshold
```

This works but adds:
- ~15 minutes to CI pipeline per run
- Maintenance burden when any figure library updates
- False positives from anti-aliasing differences across platforms
- A dedicated engineer to triage rendering divergences

### 4.3 Scope of Work

Every figure type needs dual implementation:

| Figure Type | Web Implementation | Flutter Implementation | Effort |
|------------|-------------------|----------------------|--------|
| Function plots | function-plot.js (existing) | fl_chart + CustomPaint | 2 weeks |
| Geometry constructions | JSXGraph (existing) | CustomPaint from scratch | 3 weeks |
| Physics FBD (display) | D3/SVG (existing) | flutter_svg | 1 week |
| Physics FBD (construct) | D3 drag (existing) | GestureDetector + CustomPaint | 2 weeks |
| KaTeX rendering | KaTeX (existing) | flutter_math_fork | 1 week (integration) |
| Step solver UI | Vue components (existing) | Flutter widgets | 2 weeks |
| **Total** | **Already done** | **~11 weeks** | |

## 5. RTL & Bidi in Flutter

Flutter has solid RTL support via `Directionality` and `TextDirection`. However:

- `flutter_math_fork` RTL support is **untested for Arabic mathematical notation** — may need patches
- Custom `CustomPaint` figures need manual `TextDirection`-aware layout
- Eastern Arabic digit rendering works (Unicode), but digit direction in mixed expressions needs testing
- Arabic math terminology (synonym table from §22) needs equivalent input handling in Dart

**Estimated additional RTL effort**: 2-3 weeks beyond the base implementation, including testing on real Arabic/Hebrew devices.

## 6. Development & Deployment Impact

### 6.1 Team Requirements

| Role | Duration | Cost (estimated) |
|------|----------|-----------------|
| Senior Flutter developer | 12-16 weeks | $25K-40K |
| Flutter RTL/bidi specialist | 2-3 weeks | $5K-8K |
| QA (cross-platform) | Ongoing | $3K-5K/month |
| **Total initial build** | | **$33K-53K** |

### 6.2 Ongoing Maintenance

| Item | Monthly Cost | Notes |
|------|-------------|-------|
| Flutter developer (part-time) | $3K-5K | Bug fixes, OS updates, library updates |
| Dual CI pipeline | ~$100 | GitHub Actions (web + mobile builds) |
| BrowserStack / Firebase Test Lab | ~$200 | Cross-device testing |
| App Store fees | ~$10 | $99/yr iOS + $25 one-time Android |
| **Total ongoing** | **~$3.5K-5.5K/month** | |

### 6.3 8-Week Critical Path Impact

Flutter development **cannot fit in the 8-week critical path**. The 11-week figure reimplementation alone exceeds it. Realistic timeline:

- **Weeks 1-8**: Web app (Vue 3) — same as current plan
- **Weeks 9-20**: Flutter app development (parallel with web app content work)
- **Weeks 21-24**: Cross-platform QA, pixel-diff CI, App Store submission

**Flutter adds 12-16 weeks to mobile availability.** Students can use the web app on mobile browsers during this period, but without PWA features (offline, install).

## 7. Advantages Over PWA

| Advantage | Impact for Cena | Significance |
|-----------|----------------|-------------|
| Full push notifications (iOS) | Badge count, silent push, background refresh | Low — tutoring is session-based |
| App Store discoverability | Parents/schools find app in store | Medium — schools distribute via MDM/links |
| Native performance | 60fps guaranteed, no jank | Low — Cena's UI is not animation-heavy |
| Native camera (superior) | Better autofocus, exposure control | Low — basic capture is sufficient |
| Background processing | Offline CAS? (not in current architecture) | N/A — CAS is server-side |
| Biometric auth | Face ID / fingerprint login | Low — not needed for student auth |
| Haptic feedback | Vibration on correct/incorrect | Nice-to-have only |

## 8. Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| flutter_math RTL bugs | High | High | Fork and patch; significant effort |
| Figure rendering divergence | High | High | Pixel-diff CI (Improvement #48); ongoing maintenance |
| Flutter developer availability (IL market) | Medium | High | Remote hiring; or pivot to PWA |
| Library abandonment (flutter_math, signalr_netcore) | Medium | Medium | Fork and maintain; or pivot to PWA |
| App Store rejection (educational content review) | Low | High | Pre-submission review; age rating |
| Dart/Flutter breaking changes | Medium | Medium | Pin versions; test on Flutter stable channel |

## 9. Cost Comparison Summary

| Metric | PWA | Flutter | Delta |
|--------|-----|---------|-------|
| Initial build cost | $0 (part of web app) | $33K-53K | +$33K-53K |
| Monthly maintenance | ~$0 | ~$3.5K-5.5K | +$3.5K-5.5K/mo |
| Time to mobile | Week 1 (same as web) | Week 20+ | +12-16 weeks |
| Codebases | 1 | 2 | +1 |
| CI pipelines | 1 | 2 | +1 |
| Figure implementations | 1 | 2 | +1 |
| RTL test surfaces | 1 | 2 | +1 |
| Rendering parity risk | None | High (2.3%+ divergence) | Significant |
| iOS push notifications | Partial | Full | PWA weaker |
| App Store presence | Android only (TWA) | Both platforms | PWA weaker |

## 10. Recommendation

**Flutter is the right choice only if**:
1. Israeli schools require iOS App Store presence (not just "Add to Home Screen") — verify with pilot schools before committing
2. Full iOS push notifications are critical (unlikely for a tutoring app)
3. The project has budget and timeline for a 12-16 week parallel development effort

**If none of these conditions hold, PWA is the superior approach** — it eliminates an entire technology stack, removes the cross-platform rendering parity problem, and redirects 12-16 weeks of development effort to content authoring (the actual bottleneck).

**Escape hatch**: If PWA is chosen for pilot and Flutter proves necessary later, no work is lost. The Vue 3 web app continues to serve mobile. Flutter development can start in parallel at any time. The reverse (Flutter → PWA) would waste the Flutter investment.
