# Runbook: Exam catalog CDN outage

Owner: platform SRE on-call. Severity: P2 on onboarding-morning windows
(pre-exam), P3 otherwise. prr-220 + ADR-0050.

## What this runbook covers

The student SPA serves the onboarding "pick your exam target" step from the
exam-catalog endpoint (`GET /api/v1/catalog/exam-targets`). The endpoint is
cached at the edge (Cache-Control: public, max-age=300, SWR 86400). On a
CDN outage the SPA must continue to let new students choose a target — a
Bagrut-morning outage blocking onboarding is a P1 business incident.

Three fallback layers exist:

1. **Edge cache** — 5-minute TTL + 24-hour stale-while-revalidate. The edge
   serves the last-successful response even when origin is down, for up
   to 24 hours.
2. **Service worker cache** — the PWA caches the last 200 response locally
   with 24-hour TTL. Returning students see their catalog even offline.
3. **Bundled fallback JSON** — `catalog-fallback.json` is baked into the
   SPA build. On a cold-start PWA miss + CDN miss, the SPA loads this
   bundle and shows a banner: "using offline catalog — tenant overrides
   unavailable".

## Detection

- Synthetic monitor `catalog-edge-probe` fires every 60s against
  `https://<cdn-host>/api/v1/catalog/exam-targets?locale=en`. Alert on
  5 consecutive failures (5 minutes).
- Student-SPA client reports `catalog_fallback_used=true` via RUM. Alert
  when the rate exceeds 5% over 5 minutes.
- Origin-side 5xx rate on `GET /api/v1/catalog/*` over 2%.

## Immediate actions

1. **Confirm scope.** Is the outage limited to one edge region or global?
   Check CDN vendor status page + internal probe dashboard.
2. **Check origin health.** If origin is healthy but the edge is down,
   fallback layers 1+3 cover us; proceed to step 3. If origin is also
   down, skip to step 4.
3. **Verify SWR is serving.** `curl -I https://<cdn>/api/v1/catalog/exam-targets?locale=en`
   should show `X-Cache: STALE`. If it shows `MISS` with a 5xx, the edge
   has aged past SWR — go to step 5.
4. **If origin is down**, cut over to the standby student-API host (blue/green
   DNS flip via CloudFront origin group). Record in incident timeline.
5. **Forced bundle fallback.** If CDN + origin are both unrecoverable for
   >10 minutes during peak, push a build-time feature flag flip that forces
   the SPA to use `catalog-fallback.json` without trying the network:
   - Set `VITE_FORCE_CATALOG_FALLBACK=true` in the SPA config repo.
   - Trigger a rebuild + deploy via CI (emergency pipeline green-lights).
   - Verify the SPA shows the "offline catalog" banner.
6. **Tenant-overlay disable (break-glass).** If the overlay store is the
   failure surface (tenant-config DB down but catalog itself is up), flip
   the `FORCE_GLOBAL_CATALOG` feature flag to serve the global catalog
   with an empty overlay to every tenant. Communicate to tenants that
   custom disabled targets are temporarily re-enabled.

## Acceptable bounds

- **≤ 5 minutes**: edge cache should handle it silently.
- **5m to 1h**: SWR covers it; SPA banner appears for new users only.
- **1h to 24h**: PWA service-worker cache covers returning students; new
  users see the baked fallback banner. Business impact acceptable on a
  non-exam morning; on a Bagrut morning this is already P1.
- **> 24h**: bundled fallback is stale (tenant overrides missing; catalog
  version drift). Force a catalog-version bump + redeploy SPA with fresh
  `catalog-fallback.json`.

## Post-incident

- Rotate the `catalog-fallback.json` baseline — rebuild from the last
  known-good catalog YAML + push to the SPA bucket.
- Post-mortem must cover which fallback layer(s) fired and how long.
- If SWR TTL was insufficient, propose raising to 48h in the edge config.

## Related

- Task: [tasks/pre-release-review/TASK-PRR-220-exam-catalog-service.md](../../../tasks/pre-release-review/TASK-PRR-220-exam-catalog-service.md)
- ADR: [docs/adr/0050-multi-target-student-exam-plan.md](../../adr/0050-multi-target-student-exam-plan.md)
- Source: `contracts/exam-catalog/*.yml`, `src/api/Cena.Student.Api.Host/Catalog/`.
- Fallback bundle: `src/student/full-version/public/catalog-fallback.json`.
