# Cena — Peripheral Service Costs

> **Purpose**: single reference for the monthly cost of every external service the platform depends on. Updated when we switch vendors or renegotiate pricing.
>
> **Volume baseline** (unless otherwise noted): **1,000 active parents, 10,000 active students, 40,000 parent-digest deliveries/month**. Scale numbers up linearly for larger cohorts.
>
> **Last reviewed**: 2026-04-22 (ADR-0058 rollout)

---

## 1. Email — SMTP deliverability layer

Current code: [SmtpEmailSender.cs](../../src/actors/Cena.Actors/Notifications/SmtpEmailSender.cs) (MailKit; vendor-agnostic, points at any SMTP host).

| Vendor | Setup | Cost @ 10k/mo | Cost @ 100k/mo | Deliverability | Notes |
|---|---|---|---|---|---|
| **AWS SES** ⭐ recommended | DNS + sandbox removal (1 day) | **$1.00** | **$10.00** | Excellent with SPF/DKIM/DMARC | Point existing sender at `email-smtp.<region>.amazonaws.com:587`. $0.10 / 1,000 emails. |
| Postmark | 1 hour | $15 | $50 (tier) | Best-in-class (transactional-only policy) | Parent digest fits "transactional" per their TOS. |
| SendGrid | 2 hours | $19.95 | $89.95 | Good | More spam-reporting noise than Postmark. |
| Mailgun | 1 hour | $15 | $35 | Good | Pay-as-you-go option available. |
| Self-host Postfix on VPS | 3+ days ongoing | $5 VPS | $5 VPS + outages | Poor | Cold IP → Gmail spam. **Not viable.** |
| Google Workspace relay | Included in GSuite | $0 | N/A | Good | Hard cap 2k/day/user. Not production-grade. |

**Decision**: AWS SES for launch. ~$10/month at projected volume. Reassess at 500k/mo.

## 2. SMS — Twilio

Current code: [TwilioSmsSender.cs](../../src/actors/Cena.Actors/Notifications/TwilioSmsSender.cs).

SMS is used sparingly — CAS override security notifications ([RDY-045](../../tasks/readiness/done/RDY-045-cas-override-security-notifier.md)) and account-recovery only. **Not** a digest channel.

| Line item | Cost | Notes |
|---|---|---|
| Outbound SMS (US) | $0.0079 / msg | |
| Outbound SMS (Israel) | ~$0.055 / msg | Carrier-variable; budget $0.06 for safety. |
| Outbound SMS (UAE, SA) | ~$0.10 / msg | Premium-carrier market. |
| Toll-free number | $2 / month | One per region. |
| A2P 10DLC registration (US) | $44 one-time + $0.0025 / msg carrier fee | Required for US business SMS; one-time hassle. |
| Short code (US) | $500–$1,500 / month | **Not needed** for Cena — standard number is fine. |

**Monthly baseline at 500 messages/month (security notifications):** ~$4 + $2 number = **~$6/mo**.

## 3. WhatsApp — Meta Cloud API (direct, not via Twilio)

Current code: [WhatsAppChannel.cs](../../src/actors/Cena.Actors/ParentDigest/WhatsAppChannel.cs) + [TwilioWhatsAppSender.cs](../../src/actors/Cena.Actors/ParentDigest/TwilioWhatsAppSender.cs). Interface is vendor-neutral; Meta direct adapter planned.

WhatsApp bills per **conversation** (24-hour window per parent, unlimited messages inside).

| Path | Utility convo | Marketing convo | Platform fee | Free tier |
|---|---|---|---|---|
| **Meta Cloud API (direct)** ⭐ | ~$0.014 (IL/AR) | ~$0.059 (IL/AR) | $0 | 1,000 utility convos/mo free |
| Twilio → Meta | ~$0.014 + $0.005 Twilio/msg | ~$0.059 + $0.005 Twilio/msg | $0 | No |
| 360dialog | Meta pass-through | same | €49 / month flat | No |

**Parent weekly digest math at 1,000 parents:**
- 1,000 parents × 4 weeks = 4,000 utility conversations/month
- 1,000 free tier → 3,000 billable × $0.014 = **~$42/mo direct**
- Via Twilio: ~$42 + (3,000 × $0.005) = **~$57/mo**
- Via 360dialog: ~$42 + €49 = **~$95/mo** (only worth it at much higher volume)

**Decision**: Meta Cloud API direct. Twilio stays as fallback sender for 30 days post-migration, then drop.

## 4. Web Push — self-served (VAPID)

Current code: [WebPushClient.cs](../../src/actors/Cena.Actors/Notifications/WebPushClient.cs) + [PushNotificationRateLimiter.cs](../../src/actors/Cena.Actors/Notifications/PushNotificationRateLimiter.cs).

| Channel | Cost |
|---|---|
| Web Push (VAPID, self-served) | **$0** |
| Firebase Cloud Messaging (mobile push) | $0 |
| Apple Push Notification service | $0 (covered by Apple Developer $99/yr) |
| OneSignal / Pusher Beams (SaaS alternative) | $9–$99 / month — **not needed** |

## 5. Error aggregator — Sentry SaaS (EU region)

