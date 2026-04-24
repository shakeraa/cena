# Runbook — Mashov Credentials Rotation

**Source task**: prr-017 (pre-release-review 2026-04-20)
**Owner**: Platform SRE
**Severity if overdue**: HIGH — vendor-wide credential reuse is a
  persistent-access red-team finding (persona-redteam consensus).
**Cadence**: Quarterly, or immediately on any of the triggers below.

---

## 1. What this runbook covers

Mashov is the Israeli SIS / parent-portal Cena integrates with to
expose teacher-visible mastery context (AXIS_10) and to honor grade
passback policy (ADR-TBD / prr-037). This runbook covers rotation of
the **Cena-side service-account credentials** that authenticate our
outbound HTTP calls to Mashov. It does **not** cover student login
credentials — those are owned by the school district.

Credentials in scope (logical keys in `ISecretStore`):

| Key | Purpose | Rotation owner |
|---|---|---|
| `mashov/apiUsername` | Service-account username | Cena SRE |
| `mashov/apiPassword` | Service-account password | Cena SRE |
| `mashov/apiClientId` | OAuth client ID (v2 flow, optional) | Mashov + Cena SRE |
| `mashov/apiClientSecret` | OAuth client secret (v2 flow, optional) | Mashov + Cena SRE |

## 2. When to rotate

Rotate **immediately** if ANY of the following is true:

1. Quarterly cadence has been reached (last-rotation tag on the secret).
2. An employee with access to the secret store has departed.
3. `NoHardcodedMashovCredentialsTest` has ever failed on `main` (even
   transiently — assume exposure).
4. Mashov publishes a security advisory affecting the integration.
5. A Cena incident post-mortem cites Mashov credentials as a blast-radius
   amplifier.

## 3. Preflight

- [ ] Confirm `ISecretStore` production adapter is healthy in both
      staging and production (check `/health/ready` for the Admin host;
      `redis` + `postgresql` must be Healthy — if the store itself is
      unreachable, fix that before rotating).
- [ ] Open a change ticket; link this runbook.
- [ ] Announce in `#eng-platform` — rotation takes ~5 minutes; during
      that window, any in-flight Mashov call may see a transient 401.
      The integration's Polly policy retries once on 401; a second
      failure falls through to the staleness badge (prr-039).

## 4. Rotation steps (AWS Secrets Manager)

```bash
# 1. Request a new service-account credential from Mashov ops.
#    Use their web portal — see https://help.mashov.info for the
#    current admin console URL.

# 2. Write the new secret into Secrets Manager WITHOUT removing the
#    old one — adapters do a warm-cache swap so both values must be
#    readable for up to the cache TTL (5 min default).
aws secretsmanager put-secret-value \
  --secret-id cena/mashov/apiUsername \
  --secret-string "$NEW_USERNAME"

aws secretsmanager put-secret-value \
  --secret-id cena/mashov/apiPassword \
  --secret-string "$NEW_PASSWORD"

# 3. Wait for cache TTL (5 min) + 1 min safety margin.
sleep 360

# 4. Verify the new credential works. Hit the read-only health probe
#    endpoint that exercises the Mashov auth handshake:
curl -s https://api.cena.example/health/integrations/mashov | jq .status
# Expect: "healthy"

# 5. (Optional, belt-and-suspenders) Tag the secret with the rotation
#    timestamp so the `mashov-credential-age` SLO alert stays green.
aws secretsmanager tag-resource \
  --secret-id cena/mashov/apiUsername \
  --tags 'Key=last-rotated,Value='"$(date -u +%Y-%m-%d)"
```

### GCP Secret Manager variant

```bash
# Same flow, gcloud verbs:
echo -n "$NEW_USERNAME" | gcloud secrets versions add cena-mashov-api-username --data-file=-
echo -n "$NEW_PASSWORD" | gcloud secrets versions add cena-mashov-api-password --data-file=-
# Old versions stay readable until explicitly disabled — do that 24h
# later, after confirming stability.
gcloud secrets versions disable <OLD_VERSION> --secret=cena-mashov-api-username
```

## 5. Verification

- [ ] `curl https://api.cena.example/health/integrations/mashov`
      returns `healthy` (5-minute Polly warm-up allowed).
- [ ] Grafana "Mashov Integration" dashboard shows authentication
      success rate ≥99% for 30 minutes after rotation.
- [ ] No spike in `cena_sis_auth_failures_total{vendor="mashov"}`.
- [ ] No new entries in the prr-039 staleness-badge counter.

## 6. Rollback

If post-rotation error rate >1% for >10 minutes:

```bash
# Point the secret's AWSCURRENT stage back at the prior version.
aws secretsmanager update-secret-version-stage \
  --secret-id cena/mashov/apiUsername \
  --version-stage AWSCURRENT \
  --move-to-version-id <PREVIOUS_VERSION_ID> \
  --remove-from-version-id <NEW_VERSION_ID>
```

Invalidate the Cena-side cache by doing a rolling restart of the
Admin API host (the Null / AWS / GCP adapter all read on cold start;
restart forces a fresh fetch):

```bash
kubectl rollout restart -n cena deployment/cena-admin-api
```

## 7. Post-rotation

- Close the change ticket; record the new `last-rotated` tag value.
- Update the SLO spreadsheet (`ops/slo/credential-rotation-cadence.md`)
  with the new date.
- If you found any gap in this runbook, edit it in the same PR as your
  change ticket.

## 8. 03:00-on-Bagrut-morning failure mode

If the Mashov integration is failing at 03:00 on an exam-prep morning,
the correct action is **NOT** to rotate. Rotation during a live-traffic
window is a strictly worse outcome than letting the staleness badge
(prr-039) inform teachers that the last-known mastery view is N hours
stale. Follow `incident-response.md` first; only rotate if the
post-incident analysis proves credential compromise.
