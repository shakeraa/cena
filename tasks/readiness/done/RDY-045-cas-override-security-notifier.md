# RDY-045: CAS Override — Security-Team Notifier

- **Priority**: High — RDY-036 §9 unfulfilled
- **Complexity**: Low
- **Effort**: 2-3 hours

## Problem

`CasOverrideEndpoint` emits only a SIEM log on override. RDY-036 §9 required Slack/email notification to the security team on every override event.

## Scope

- `ISecurityNotifier` interface in `Cena.Admin.Api` (or shared infra if needed)
- `SlackWebhookSecurityNotifier` implementation reading `CENA_SECURITY_SLACK_WEBHOOK`
- `NullSecurityNotifier` fallback when env var is unset (so dev / CI isn't blocked)
- `CasOverrideEndpoint.Handle` fires the notification fire-and-forget; failures log warning
- Unit test asserts the notifier is invoked on override + not invoked on normal authoring

## Acceptance

- [ ] Notification fires on every override
- [ ] Webhook failures do not fail the request
- [ ] Test covers both normal and override paths
