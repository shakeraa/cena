# WEB-001: React PWA Scaffold — Vite, TypeScript, PWA, Service Worker

**Priority:** P0 — all frontend tasks depend on this
**Blocked by:** Nothing (greenfield)
**Estimated effort:** 2 days
**Contract:** N/A (scaffolding)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
The Cena web client is a React PWA (Progressive Web App) targeting teachers and parents (students use the React Native mobile app). It uses Vite for builds, TypeScript for type safety, and a service worker for offline capability. This task creates the project skeleton with all tooling configured: Vite + React + TS, PWA manifest, service worker registration, React Testing Library, ESLint, and the folder structure.

## Subtasks

### WEB-001.1: Vite + React + TypeScript Project Setup
**Files:**
- `src/web/package.json` — dependencies
- `src/web/tsconfig.json` — strict TypeScript
- `src/web/vite.config.ts` — Vite config with PWA plugin
- `src/web/src/main.tsx` — entry point
- `src/web/src/App.tsx` — root component
- `src/web/index.html` — HTML shell
- `src/web/.eslintrc.cjs` — ESLint config

**Acceptance:**
- [ ] `npm create vite@latest` base with React + TypeScript template
- [ ] TypeScript strict mode: `strict: true`, `noUncheckedIndexedAccess: true`, `exactOptionalPropertyTypes: true`
- [ ] Path aliases: `@/` maps to `src/`
- [ ] Vite config: `vite-plugin-pwa` for service worker generation
- [ ] ESLint: `@typescript-eslint/strict-type-checked`, `react-hooks/exhaustive-deps`
- [ ] Folder structure:
  ```
  src/web/src/
    components/    # Shared UI components
    features/      # Feature modules (teacher/, parent/, auth/)
    hooks/         # Custom hooks
    store/         # Zustand slices
    services/      # API, SignalR, GraphQL clients
    types/         # Shared TypeScript types
    utils/         # Pure utility functions
    assets/        # Static assets
  ```
- [ ] `npm run dev` starts dev server on port 5173
- [ ] `npm run build` produces production build with source maps
- [ ] `npm run lint` passes with 0 warnings
- [ ] `npm run type-check` passes with `tsc --noEmit`

**Test:**
```typescript
// src/web/src/App.test.tsx
import { render, screen } from '@testing-library/react';
import { App } from './App';

test('renders app root', () => {
  render(<App />);
  expect(screen.getByRole('main')).toBeInTheDocument();
});
```

---

### WEB-001.2: PWA Manifest & Service Worker
**Files:**
- `src/web/public/manifest.json` — PWA manifest
- `src/web/src/sw-register.ts` — service worker registration
- `src/web/vite.config.ts` — `vite-plugin-pwa` config

**Acceptance:**
- [ ] Manifest: `name: "Cena Learning"`, `short_name: "Cena"`, `start_url: "/"`, `display: "standalone"`, `theme_color: "#1976D2"`, `background_color: "#FFFFFF"`
- [ ] Icons: 192x192, 512x512 PNG (placeholder for now)
- [ ] `vite-plugin-pwa` generates Workbox service worker with:
  - `runtimeCaching` for API responses (network-first, 24h cache)
  - `precacheAndRoute` for static assets (cache-first)
  - `cleanupOutdatedCaches: true`
  - `skipWaiting: true` for immediate activation
- [ ] Service worker registration in `sw-register.ts` with update prompt
- [ ] `navigator.serviceWorker.register` called only in production (`import.meta.env.PROD`)
- [ ] Lighthouse PWA audit score >= 90

**Test:**
```typescript
// src/web/src/sw-register.test.ts
import { vi } from 'vitest';

test('service worker not registered in dev mode', async () => {
  vi.stubGlobal('import.meta', { env: { PROD: false } });
  const registerSpy = vi.fn();
  Object.defineProperty(navigator, 'serviceWorker', {
    value: { register: registerSpy },
    writable: true,
  });

  await import('./sw-register');
  expect(registerSpy).not.toHaveBeenCalled();
});
```

---

### WEB-001.3: Testing Setup (Vitest + RTL)
**Files:**
- `src/web/vitest.config.ts` — test configuration
- `src/web/src/test/setup.ts` — test setup with jest-dom matchers
- `src/web/src/test/test-utils.tsx` — custom render with providers

**Acceptance:**
- [ ] Vitest configured with `jsdom` environment
- [ ] `@testing-library/react` with `@testing-library/jest-dom` matchers
- [ ] `@testing-library/user-event` for interaction testing
- [ ] Custom `render()` wrapper that includes providers (Zustand store, router)
- [ ] Coverage reporter: `v8` with threshold: 80% branches, 80% functions
- [ ] `npm test` runs all tests
- [ ] `npm run test:coverage` generates coverage report

**Test:**
```typescript
// src/web/src/test/test-utils.test.tsx
import { renderWithProviders, screen } from './test-utils';

test('custom render wraps with providers', () => {
  function TestComponent() {
    return <div data-testid="test">Hello</div>;
  }

  renderWithProviders(<TestComponent />);
  expect(screen.getByTestId('test')).toHaveTextContent('Hello');
});
```

**Edge cases:**
- Old browser without service worker support -> app works without offline features, no error
- Manifest missing icons -> Lighthouse PWA score drops; CI should catch
- TypeScript strict mode breaks third-party library types -> use `skipLibCheck: true`

---

## Integration Test

```typescript
// src/web/src/App.integration.test.tsx
import { render, screen } from '@testing-library/react';
import { App } from './App';

test('full app renders without errors', () => {
  const { container } = render(<App />);
  expect(container).toBeTruthy();
  expect(screen.getByRole('main')).toBeInTheDocument();
});

test('app has correct document title', () => {
  render(<App />);
  expect(document.title).toContain('Cena');
});
```

## Rollback Criteria
- If Vite has critical bugs: fall back to Create React App or Webpack 5
- If `vite-plugin-pwa` is unmaintained: use `workbox-cli` directly
- If strict TypeScript blocks progress: relax to `strict: false` temporarily

## Definition of Done
- [ ] All 3 subtasks pass their tests
- [ ] `npm run dev` starts without errors
- [ ] `npm run build` produces production bundle < 200KB gzipped (initial)
- [ ] `npm run lint` passes with 0 warnings
- [ ] `npm run type-check` passes
- [ ] `npm test` passes with 80%+ coverage
- [ ] Lighthouse PWA score >= 90
- [ ] Folder structure matches specification
- [ ] PR reviewed by frontend lead
