# TASK-E2E-BG-01: Backend gap — `GET /api/admin/ai/settings` returns 500

**Status**: Proposed (surfaced 2026-04-27 by EPIC-G admin smoke matrix)
**Priority**: P1
**Epic**: Backend gap fixes (referenced by EPIC-E2E-G admin journey)
**Tag**: `@backend-gap @admin-api @p1`
**Owner**: admin-api maintainers
**Surfaced by**: `EPIC-G-admin-pages-smoke.spec.ts` allowlist entry + screenshot evidence (`localhost:5174/apps/system/ai-settings` shows red banner `[GET] "/api/admin/ai/settings": 500 Internal Server Error`)

## Evidence

User-visible symptom on `/apps/system/ai-settings`:

```
[GET] "/api/admin/ai/settings": 500 Internal Server Error
```

The page now renders the empty-state cleanly (the JS-undefined regression on `activeProviderConfig.modelId` was patched in 7e334fdd), but the underlying API still 500s, so the providers list never populates. "Save Settings" submits to a defaulted form — i.e. the user can stomp on whatever was actually configured.

## What to investigate

1. Reproduce locally:
   ```bash
   TOKEN=$(curl -s -X POST "http://localhost:9099/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key" \
     -H 'Content-Type: application/json' \
     -d '{"email":"admin@cena.local","password":"DevAdmin123!","returnSecureToken":true}' | jq -r .idToken)
   curl -i -H "Authorization: Bearer $TOKEN" http://localhost:5052/api/admin/ai/settings
   ```
2. Check `cena-admin-api` logs for the unhandled exception on this route.
3. Likely culprits (ranked):
   - Marten projection registration missing — settings doc type not in the registry (echo of TASK-E2E-BG-04 SignalR pattern)
   - Reading from a config table that wasn't seeded by `seed-marten-from-firebase.sh`
   - Provider-secrets store throwing on a missing key (the page renders provider tiles with `provider.modelId`; if provider list is empty, the API may be choking on an empty enumeration)

## Definition of done

- [ ] Root cause identified + linked PR with the fix
- [ ] `GET /api/admin/ai/settings` returns 200 with at least an empty providers array (`{"providers":[],"defaults":{...}}`) when nothing is configured
- [ ] If the admin has never set a provider, the page surfaces an empty-state with a clear "Add Provider" CTA — NOT a red 500 banner
- [ ] EPIC-G admin smoke `KNOWN_BROKEN_ROUTES['/apps/system/ai-settings']` entry REMOVED — the test will then enforce that the route stays green
- [ ] Unit test added in `Cena.Admin.Api.Tests` for the GET handler covering the "no providers configured" + "happy path" branches

## Why this is a real bug, not just noise

- The admin's only path to wire up an LLM provider goes through this page
- Today, opening it shows a red error banner — even an admin who knows the system cannot tell whether Save would succeed
- The "Generation Defaults" panel still works and saves — but tier defaults written via `PUT /api/admin/ai/settings` while GET 500s means the admin is editing a phantom record
