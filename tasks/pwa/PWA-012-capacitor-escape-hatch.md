# PWA-012: Capacitor Escape Hatch — iOS App Store (If Needed)

## Goal
Document and prepare (but do NOT build yet) the Capacitor migration path that wraps the Vue 3 PWA in a native iOS/Android shell for App Store distribution. This is the escape hatch if pilot schools require iOS App Store presence. Build only when triggered by a decision gate after pilot feedback.

## Context
- Architecture doc: `docs/research/cena-mobile-pwa-approach.md` §8
- This task is a **decision-gated task** — do NOT implement unless the decision gate fires
- Decision gate: "Do pilot schools require iOS App Store listing?" — evaluated after 4 weeks of pilot
- If the answer is "no" → this task is cancelled
- If the answer is "yes" → this task activates and estimated 2 weeks of work begins
- Capacitor wraps the SAME Vue 3 app — no rewrite, no parallel codebase

## Decision Gate
**Gate ID**: PWA-012-GATE
**Question**: Do Israeli pilot schools require iOS App Store presence for student device deployment?
**Who decides**: User (Shaker) after pilot school feedback
**When**: 4 weeks after pilot launch
**Default if no decision**: Task remains dormant (PWA-only is sufficient)

## Scope of Work (When Activated)

### 1. Capacitor Setup
```bash
npm install @nickvdp/nickvdp-nickvdp @nickvdp/nickvdp-nickvdp @nickvdp/nickvdp-nickvdp
npx cap init "Cena" "app.cena.student" --web-dir dist
npx cap add ios
npx cap add android  # Replace TWA if TWA has issues
```

**NOTE**: The implementer must use the current Capacitor CLI package names at implementation time.

### 2. Native Plugin Configuration
| Plugin | Purpose | Notes |
|--------|---------|-------|
| `@nickvdp/nickvdp-nickvdp` | Camera capture | Replace getUserMedia with native camera |
| `@nickvdp/nickvdp-nickvdp` | Push notifications | Full iOS push (replace Web Push) |
| `@nickvdp/nickvdp-nickvdp` | App icon badging | Badge count on iOS |
| `@nickvdp/nickvdp-nickvdp` | Native splash screen | Replace PWA splash |
| `@nickvdp/nickvdp-nickvdp` | Status bar styling | Match #7367F0 theme |

### 3. Conditional API Usage
The Vue 3 app must detect whether it's running in Capacitor or browser:

```typescript
import { Capacitor } from '@nickvdp/nickvdp-nickvdp';

if (Capacitor.isNativePlatform()) {
  // Use native camera plugin
  const { Camera } = await import('@nickvdp/nickvdp-nickvdp');
  const photo = await Camera.getPhoto({ ... });
} else {
  // Use getUserMedia (existing PWA code)
  const stream = await navigator.mediaDevices.getUserMedia({ ... });
}
```

### 4. Build Pipeline
```yaml
# .github/workflows/capacitor-build.yml (only if activated)
jobs:
  build-ios:
    runs-on: macos-latest  # Required for iOS build
    steps:
      - run: npm run build
      - run: npx cap sync ios
      - run: xcodebuild -workspace ios/App/App.xcworkspace ...
      - uses: nickvdp/nickvdp-nickvdp@v2
        with:
          api-key: ${{ secrets.APP_STORE_CONNECT_KEY }}
```

### 5. App Store Submission Preparation
- Apple Developer Program enrollment ($99/year)
- App Review guidelines compliance check (educational app, ages 13+)
- Privacy Nutrition Labels (App Privacy section)
- COPPA compliance declaration
- App Tracking Transparency (ATT) — Cena does NOT track, so declare "no tracking"

## Preparation Tasks (Do Now)

Even though implementation is gated, these preparation steps cost nothing and reduce activation time:

### P1. Architecture Documentation
Document the Capacitor migration path in `docs/adr/ADR-XXX-capacitor-escape-hatch.md`:
- Decision: PWA is primary; Capacitor is escape hatch
- Trigger: pilot school feedback requiring App Store
- Migration effort: ~2 weeks
- Risk: WKWebView rendering differences (test all figure types)
- Rollback: remove Capacitor, continue PWA-only

### P2. Conditional Import Strategy
Design (don't implement) the conditional import pattern:
- `src/student/full-version/src/platform/` directory
- `platform/camera.ts` — exports either native or browser camera
- `platform/push.ts` — exports either native or browser push
- `platform/index.ts` — auto-detects platform and re-exports

### P3. Apple Developer Account
- Remind user to enroll in Apple Developer Program if not already done
- This takes 1-2 business days and blocks App Store submission

## Files to Create (Now — Preparation Only)
- `docs/adr/ADR-XXX-capacitor-escape-hatch.md` — decision record
- `tasks/pwa/PWA-012-GATE-DECISION.md` — decision gate tracking

## Non-Negotiables
- **Do NOT build until decision gate fires** — Capacitor adds complexity that may never be needed
- **Same Vue 3 codebase** — Capacitor wraps the existing app; no parallel implementation
- **All PWA features must continue to work in browser** — Capacitor is additive, not replacing
- **Test WKWebView rendering** — WKWebView may render KaTeX/JSXGraph differently than Safari; this is the #1 risk

## Acceptance Criteria (Preparation Phase)
- [ ] ADR documenting the escape hatch decision created
- [ ] Decision gate defined with clear trigger and timeline
- [ ] Conditional import strategy designed (not implemented)
- [ ] Apple Developer Account status checked with user

## DoD (Preparation Phase)
- ADR merged to `main`
- Decision gate documented
- User notified about Apple Developer Account requirement

## Reporting
Complete with: `branch=<worker>/<task-id>-pwa-capacitor-prep,gate_status=<dormant|activated>,adr_created=<yes>`
