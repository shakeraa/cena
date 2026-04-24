---
id: FIND-ARCH-018
task_id: t_25df87c51509
severity: P0 — Critical
lens: arch
tags: [reverify, arch, stub]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-arch-018: NotificationChannelService stub channels (web push, email, SMS)

## Summary

NotificationChannelService stub channels (web push, email, SMS)

## Severity

**P0 — Critical**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

**Goal**: Replace the three "Would send" stubs in NotificationChannelService
with real Web Push, SMTP/SendGrid, and Twilio implementations.
The user has banned stubs in production paths.

**Files to read first**:
  - src/actors/Cena.Actors/Notifications/NotificationChannelService.cs
  - src/actors/Cena.Actors/Notifications/NotificationDispatcher.cs
  - src/shared/Cena.Infrastructure/Documents/NotificationDocuments.cs
  - src/actors/Cena.Actors/Events/NotificationEvents.cs

**Files to touch**:
  - src/actors/Cena.Actors/Notifications/NotificationChannelService.cs
    (replace SendWebPushAsync, SendEmailAsync, SendSmsAsync)
  - src/actors/Cena.Actors/Notifications/IWebPushClient.cs (new)
  - src/actors/Cena.Actors/Notifications/WebPushClient.cs (new — wraps WebPush.NetCore)
  - src/actors/Cena.Actors/Notifications/SmtpEmailSender.cs (new — MailKit)
  - src/actors/Cena.Actors/Notifications/TwilioSmsSender.cs (new)
  - appsettings.Development.json (add VAPID + SMTP + Twilio sections,
    leave keys blank for dev)
  - src/actors/Cena.Actors.Tests/Notifications/NotificationChannelServiceTests.cs (new)

**Definition of Done**:
  - [ ] `grep -n "Would send\|stub implementation\|Simulate async work" src/actors/Cena.Actors/Notifications/` returns zero
  - [ ] All three Send* methods return `false` on failure with a
        specific error reason (not just a boolean)
  - [ ] Notification persistence still happens BEFORE the channel
        send attempt, so a failed send does not lose the in-app row
  - [ ] Integration test asserts each channel calls its real client
  - [ ] Per-tenant + global rate limit on each channel (cost guardrail)

**Reporting requirements**:
  - In your --result, paste a sample log line from each successful
    channel showing the structured fields (`channel`, `notification_id`,
    `result`, `error_code`).
  - Confirm the in-app fallback still works when an external channel
    is down.

**Reference**: FIND-arch-018 in docs/reviews/agent-arch-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-arch-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_25df87c51509`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
