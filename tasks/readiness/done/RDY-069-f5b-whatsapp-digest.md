# RDY-069 — F5b: WhatsApp parent digest integration

- **Wave**: B (depends on RDY-067 email digest shipping first)
- **Priority**: MED
- **Effort**: 3 engineer-weeks (Oren scoped in panel review)
- **Dependencies**: RDY-067 (email path first); WhatsApp Business API vendor selection
- **Source**: [panel review](../../docs/research/cena-panel-review-user-personas-2026-04-17.md) Round 2.F5 (Oren)

## Problem

Mahmoud (Arabic-L1, small-business father) lives in WhatsApp. Email is low-signal for him. Arab-sector parent engagement in Israeli EdTech is consistently weak on email; WhatsApp is the universal channel.

## Scope

WhatsApp Business API integration extending the RDY-067 digest pipeline to deliver the same content via WhatsApp.

**Operational concerns (Iman flagged):**
- Idempotent retries (no duplicate digests on failed ack)
- Dead-letter queue for invalid numbers
- Rate-limiting per vendor quota
- Quality-based sender reputation monitoring
- Per-template pre-approval flow with WhatsApp (cannot send free-form first)

**Channel preference:**
- Parent chooses email / WhatsApp / both in parent console
- Per-channel opt-out (panel demand: not all-or-nothing)

## Files to Create / Modify

- `src/shared/Cena.Infrastructure/Messaging/IWhatsAppSender.cs` + Twilio/360dialog adapter
- `src/api/Cena.Admin.Api/Features/ParentDigest/ChannelPreference.cs`
- `src/api/Cena.Admin.Api/Features/ParentDigest/WhatsAppDigestDispatcher.cs`
- `src/shared/Cena.Infrastructure/Messaging/DeadLetterQueue.cs`
- `ops/grafana/whatsapp-delivery-dashboard.json` — per Iman's dashboard-for-oncall rule
- `docs/ops/runbooks/whatsapp-delivery.md`

## Acceptance Criteria

- [ ] Approved WhatsApp template for Arabic, Hebrew, English digest (3 pre-approved templates)
- [ ] Idempotent delivery: re-dispatching a digest id does not double-send
- [ ] DLQ captures invalid numbers, surfaces for ops review
- [ ] Grafana dashboard shows delivery rate, failure rate, queue depth
- [ ] Runbook for oncall covers 5 failure modes with remediation steps (Iman's rule)
- [ ] Opt-out link in every message; opt-out honored within 24h

## Success Metrics

- **Delivery rate**: target >97%
- **Parent engagement delta (WhatsApp vs email)**: measure open-equivalent (tap-to-read) rate; expect +15pp for Arab-sector
- **Sender reputation score**: stay in WhatsApp "green" tier

## ADR Alignment

- Shipgate scanner applies to WhatsApp template content identical to email
- ADR-0003 (no misconception codes in content)

## Out of Scope

- SMS fallback (separate task if/when needed)
- Interactive WhatsApp (question/answer flow with parent) — dangerous, defer

## Assignee

Unassigned; Oren leads, Iman for runbook + dashboard.
