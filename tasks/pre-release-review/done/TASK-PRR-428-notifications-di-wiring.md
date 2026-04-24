# TASK-PRR-428: Wire notifications DI — config-driven vendor selector for Email/SMS/WhatsApp

**Priority**: P1
**Effort**: S (~1 day)
**Lens consensus**: platform-hardening — closes an orphaned-abstraction gap
**Source**: 2026-04-22 peripherals audit conversation — user asked *"is there a provider and DI that will allow me to switch the peripheral?"*
**Assignee hint**: backend
**Tags**: epic=none, platform-hardening, priority=p1, di, vendor-swap
**Status**: In-progress (claude-code)
**Tier**: launch
**Epic**: none (platform-wide, touches all 3 hosts)

---

## Problem

The peripheral interfaces are vendor-neutral by design (`IEmailSender`, `ISmsSender`, `IWhatsAppSender` with `VendorId` + `IsConfigured` properties), but **none of them are DI-registered in any host Program.cs**. Evidence: grepping the 3 host files for `AddSingleton|AddScoped|AddTransient.*Sender` returns only `IWebPushClient`/`IPushNotificationRateLimiter`.

Consequences:

1. `NotificationChannelService` takes `IWebPushClient`, `IEmailSender`, `ISmsSender` as constructor dependencies but is itself unregistered. Wiring it into any pipeline crashes DI with `Unable to resolve service for type 'ISmsSender'`.
2. `NotificationDispatcher` dodges the issue via `INotificationChannelService? channelService = null` optional injection — which is exactly the "silent no-op" stub pattern the user banned 2026-04-11 ("no stubs — production grade").
3. Swapping vendors (Twilio → Meta Cloud for WhatsApp, SMTP → SendGrid for email) has no single-point switch; callers would have to change.

The `AddCenaErrorAggregator` extension at [ServiceCollectionExtensions.cs](../../src/shared/Cena.Infrastructure/Observability/ErrorAggregator/ServiceCollectionExtensions.cs) is the canonical "good" pattern to replicate — config-driven backend selection with safe fallback, registered once per host.

## Goal

Ship `AddCenaNotifications(IConfiguration)` mirroring the `AddCenaErrorAggregator` pattern. Register Email, SMS, WhatsApp, and the multi-channel `INotificationChannelService` behind a config selector so vendor swaps become a config-only change.

## Scope

### 1. New extension `Cena.Actors.Notifications.NotificationsServiceCollectionExtensions`

Backend-selector for each peripheral. Schema:

```json
"Notifications": {
  "Email":    { "Backend": "smtp" },    // smtp | null  (future: ses-native, sendgrid, postmark)
  "Sms":      { "Backend": "twilio" },  // twilio | null  (future: aws-sns, vonage)
  "WhatsApp": { "Backend": "twilio" }   // twilio | null  (future: meta, 360dialog)
}
```

Unknown / missing backend → register the `Null*` variant + log warning. Never throw at startup; mirror the ErrorAggregator fallback behaviour.

### 2. New `Null*` senders

