# TASK-PRR-429: MetaCloudWhatsAppSender — Meta Cloud API direct adapter

**Priority**: P2
**Effort**: S (~1 day)
**Lens consensus**: cost-optimisation — drops-in behind existing vendor-neutral interface
**Source**: 2026-04-22 peripherals audit — see [docs/ops/peripheral-costs.md §3](../../docs/ops/peripheral-costs.md#3-whatsapp--meta-cloud-api-direct-not-via-twilio)
**Assignee hint**: backend
**Tags**: epic=none, platform-hardening, priority=p2, cost-optimisation, whatsapp, vendor-swap
**Status**: Pending
**Tier**: launch
**Epic**: none

---

## Problem

[PRR-428](TASK-PRR-428-notifications-di-wiring.md) landed the vendor-neutral `IWhatsAppSender` selector
in `NotificationsServiceCollectionExtensions`, but only `"twilio"` and `"null"` are wired in the
switch. The peripheral-costs audit (2026-04-22) recorded that at **1,000-parent scale**:

- Meta Cloud API direct ≈ **$42/mo** (3,000 billable utility convos × $0.014 after the 1,000 free tier)
- Same volume via Twilio → Meta ≈ **$57/mo** (+ $0.005/msg Twilio fee)

Switching saves ~$15/mo immediately and ~$150/mo at 10× scale. The interface already exists — this
task wires a concrete Meta Cloud adapter behind the same selector so the swap is a
`Notifications:WhatsApp:Backend` config change, not a code change.

## Goal

Ship `MetaCloudWhatsAppSender : IWhatsAppSender` against the Meta Cloud Graph API (v21.0) and
register it in `NotificationsServiceCollectionExtensions.RegisterWhatsApp` under
`backend == "meta"`. Twilio stays the default; operators opt in by flipping the config key.

## Scope

### 1. New `MetaCloudWhatsAppSender` + `MetaCloudWhatsAppOptions`

Path: `src/actors/Cena.Actors/ParentDigest/MetaCloudWhatsAppSender.cs`

- Implements `IWhatsAppSender`; `VendorId = "meta"`; `IsConfigured` true iff
  `PhoneNumberId` + `AccessToken` + `BusinessAccountId` are all non-blank.
- Options bound from configuration section `"MetaCloud"`:
  - `PhoneNumberId` — business phone-number identifier from WhatsApp Business Manager
  - `AccessToken` — long-lived system-user token
  - `BusinessAccountId`
  - `GraphApiVersion` — default `"v21.0"`
  - `BaseUrl` — default `"https://graph.facebook.com"` (override in tests)
- Posts JSON to `{BaseUrl}/{GraphApiVersion}/{PhoneNumberId}/messages` with Bearer auth. Body shape:

  ```json
  {
    "messaging_product": "whatsapp",
    "to": "{e164_phone_no_plus}",
    "type": "template",
    "template": { "name": "{TemplateId}", "language": { "code": "{locale}" } }
  }
  ```

- Uses the same `IWhatsAppRecipientLookup` as the Twilio impl.
- Sets `Idempotency-Key: {CorrelationId}` header (Meta supports this on v21+ for dedup).
- Error-code mapping (parsed from `{"error":{"code":N,"message":"..."}}` via `System.Text.Json`):
  - `200` → `Accepted`
  - `400` + code `131047` (re-engagement window expired) → `InvalidRecipient`
  - `400` + code `132000–132999` (template not approved / mismatch) → `TemplateNotApproved`
  - `429` → `RateLimited`
  - `401` / `403` → `VendorError`
  - Anything else → `VendorError`
- No-throw: all paths wrapped in try/catch; `HttpRequestException` / `TaskCanceledException` →
  `VendorError`, same posture as `TwilioWhatsAppSender`.
- Logger prefix `[PRR-429]` + correlation id in structured fields.

### 2. Register in the selector

Edit `src/actors/Cena.Actors/Notifications/NotificationsServiceCollectionExtensions.cs`:

- Add `"meta"` case in `RegisterWhatsApp` — binds `MetaCloudWhatsAppOptions`, registers named
  HttpClient `"MetaCloudWhatsApp"`, and resolves `IWhatsAppSender` from the factory.
- Update the `default:` fallback log message to list `"meta"` as an accepted value alongside
  `twilio` and `null`.

### 3. Config sections

Add a `"MetaCloud"` section to all 3 host `appsettings.json` files (alongside the existing
`Notifications` section). Leave `Notifications:WhatsApp:Backend` set to `"twilio"` so behaviour
is unchanged until an operator opts in.

```json
"MetaCloud": {
  "PhoneNumberId": "",
  "AccessToken": "",
  "BusinessAccountId": "",
  "GraphApiVersion": "v21.0"
}
```

### 4. Tests

New `src/actors/Cena.Actors.Tests/ParentDigest/MetaCloudWhatsAppSenderTests.cs`, mirroring the
shape of `TwilioWhatsAppSenderTests.cs`:

- **Options**: `IsComplete` gating across credential combinations
- **Mapping**: HTTP status + Meta error code → `WhatsAppDeliveryOutcome`
- **Unconfigured**: missing `PhoneNumberId` → `VendorError`
- **Invalid recipient**: `NullWhatsAppRecipientLookup` → `InvalidRecipient`
- **Happy path**: mock HttpClient returns 200 → `Accepted`, verify request path/body/headers
  (including `Idempotency-Key` + `Authorization: Bearer`)

Also add a case to `NotificationsWiringTests.cs`:

```csharp
[Fact]
public void WhatsApp_backend_meta_resolves_MetaCloudWhatsAppSender()
{
    using var sp = Build(new Dictionary<string, string?>
    {
        ["Notifications:WhatsApp:Backend"] = "meta",
        ["MetaCloud:PhoneNumberId"] = "1234567890",
        ["MetaCloud:AccessToken"] = "dummy-token",
        ["MetaCloud:BusinessAccountId"] = "987654321"
    });
    var sender = sp.GetRequiredService<IWhatsAppSender>();
    Assert.IsType<MetaCloudWhatsAppSender>(sender);
    Assert.Equal("meta", sender.VendorId);
}
```

### 5. Peripheral-costs change log

Add a row to [docs/ops/peripheral-costs.md](../../docs/ops/peripheral-costs.md) change-log table:

```
| 2026-04-22 | MetaCloudWhatsAppSender shipped — selector supports "meta" backend. Flipping `Notifications:WhatsApp:Backend` to `"meta"` saves ~$15/mo at 1k-parent scale. | PRR-429 |
```

## Out of scope

- **Webhook inbound delivery-status callbacks** (Meta calls back with `sent` / `delivered` / `read`
  events). Tracked as a separate follow-up PRR.
- **Default backend migration from twilio → meta.** User-controlled flip; this task keeps Twilio
  default.
- **Template pre-approval workflow.** Already modelled by `WhatsAppTemplate.PreApprovalStatus`;
  ingestion of Meta's template state is a separate concern.

## Files to add / modify

**Add**:

- `src/actors/Cena.Actors/ParentDigest/MetaCloudWhatsAppSender.cs`
- `src/actors/Cena.Actors.Tests/ParentDigest/MetaCloudWhatsAppSenderTests.cs`

**Modify**:

- `src/actors/Cena.Actors/Notifications/NotificationsServiceCollectionExtensions.cs` (+meta case, fallback message)
- `src/actors/Cena.Actors.Tests/Notifications/NotificationsWiringTests.cs` (+meta resolution test)
- `src/actors/Cena.Actors.Host/appsettings.json` (+MetaCloud section)
- `src/api/Cena.Admin.Api.Host/appsettings.json` (+MetaCloud section)
- `src/api/Cena.Student.Api.Host/appsettings.json` (+MetaCloud section)
- `docs/ops/peripheral-costs.md` (+change log row)

## Definition of Done

- [ ] `MetaCloudWhatsAppSender` implemented; `VendorId = "meta"`; `IsConfigured` gated on all 3 credentials.
- [ ] `"meta"` case registered in `RegisterWhatsApp`; default fallback message mentions `meta`.
- [ ] All 4 test groups green (options / mapping / unconfigured / recipient / happy path).
- [ ] `NotificationsWiringTests.WhatsApp_backend_meta_resolves_MetaCloudWhatsAppSender` green.
- [ ] Existing Twilio tests **do not regress**.
- [ ] `dotnet build Cena.Actors.sln` zero errors.
- [ ] Commit on `claude-subagent-meta-whatsapp/PRR-429` → reviewed by coordinator → merged to `main`.

## Non-negotiable references

- [TASK-PRR-428](TASK-PRR-428-notifications-di-wiring.md) — the selector this task extends
- [docs/ops/peripheral-costs.md §3](../../docs/ops/peripheral-costs.md#3-whatsapp--meta-cloud-api-direct-not-via-twilio) — cost motivation
- Memory ["No stubs — production grade"](~/.claude/projects/-Users-shaker-edu-apps-cena/memory/feedback_no_stubs_production_grade.md) — this is a real adapter, not a stub
- Memory ["Full sln build gate"](~/.claude/projects/-Users-shaker-edu-apps-cena/memory/feedback_full_sln_build_gate.md) — full-sln build before merge
- Meta Cloud Graph API v21.0 messages endpoint: `POST /{phone-number-id}/messages`
