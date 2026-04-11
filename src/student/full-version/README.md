# Cena Student Web

Vue 3 + Vite + Vuetify front-end for the Cena student experience.

## Clean install

```sh
cd src/student/full-version
npm install
npm run dev        # boots on http://localhost:5175
```

On a freshly cloned repo the install does three things:

1. Resolves the dependency tree and writes `node_modules/.bin/*` (vite,
   tsx, msw, etc.)
2. Runs `scripts/postinstall-guard.mjs`, which invokes `build:icons`
   (generates `src/plugins/iconify/icons.css` from the bundled iconify
   JSON) and `msw:init` (copies `mockServiceWorker.js` into `public/`).
3. Reports any missing artifact via `[postinstall-guard]` on stderr but
   ALWAYS exits 0 so that `npm install` itself never fails due to an
   optional code-generation step.

Every time you run `npm run dev` or `npm run build`, the
`scripts/predev-check.mjs` hook runs first and verifies that every
required artifact is present (vite/tsx/msw bins, icons.css, the MSW
worker file). If anything is missing it prints a recovery recipe
instead of the opaque `sh: vite: command not found` that blocked fresh
clones in FIND-ux-001.

### Recovery

If `npm run dev` refuses to start with a missing-artifact message, run:

```sh
rm -rf node_modules package-lock.json
npm install
npm run dev
```

If `npm install` itself fails mid-tree (peer-dep bail, network hiccup),
bootstrap manually:

```sh
npm install --ignore-scripts
npm run build:icons
npm run msw:init
npm run dev
```

## Scripts

| Script | Purpose |
|---|---|
| `npm run dev` | Start Vite dev server on port 5175 (runs `predev` check first) |
| `npm run build` | Production build (runs `prebuild` check first) |
| `npm run preview` | Preview production build on port 5050 |
| `npm run typecheck` | `vue-tsc --noEmit` |
| `npm run lint` | ESLint with --fix |
| `npm run build:icons` | Rebuild `src/plugins/iconify/icons.css` |
| `npm run msw:init` | Copy MSW worker file to `public/` |
| `npm run setup` | Run both `build:icons` + `msw:init` in order |
| `npm run test:unit` | Vitest unit tests |
| `npm run test:e2e` | Playwright E2E tests |