- `NullEmailSender` — returns `EmailSendResult(false, "NOT_CONFIGURED", ...)`. `IsConfigured` = false.
- `NullSmsSender` — same shape.
- `NullWhatsAppSender` already exists in [WhatsAppChannel.cs:160](../../src/actors/Cena.Actors/ParentDigest/WhatsAppChannel.cs#L160); no new file needed.

The existing `SmtpEmailSender` + `TwilioSmsSender` already degrade gracefully when unconfigured (documented behaviour), but having explicit `Null*` variants means the selector result is deterministic and unit-testable without fiddly config.

### 3. Peripheral dependencies wired

- `IWhatsAppRecipientLookup` → `NullWhatsAppRecipientLookup` via `TryAddSingleton` (the real lookup is a separate task when the identity store lands)
- `TwilioWhatsAppOptions` bound from `Twilio:*` section
- `HttpClient` for `TwilioWhatsAppSender` via `AddHttpClient("TwilioWhatsApp")`
- `INotificationChannelService` → `NotificationChannelService` (Scoped)

### 4. Wire in all 3 hosts

One-liner in each:

- [src/actors/Cena.Actors.Host/Program.cs](../../src/actors/Cena.Actors.Host/Program.cs) — next to `AddCenaErrorAggregator`
- [src/api/Cena.Admin.Api.Host/Program.cs](../../src/api/Cena.Admin.Api.Host/Program.cs) — same
- [src/api/Cena.Student.Api.Host/Program.cs](../../src/api/Cena.Student.Api.Host/Program.cs) — replaces the lone `AddSingleton<IWebPushClient>` line (moved into the extension)

### 5. Config section in 3 appsettings.json files

Add the `Notifications` section with `smtp` / `twilio` defaults so the behaviour is identical to today (SMS + Twilio + SMTP stubs are graceful-disabled when credentials missing).

### 6. Tests

New `Cena.Actors.Tests/Notifications/NotificationsWiringTests.cs` mirroring [ErrorAggregatorWiringTests.cs](../../src/shared/Cena.Infrastructure.Tests/Observability/ErrorAggregatorWiringTests.cs). Coverage:

- Default (no config) → Null senders resolved across all 3 channels
- `Backend=smtp` / `twilio` → real senders resolved
- Unknown backend → Null fallback + no throw
- `INotificationChannelService` resolves with all 3 channel deps satisfied
- `IWhatsAppRecipientLookup` always registered (even when WhatsApp backend = null)

## Out of scope (future PRR tasks)

- Real Twilio SDK wire-up in `TwilioSmsSender` (today it's a stub per its own docstring — separate hardening task)
- `SesEmailSender` native SDK (today SMTP → SES works via host config change, no code change needed)
- `MetaCloudWhatsAppSender` (separate PRR after ADR on Meta Cloud migration)
- `SendGridEmailSender` / Postmark / Mailgun adapters

## Files to add / modify

**Add**:

- `src/actors/Cena.Actors/Notifications/NotificationsServiceCollectionExtensions.cs`
- `src/actors/Cena.Actors/Notifications/NullEmailSender.cs`
- `src/actors/Cena.Actors/Notifications/NullSmsSender.cs`
- `src/actors/Cena.Actors.Tests/Notifications/NotificationsWiringTests.cs`

**Modify**:

- `src/actors/Cena.Actors.Host/Program.cs` (+1 line)
- `src/actors/Cena.Actors.Host/appsettings.json` (+Notifications section)
- `src/api/Cena.Admin.Api.Host/Program.cs` (+1 line)
- `src/api/Cena.Admin.Api.Host/appsettings.json` (+Notifications section)
- `src/api/Cena.Student.Api.Host/Program.cs` (~3 lines: +extension call, remove the now-redundant IWebPushClient line if moved, or keep both for clarity)
- `src/api/Cena.Student.Api.Host/appsettings.json` (+Notifications section)

## Definition of Done

- [ ] `AddCenaNotifications` extension exists, follows the `AddCenaErrorAggregator` shape
- [ ] `IEmailSender`, `ISmsSender`, `IWhatsAppSender`, `IWhatsAppRecipientLookup`, `INotificationChannelService` resolvable from DI in all 3 hosts
- [ ] `NotificationsWiringTests` green — covers default, each backend, unknown, fallback
- [ ] `dotnet build src/actors/Cena.Actors.sln` green (full sln build gate per user memory 2026-04-13)
- [ ] `dotnet test src/actors/Cena.Actors.sln` green
- [ ] Commit on `claude-code/PRR-428-notifications-di` → merged to `main` (per "always merge to main" memory)

## Non-negotiable references

- `AddCenaErrorAggregator` at [ServiceCollectionExtensions.cs:24](../../src/shared/Cena.Infrastructure/Observability/ErrorAggregator/ServiceCollectionExtensions.cs#L24) — canonical selector pattern
- Memory ["No stubs — production grade"](~/.claude/projects/-Users-shaker-edu-apps-cena/memory/feedback_no_stubs_production_grade.md) — no new stubs; Null variants are explicit graceful-disabled, documented as such
- Memory ["Full sln build gate"](~/.claude/projects/-Users-shaker-edu-apps-cena/memory/feedback_full_sln_build_gate.md) — full sln build before merge
- Memory ["Always merge to main"](~/.claude/projects/-Users-shaker-edu-apps-cena/memory/feedback_always_merge_to_main.md) — don't leave branch hanging
- [docs/ops/peripheral-costs.md](../../docs/ops/peripheral-costs.md) — cost context for future vendor choices enabled by this switcher