See [ADR-0058](../adr/0058-error-aggregator-sentry.md).

| Tier | Price | Errors/mo | Users | Notes |
|---|---|---|---|---|
| Developer (free) | $0 | 5k | 1 | Good for solo dev; not production. |
| **Team** ⭐ | **$26/mo** (annual) / $31/mo | 50k | Unlimited | Our pick. Source-layer scrubbing keeps event count well under 50k. |
| Business | $80/mo | 100k | Unlimited | Upgrade when we outgrow Team. |
| Self-hosted | $0 licence + **~$50–$100/mo infra** | Unbounded | Unbounded | 20+ container stack — rejected in ADR-0058 for ops burden. |

**Decision**: Sentry Team SaaS, EU region. **$26/mo** committed annually.

## 6. Observability stack — Serilog + OpenTelemetry + Grafana

Current state: Serilog writes structured logs to stdout/file; OTel traces exported (configurable endpoint). No hosted aggregator.

| Component | Current | Alternative SaaS | Notes |
|---|---|---|---|
| Structured logs | Serilog → file / stdout | Grafana Loki Cloud: free 50GB/mo, then $0.50/GB | Fine on self-host until 500GB/mo. |
| Distributed traces | OTel → configurable collector | Grafana Tempo Cloud (free tier), Honeycomb free 20M events/mo | Honeycomb is the "just works" choice at $0 for our scale. |
| Metrics / dashboards | OTel → Prometheus-compatible | Grafana Cloud free: 10k series, 10k metrics/hour | Sufficient. Paid tier starts $49/mo. |

**Baseline**: stay on **free tier** across Grafana Cloud + Honeycomb until we exceed it. **$0/mo**.

## 7. Per-feature success metrics — RDY-078 dashboards

See [RDY-078](../../tasks/readiness/done/RDY-078-action-feature-metrics-framework.md).

| Item | Cost |
|---|---|
| Grafana dashboards (self-host / free tier) | $0 |
| Mixpanel / Amplitude (if we need product analytics) | $0 for free tier (< 20M events/mo); $25–$2,000+ thereafter |

**Decision**: avoid Mixpanel/Amplitude unless product team specifically requests cohort analysis. The OTel metrics framework covers everything RDY-078 calls out.

## 8. LLM / AI services (separate cost centre — reference only)

These are NOT peripherals but drive the single largest line item. Reference here so the full monthly envelope is visible.

| Service | Tier-2 (Haiku) | Tier-3 (Sonnet/Opus) | Monthly estimate |
|---|---|---|---|
| Anthropic Claude (3-tier routing per [ADR-0026](../adr/0026-llm-three-tier-routing.md)) | $0.80 / M input, $4 / M output | Sonnet: $3 / M in, $15 / M out. Opus: $15 / M in, $75 / M out. | Highly variable — budget **$200–$2,000/mo** at launch scale. Dominates peripheral cost. |

## 9. Firebase Auth

Current: [user memory](../../.claude/projects/-Users-shaker-edu-apps-cena/memory/project_admin_dashboard.md). Firebase Auth free tier covers 50k MAU for email/password + social.

| Service | Free tier | Paid | Notes |
|---|---|---|---|
| Firebase Auth | 50k MAU free | $0.0055 / MAU after | Sufficient until we cross 50k students. |
| Phone auth SMS (if we enable it) | 10k / mo free | $0.01 / msg | Not currently enabled. |

## 10. Infrastructure (AWS / hosting)

Out of scope for this doc — tracked separately in `docs/ops/capacity/` and the deployment runbooks. Peripheral services here assume the .NET hosts + SPAs + Postgres / Marten + Redis / NATS all run on existing infrastructure with no marginal cost per peripheral call.

---

## Total at baseline (1,000 parents / 10,000 students)

| Category | Low | High |
|---|---|---|
| Email (SES) | $1 | $10 |
| SMS (Twilio) | $6 | $30 |
| WhatsApp (Meta direct) | $42 | $85 |
| Web Push | $0 | $0 |
| Sentry | $26 | $26 |
| Observability (Grafana/Honeycomb free tier) | $0 | $49 |
| Firebase Auth | $0 | $0 |
| **Peripherals subtotal** | **~$75/mo** | **~$200/mo** |
| LLM (separate cost centre) | $200 | $2,000 |
| **Grand total** | **~$275/mo** | **~$2,200/mo** |

## At 10x scale (10,000 parents / 100,000 students)

| Category | Estimate |
|---|---|
| Email (SES, 400k emails/mo) | ~$40 |
| SMS | ~$100 |
| WhatsApp (40k convos/mo, 1k free) | ~$550 |
| Sentry Business tier | $80 |
| Observability (Grafana paid) | ~$100 |
| Firebase Auth (>50k MAU) | ~$300 |
| **Peripherals subtotal** | **~$1,170/mo** |
| LLM (3-tier routed) | $5k–$30k |
| **Grand total** | **~$6k–$31k/mo** |

## Change log

| Date | Change | Source |
|---|---|---|
| 2026-04-22 | Initial document — peripherals audit follow-up + ADR-0058 Sentry decision + Meta-direct WhatsApp decision | Peripherals audit conversation |
