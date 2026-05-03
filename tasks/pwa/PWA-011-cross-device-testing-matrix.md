# PWA-011: Cross-Device Testing Matrix + CI Integration

## Goal
Build a comprehensive testing infrastructure that verifies the PWA works correctly across the target device/browser matrix. Not "run tests on one browser" — a real matrix that catches the iOS Safari bugs, the Android Chrome quirks, and the keyboard behavior differences that break real students' sessions.

## Context
- PWA targets: iOS Safari 16.4+, Android Chrome, desktop Chrome/Firefox/Edge
- Israeli student device demographics: ~60% Android (Samsung Galaxy A-series dominant), ~35% iPhone (SE/14/15), ~5% iPad
- Critical interactions: math input with virtual keyboard, camera capture, figure touch, SignalR reconnect
- All previous PWA tasks (001-010) produce testable features that need cross-device verification

## Scope of Work

### 1. Playwright Test Suite — PWA-Specific
Create `tests/pwa/` directory:

```
tests/pwa/
├── service-worker.spec.ts     — SW registration, caching strategies, update lifecycle
├── manifest.spec.ts           — manifest validation, meta tags, install prompt
├── offline.spec.ts            — offline question cache, draft persistence, reconnect
├── camera.spec.ts             — photo capture flow (mocked getUserMedia)
├── figures-mobile.spec.ts     — figure rendering at mobile viewports
├── responsive.spec.ts         — layout at 375px, 768px, 1024px, 1440px
├── rtl.spec.ts                — RTL layout verification (Arabic + Hebrew)
├── push.spec.ts               — push subscription flow (mocked PushManager)
├── keyboard.spec.ts           — virtual keyboard handling
└── accessibility.spec.ts      — axe-core audit at each breakpoint
```

### 2. Device Emulation Matrix (Playwright)
Configure Playwright projects for:

| Device | Viewport | User Agent | Touch | Notes |
|--------|----------|------------|-------|-------|
| iPhone SE | 375×667 | iOS Safari 16 | Yes | Smallest target device |
| iPhone 14 Pro | 393×852 | iOS Safari 17 | Yes | Dynamic Island |
| iPhone 15 | 393×852 | iOS Safari 18 | Yes | Current flagship |
| Samsung Galaxy A14 | 412×915 | Chrome Android | Yes | Budget, most common |
| Pixel 7 | 412×915 | Chrome Android | Yes | Reference Android |
| iPad 10th gen | 810×1080 | iOS Safari 17 | Yes | Tablet |
| Desktop Chrome | 1440×900 | Chrome desktop | No | Standard desktop |
| Desktop Firefox | 1440×900 | Firefox | No | Firefox testing |

### 3. Lighthouse CI Integration
Add Lighthouse CI to GitHub Actions:

```yaml
# .github/workflows/lighthouse.yml
- name: Lighthouse CI
  uses: nickvdp/nickvdp-nickvdp@v1
  with:
    urls: |
      http://localhost:5173/
      http://localhost:5173/session
      http://localhost:5173/mastery
    budgetPath: .lighthouserc.json
```

Budget file (`.lighthouserc.json`):
```json
{
  "ci": {
    "assert": {
      "assertions": {
        "categories:performance": ["error", { "minScore": 0.8 }],
        "categories:accessibility": ["error", { "minScore": 0.95 }],
        "categories:best-practices": ["error", { "minScore": 0.9 }],
        "categories:pwa": ["error", { "minScore": 1.0 }]
      }
    }
  }
}
```

### 4. Accessibility Testing (axe-core)
Integrate `@axe-core/playwright` into every responsive test:

```typescript
import AxeBuilder from '@axe-core/playwright';

test('session page accessible at mobile viewport', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 667 });
  await page.goto('/session');
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa'])
    .analyze();
  expect(results.violations).toEqual([]);
});
```

Run at every breakpoint, in both Arabic and Hebrew.

