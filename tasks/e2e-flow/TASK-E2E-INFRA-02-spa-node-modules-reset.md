# TASK-E2E-INFRA-02: Untangle pnpm/npm mixed-lockfile state (containers-first recovery)

**Status**: Reframed 2026-04-24 — hard constraint: **do not delete `node_modules`**. Recovery path now goes through Docker stack reset and lockfile-level cleanup; raw `rm -rf node_modules` is the absolute last resort. See user memory `feedback_destroy_containers_not_node_modules.md`.
**Priority**: P2 (blocks host-side `npm install <pkg>` and `vite build`; **does NOT block** the dev stack or the e2e-flow suite — both run inside Docker against the container's own `node_modules` volume)
**Epic**: Shared infra
**Tag**: `@infra @frontend @build`
**Prereqs**: none

## Symptoms observed

- `npm install <any-pkg>` fails with `ENOTDIR: not a directory, rename 'node_modules/<pkg>' -> 'node_modules/.<pkg>-<hash>'` on various packages (`happy-dom`, `jsdom`, `eslint`, `vuetify`, `workbox-build`, …). Each retry surfaces a different package but the rename-ENOTDIR pattern is identical.
- `npm run build` fails with `Cannot find module 'vuetify/dist/json/importMap.json'` (@vuetify/loader-shared expects a file that the installed vuetify version's dist tree does not provide — classic symptom of a half-completed install).
- `scripts/predev-check.mjs` detects the failure and names the recovery recipe explicitly: `rm -rf node_modules package-lock.json && npm install`.
- Running that recipe **does not help** on the current machine: stale directories with timestamps predating the reinstall (e.g. `Apr 23 17:42`) persist inside `node_modules/` after a successful `rm -rf node_modules`. Likely APFS clone-on-write / extended-attribute retention holding a sub-tree open. The docker volume mounted as `/app/node_modules` is unaffected (it's a separate volume, timestamps Apr 24).

## Probable cause

Mixed pnpm + npm history in the repo. Both `package-lock.json` (888 KB, npm) and `pnpm-lock.yaml` (497 KB, pnpm) are tracked in git. A prior `pnpm install` at some point wrote nested `node_modules/<pkg>/node_modules/...` subtrees that npm's flatten-rename logic can't reconcile. Some of those subtrees are now effectively immortal on this filesystem.

## What to do (in this order — stop at the first step that resolves the issue)

1. **Container/volume reset first** (cheap, zero risk to host node_modules):
   ```bash
   docker compose down -v             # nuke containers AND volumes
   docker volume prune -f             # reclaim any dangling volumes
   docker compose -f docker-compose.yml -f docker-compose.app.yml up -d
   ```
   This rebuilds `cena-student-spa`'s isolated node_modules volume from the image and is sufficient for ~90% of "stuck install" symptoms because the container is what production-style commands actually run against.

2. **Lockfile-level cleanup, host node_modules untouched**:
   ```bash
   cd src/student/full-version
   git rm pnpm-lock.yaml              # commit-side: project is npm-majority; pnpm-lock is leftover
   echo "pnpm-lock.yaml" >> .gitignore
   npm cache clean --force            # cache-only operation; does NOT touch node_modules
   npm install --prefer-offline       # idempotent re-resolve; npm patches its own tree
   ```

3. **Targeted package surgery (still no rm -rf)**:
   ```bash
   npm uninstall <stuck-pkg>          # let npm clean its own dirs
   npm install <stuck-pkg>            # reinstall single package
   ```

4. **Last resort — only with explicit user authorization**:
   The original recipe (`rm -rf node_modules && npm install`) is the historical fix but it is **not** to be run automatically by an agent. Per `feedback_destroy_containers_not_node_modules.md`, the user's APFS clone-on-write tree has had stale dirs survive `rm -rf`, and reinstall has corrupted sibling packages on this machine. If steps 1–3 don't resolve, surface to the user with the exact failing symptom and let them pick the recovery path.

5. **Document the order** in `scripts/predev-check.mjs` (the script already names step 4 as "the recipe"; flip it to put steps 1–3 first and step 4 last, with the explicit user-authorization gate).

## Why it's not P0

- Dev stack runs fine — the Docker container's `/app/node_modules` is a separate volume the host rot doesn't touch.
- CI images install node_modules fresh on each build, so CI is unaffected.
- The blast radius is "local host-side dev ergonomics": `npm run build`, `npm run test:unit`, `npm install <new-dep>`. Playwright E2E flows work (they hit the dockerized SPA, not the host's build).

## Why it's not P3 either

- New contributors hit this on first clone.
- Any task that needs to add an npm dependency to the student SPA is blocked until this is resolved (this was exactly what blocked the bus-probe fixture — TASK-E2E-INFRA-01 — from using the official `nats` package; it ships with a raw-TCP protocol implementation instead).
- `scripts/predev-check.mjs` FIND-ux-001 already flags this as a persistent sore spot.

## Done when

- [ ] `pnpm-lock.yaml` removed from git + added to `.gitignore` (single-package-manager invariant)
- [ ] `scripts/predev-check.mjs` re-ordered: containers-first → lockfile cleanup → targeted reinstall → user-gated rm-rf
- [ ] Documentation entry under `docs/engineering/dev-stack-recovery.md` (or appended to the existing predev-check README) capturing the order and the rationale (link `feedback_destroy_containers_not_node_modules.md`)
- [ ] `npm install <pkg>` succeeds via the steps 1–3 path on the affected host
- [ ] `npm run build` succeeds inside `cena-student-spa` (container path is the contract; host path is best-effort)
