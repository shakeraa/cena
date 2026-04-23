# TASK-PRR-437: Meta WhatsApp inbound delivery-status webhook + signature verification

**Priority**: P1
**Effort**: M (~1 day)
**Lens consensus**: ops + reliability — closes the half-open loop on PRR-429 outbound send
**Source**: 2026-04-22 peripherals rollout — PRR-429 task explicitly deferred inbound webhook handling
**Assignee hint**: backend
**Tags**: epic=none, platform-hardening, priority=p1, whatsapp, webhook
**Status**: Ready
**Tier**: launch
**Epic**: none (complements PRR-429)

---

## Problem

PRR-429 landed `MetaCloudWhatsAppSender` for outbound delivery, but delivery *status* (`sent` / `delivered` / `read` / `failed`) only flows back via Meta's inbound webhook. Without webhook handling:

- Failed deliveries never reach `WhatsAppDeadLetter` — ops can't triage bad numbers / rejected templates
- Sender-reputation (`WhatsAppSenderQuality.Green | Yellow | Red`) never updates — the circuit breaker in `WhatsAppDispatcher.Decide` stays `Unknown` forever
- Read-rates never feed the digest cadence tuner
- Idempotent retries can double-send when Meta's response shows `Accepted` but subsequent callback reveals a `failed` state the dispatcher doesn't see

## Goal

Endpoint + signature verification + status-to-outcome mapper. No new concepts; just the inbound half of the protocol Meta-Cloud defines.

## Scope

### 1. Webhook verification (GET handshake)

`GET /api/webhooks/meta-whatsapp` — Meta's verify-token handshake at subscription time. Read `hub.mode=subscribe`, `hub.verify_token`, `hub.challenge` query params; compare `verify_token` against `MetaCloud:WebhookVerifyToken` config. Match → return 200 with the challenge body verbatim; mismatch → 403.

Must be publicly reachable (anonymous auth, no rate limit bypass of inbound guards). Register at `src/api/Cena.Admin.Api.Host/Program.cs` adjacent to the StripeWebhook registration for pattern-match.

### 2. Webhook inbound (POST delivery-status)

`POST /api/webhooks/meta-whatsapp` — Meta POSTs a JSON envelope like:

```json
{
  "entry": [{
    "changes": [{
      "value": {
        "statuses": [{
          "id": "{meta_message_id}",
          "recipient_id": "{e164_no_plus}",
          "status": "delivered | sent | read | failed",
          "timestamp": "1730000000",
          "errors": [{"code": 131047, "title": "..."}]
        }]
      }
    }]
  }]
}
```

**Signature verification (non-negotiable)** — Meta signs each POST with `X-Hub-Signature-256: sha256=<hex>` using the App Secret. Compute HMAC-SHA256 over the raw request body (bytes, not parsed JSON) with the `MetaCloud:AppSecret` key; compare constant-time. Mismatch → 401, log at Warning with correlation id (not the body — might contain numbers).

Signature scrubber reuses the existing `ExceptionScrubber` patterns via a new `IMetaWebhookSignatureVerifier` that takes the raw bytes + header and yields bool.

### 3. Status-to-outcome mapping

Translate Meta's status string + error code to an internal state:

| Meta status | Error code | Internal action |
|---|---|---|
| `sent` | — | Breadcrumb only; no state change |
| `delivered` | — | Transition correlation to `Delivered`; update metrics |
| `read` | — | Transition correlation to `Read`; update cadence-tuner signal |
| `failed` | 131047 | Move to `WhatsAppDeadLetter` with `Reason="re-engagement window expired"` |
| `failed` | 132000..132999 | Move to `WhatsAppDeadLetter` with `Reason="template not approved / mismatch"` + mark template `Paused` in catalogue |
| `failed` | 131050 | Mark sender-quality `Red`, pause WhatsApp channel for 24h |
| `failed` | any other | Move to `WhatsAppDeadLetter` with `Reason=$"meta-code:{code}"` |

### 4. Idempotency