### 5. Visual Regression Testing
Use Playwright visual comparison:

```typescript
test('session page visual regression - mobile AR', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 667 });
  await page.emulateMedia({ colorScheme: 'light' });
  // Set Arabic locale
  await page.goto('/session?lang=ar');
  await expect(page).toHaveScreenshot('session-mobile-ar.png', {
    maxDiffPixelRatio: 0.01
  });
});
```

Generate golden screenshots for: 4 breakpoints × 3 pages × 2 languages = 24 baselines.

### 6. Performance Budget Testing
Create `tests/pwa/performance.spec.ts`:

| Metric | Budget | How |
|--------|--------|-----|
| First Contentful Paint | < 1.5s | Lighthouse CI |
| Largest Contentful Paint | < 2.5s | Lighthouse CI |
| Total Blocking Time | < 200ms | Lighthouse CI |
| Cumulative Layout Shift | < 0.1 | Lighthouse CI |
| Bundle size (initial) | < 200KB gzipped | Build output check |
| Bundle size (total lazy) | < 800KB gzipped | Build output check |
| Service Worker install | < 3s | Playwright timing |

### 7. Manual Testing Checklist
Create `tests/pwa/MANUAL_TEST_CHECKLIST.md`:

A structured checklist for real-device testing that cannot be automated:
- Camera capture on real iOS/Android
- Push notification delivery on real devices
- Virtual keyboard behavior during math input
- FBD Construct drag precision with actual fingers
- Install-to-home-screen flow on real iOS Safari
- Offline → online transition on real network
- Performance on budget Android (Galaxy A14)

Each checklist item has: steps, expected result, pass/fail, device, tester, date.

### 8. CI Pipeline Configuration
Update `.github/workflows/` to run the full matrix on every PR:

```yaml
strategy:
  matrix:
    project: [mobile-chrome, mobile-safari, tablet, desktop-chrome, desktop-firefox]
```

- Run on every PR that touches `src/student/full-version/`
- Fail the PR if any test fails
- Upload Lighthouse reports and visual regression diffs as artifacts
- Cache Playwright browsers between runs

## Files to Create/Modify
- `tests/pwa/` — 10 test files
- `tests/pwa/MANUAL_TEST_CHECKLIST.md` — manual testing guide
- `.lighthouserc.json` — Lighthouse budgets
- `playwright.config.ts` — add PWA projects to device matrix
- `.github/workflows/pwa-tests.yml` — CI pipeline

## Non-Negotiables
- **axe-core must pass at every breakpoint** — accessibility is not optional
- **Lighthouse PWA score = 100** — anything less means the PWA is broken
- **Visual regression baselines must include Arabic** — RTL regressions are invisible in LTR-only tests
- **Performance budgets are CI gates** — a PR that regresses LCP from 2s to 4s must not merge
- **Manual testing checklist is maintained** — it's a living document, updated as new features ship

## Acceptance Criteria
- [ ] 10 Playwright test files covering all PWA features
- [ ] 8-device emulation matrix configured in playwright.config.ts
- [ ] Lighthouse CI integrated — PWA score 100, accessibility ≥ 95
- [ ] axe-core runs at 4 breakpoints × 2 languages = 8 configurations
- [ ] 24 visual regression baselines generated and committed
- [ ] Performance budgets enforced in CI
- [ ] Manual testing checklist created with ~30 items
- [ ] CI pipeline runs on every PR touching student app

## Testing Requirements
- This task IS the testing infrastructure — test it by running the full matrix against the current student app state

## DoD
- PR merged to `main`
- CI pipeline green on all matrix projects
- Lighthouse report attached (scores meeting budgets)
- Visual regression baselines committed
- Manual testing checklist reviewed by at least one other team member

## Reporting
Complete with: `branch=<worker>/<task-id>-pwa-testing-matrix,test_files=<n>,device_projects=<n>,lighthouse_pwa=<n>`
