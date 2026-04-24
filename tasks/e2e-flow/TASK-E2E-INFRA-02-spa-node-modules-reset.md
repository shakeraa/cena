# TASK-E2E-INFRA-02: Reset `src/student/full-version/node_modules` (stuck pnpm/npm mixed state)

**Status**: Proposed
**Priority**: Medium (blocks host-side `npm run build`, `npm install <pkg>`, and the `vite build` CI path; **does NOT block** the dev stack — `cena-student-spa` uses its own Docker volume)
**Epic**: Shared infra — a recurrence of the exact failure mode the `scripts/predev-check.mjs` recipe documents
**Tag**: `@infra @frontend @build`

## Symptoms observed

- `npm install <any-pkg>` fails with `ENOTDIR: not a directory, rename 'node_modules/<pkg>' -> 'node_modules/.<pkg>-<hash>'` on various packages (`happy-dom`, `jsdom`, `eslint`, `vuetify`, `workbox-build`, …). Each retry surfaces a different package but the rename-ENOTDIR pattern is identical.
- `npm run build` fails with `Cannot find module 'vuetify/dist/json/importMap.json'` (@vuetify/loader-shared expects a file that the installed vuetify version's dist tree does not provide — classic symptom of a half-completed install).
- `scripts/predev-check.mjs` detects the failure and names the recovery recipe explicitly: `rm -rf node_modules package-lock.json && npm install`.
- Running that recipe **does not help** on the current machine: stale directories with timestamps predating the reinstall (e.g. `Apr 23 17:42`) persist inside `node_modules/` after a successful `rm -rf node_modules`. Likely APFS clone-on-write / extended-attribute retention holding a sub-tree open. The docker volume mounted as `/app/node_modules` is unaffected (it's a separate volume, timestamps Apr 24).

## Probable cause

Mixed pnpm + npm history in the repo. Both `package-lock.json` (888 KB, npm) and `pnpm-lock.yaml` (497 KB, pnpm) are tracked in git. A prior `pnpm install` at some point wrote nested `node_modules/<pkg>/node_modules/...` subtrees that npm's flatten-rename logic can't reconcile. Some of those subtrees are now effectively immortal on this filesystem.

## What to do

1. **Kill the container reference to the volume first**: `docker compose stop cena-student-spa` — ensures nothing is holding bind paths open.
2. **Take the nuclear option**:
   ```bash
   cd src/student/full-version
   rm -rf node_modules
   # If rm leaves stale dirs (as it does on the affected Mac), rename first:
   mv node_modules /tmp/cena-spa-nm-wrecked-$(date +%s) 2>/dev/null || true
   # And rely on a cold `npm cache clean --force` if npm still gets confused:
   npm cache clean --force
   ```
3. **Commit to a single package manager**. The project is npm-majority (scripts use `npm run`, postinstall uses `node`, no `packageManager` field declares otherwise). Propose: delete `pnpm-lock.yaml` from git, add it to `.gitignore` to prevent future accidental commits.
4. **Re-install clean**:
   ```bash
   rm -f package-lock.json
   npm install
   # Sanity: should list ~100+ bins
   ls node_modules/.bin/ | wc -l
   npm run build                       # should succeed
   ```
5. **Commit the regenerated `package-lock.json`** (expect a diff — version resolutions shift under a fresh install). Squash with the `pnpm-lock.yaml` removal and a `.gitignore` entry for it.
6. **Document the recipe** in `scripts/predev-check.mjs` (the script already names the basic recipe; extend the failure-case advice to mention the `mv + rename` fallback when `rm -rf` stalls).

## Why it's not P0

- Dev stack runs fine — the Docker container's `/app/node_modules` is a separate volume the host rot doesn't touch.
- CI images install node_modules fresh on each build, so CI is unaffected.
- The blast radius is "local host-side dev ergonomics": `npm run build`, `npm run test:unit`, `npm install <new-dep>`. Playwright E2E flows work (they hit the dockerized SPA, not the host's build).

## Why it's not P3 either

- New contributors hit this on first clone.
- Any task that needs to add an npm dependency to the student SPA is blocked until this is resolved (this was exactly what blocked the bus-probe fixture — TASK-E2E-INFRA-01 — from using the official `nats` package; it ships with a raw-TCP protocol implementation instead).
- `scripts/predev-check.mjs` FIND-ux-001 already flags this as a persistent sore spot.

## Done when

- [ ] `npm run build` succeeds on a clean host-side clone
- [ ] `npm install <any-pkg>` succeeds without ENOTDIR
- [ ] `pnpm-lock.yaml` removed from git + added to `.gitignore`
- [ ] Updated `package-lock.json` committed
- [ ] `scripts/predev-check.mjs` documents the mv-then-rm fallback