Meta retries failed POSTs. Use the `id` (Meta message id) as an idempotency key in a short-TTL Redis cache (`whatsapp:webhook:{id}`, 48h TTL). Duplicate → return 200 without acting (Meta only cares about the 200).

### 5. Domain types (extend existing, don't create parallel)

- Reuse `WhatsAppDeadLetter` record already defined in [WhatsAppChannel.cs:176](../../src/actors/Cena.Actors/ParentDigest/WhatsAppChannel.cs#L176). Persisted via a new `IWhatsAppDeadLetterStore` (Marten-backed; read-side projection feeds the admin console ops queue).
- Reuse `WhatsAppSenderQuality` enum (already defined). A new `IWhatsAppSenderQualityStore` surfaces the current state to `WhatsAppDispatcher.Decide` at the call site (circuit-breaker input).

### 6. Tests

- `MetaWebhookSignatureVerifierTests`: known-good payload + signature passes; tampered body fails; wrong secret fails; missing header fails.
- `MetaWebhookStatusMapperTests`: each row of the §3 table verified by fixture JSON.
- `MetaWhatsAppWebhookEndpointTests`: integration-style — bad signature → 401; valid verify handshake → 200 echo; duplicate id → 200 no-op; failed-status writes to the dead-letter store.
- `WhatsAppDispatcherCircuitBreakerTests` (new): with sender quality Red, `Decide` returns `FallBackToEmail`. Covers the end-to-end seam between webhook → store → dispatcher.

### 7. Config

Add to all 3 host `appsettings.json` under the existing `MetaCloud` section:

```json
"MetaCloud": {
  "PhoneNumberId": "",
  "AccessToken": "",
  "BusinessAccountId": "",
  "GraphApiVersion": "v21.0",
  "AppSecret": "",
  "WebhookVerifyToken": ""
}
```

Both new fields blank by default; webhook endpoint refuses inbound when either is empty (safety net against deploying without the pair).

## Out of scope

- Actual message *content* webhooks (inbound text messages from parents) — that's a customer-support channel nobody's asked for yet.
- Template-approval lifecycle webhooks (Meta notifies when a template's approval state flips) — separate task when the admin console template-authoring UX lands.

## Files

**Add**:
- `src/api/Cena.Admin.Api/Features/Webhooks/MetaWhatsAppWebhookEndpoint.cs`
- `src/api/Cena.Admin.Api/Features/Webhooks/MetaWebhookSignatureVerifier.cs`
- `src/actors/Cena.Actors/ParentDigest/MetaWebhookStatusMapper.cs`
- `src/actors/Cena.Actors/ParentDigest/IWhatsAppDeadLetterStore.cs` + in-memory impl (Marten impl deferred per Phase-1 in-memory pattern)
- `src/actors/Cena.Actors/ParentDigest/IWhatsAppSenderQualityStore.cs` + in-memory impl
- Tests: `MetaWebhookSignatureVerifierTests.cs`, `MetaWebhookStatusMapperTests.cs`, `MetaWhatsAppWebhookEndpointTests.cs`

**Modify**:
- `NotificationsServiceCollectionExtensions.cs` — register dead-letter + quality stores
- 3× `appsettings.json` — add `AppSecret` + `WebhookVerifyToken` fields
- `docs/ops/peripheral-costs.md` — change log entry only (no cost impact)

## Definition of Done

- [ ] Webhook signature verification passes Meta's official test harness (documented cURL fixture)
- [ ] Failed-status webhook writes a `WhatsAppDeadLetter` visible in the admin console ops queue
- [ ] Sender quality transitions Red → email fallback kicks in via `WhatsAppDispatcher.Decide`
- [ ] Full sln build gate green
- [ ] All PRR-430 tests green; PRR-428 + PRR-429 tests still green
- [ ] Merged to `main`

## Non-negotiable references

- PRR-429 — outbound adapter this closes the loop on
- Memory "No stubs — production grade" — signature verification uses real HMAC, not a stub
- Memory "Full sln build gate"
- Memory "Always merge to main"
