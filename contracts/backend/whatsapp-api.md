# Cena Platform — WhatsApp Business API Integration Contract

**Layer:** Backend / Outreach | **Runtime:** .NET 9 (OutreachSchedulerActor)
**Provider:** Meta Cloud API v21.0 (primary), Twilio WhatsApp (fallback)
**Status:** BLOCKER — outreach actor has no delivery channel defined

---

## 1. Provider Configuration

| Setting | Value |
|---------|-------|
| Primary API | Meta Cloud API (`graph.facebook.com/v21.0/{PHONE_NUMBER_ID}/messages`) |
| Fallback API | Twilio WhatsApp (`api.twilio.com/2010-04-01/Accounts/{SID}/Messages`) |
| Business Phone | Dedicated Israeli number (+972-XX-XXX-XXXX) |
| WABA ID | Configured in Meta Business Manager |
| Failover trigger | 3 consecutive Meta API failures within 5 minutes |

### Environment Variables (never hardcoded)

```
WHATSAPP_META_TOKEN         # Meta Cloud API bearer token
WHATSAPP_META_PHONE_ID      # Phone number ID
WHATSAPP_TWILIO_SID         # Twilio account SID (fallback)
WHATSAPP_TWILIO_AUTH_TOKEN  # Twilio auth token (fallback)
WHATSAPP_TWILIO_FROM        # Twilio WhatsApp sender number
```

---

## 2. Message Types

| Type | API Category | Use Case | Template Required |
|------|-------------|----------|-------------------|
| Text | Business-initiated | Streak warning, break reminder | Yes (outside 24h window) |
| Template | Business-initiated | Review quiz notification | Yes |
| Interactive (buttons) | Business-initiated | Inline quiz (2-4 answer choices) | Yes |
| Free-form text | User-initiated | Student replies within 24h window | No |

### Interactive Message Structure (Inline Quiz)

```json
{
  "type": "interactive",
  "interactive": {
    "type": "button",
    "header": { "type": "text", "text": "Quiz: {concept_name_he}" },
    "body": { "text": "{question_text_he}" },
    "action": {
      "buttons": [
        { "type": "reply", "reply": { "id": "ans_a", "title": "A" } },
        { "type": "reply", "reply": { "id": "ans_b", "title": "B" } },
        { "type": "reply", "reply": { "id": "ans_c", "title": "C" } }
      ]
    }
  }
}
```

---

## 3. Pre-Approved Message Templates

All templates must be submitted to Meta for approval before use.

| Template Name | Language | Trigger | Content Pattern |
|---------------|----------|---------|-----------------|
| `cena_streak_warning` | he, ar | Streak at risk (no session in 20h) | "Hey {name}, your {streak_count}-day streak ends in 4 hours! Tap to continue." |
| `cena_review_quiz` | he, ar | HLR recall below 0.85 threshold | "Time to review {concept_name}! Quick 3-question quiz inside." |
| `cena_stagnation_break` | he, ar | Stagnation score > 0.7 | "Taking a short break helps learning. Come back in {cooldown_min} minutes." |
| `cena_methodology_switch` | he, ar | Methodology switched | "We're trying a new approach for {concept_name}: {methodology_label}." |
| `cena_welcome` | he, ar | New user opt-in | "Welcome to Cena! Tap to start your first math session." |

### Template Variables

- `{name}` — Student's first name (from profile)
- `{streak_count}` — Current streak day count
- `{concept_name}` — Localized concept name
- `{cooldown_min}` — Cooldown duration in minutes
- `{methodology_label}` — Localized methodology display name

---

## 4. Webhook: Receiving Student Responses

### Webhook Endpoint

```
POST /api/webhooks/whatsapp
```

### Verification (Meta)

- Meta sends `GET` with `hub.mode=subscribe`, `hub.verify_token`, `hub.challenge`.
- Server validates `hub.verify_token` matches configured secret.
- Returns `hub.challenge` as plain text with 200 OK.

### Inbound Message Processing

1. Receive webhook POST from Meta.
2. Validate `X-Hub-Signature-256` header against app secret (HMAC-SHA256).
3. Parse message payload: extract `from` (phone), `type`, `text`/`button.reply.id`.
4. Look up student by phone number (phone -> student_id mapping in PostgreSQL).
5. Publish to NATS subject: `cena.outreach.whatsapp.inbound`.
6. OutreachSchedulerActor subscribes and routes to appropriate StudentActor.

### NATS Message Schema

```json
{
  "student_id": "stu_001",
  "phone": "+972501234567",
  "message_type": "button_reply",
  "button_id": "ans_b",
  "text": null,
  "template_context": "cena_review_quiz",
  "concept_id": "quad-equations-01",
  "timestamp": "2026-03-26T14:30:00Z",
  "whatsapp_message_id": "wamid.xxx"
}
```

---

## 5. Rate Limits

| Category | Limit | Notes |
|----------|-------|-------|
| Business-initiated (templates) | 80 messages/second | Applies to all template messages |
| User-initiated (24h window) | 1,000 messages/second | Within 24h of last user message |
| Daily per-user cap (Cena policy) | 5 messages/day | Prevent notification fatigue |
| Quiet hours | No messages 22:00-07:00 IST | Israeli market expectation |

### Backpressure Strategy

- OutreachSchedulerActor maintains a send queue with token bucket rate limiter.
- If Meta returns 429: exponential backoff (1s, 2s, 4s, max 30s).
- If backlog exceeds 10,000 messages: drop lowest-priority messages (stagnation_break first).

---

## 6. Opt-In / Opt-Out

WhatsApp Business Policy requires explicit opt-in before sending business-initiated messages.

### Opt-In Flow

1. Parent creates account in Cena app.
2. During onboarding: "Enable WhatsApp notifications?" toggle with explanation.
3. On opt-in: send `cena_welcome` template to confirm.
4. Store consent: `whatsapp_opted_in: true`, `opted_in_at: timestamp` in parent profile.

### Opt-Out Flow

1. User sends "STOP" or "הפסק" or "توقف" to the Cena WhatsApp number.
2. Webhook receives message, triggers opt-out.
3. Update parent profile: `whatsapp_opted_in: false`, `opted_out_at: timestamp`.
4. Send final confirmation: "You've been unsubscribed from Cena notifications."
5. No further business-initiated messages until re-opt-in.

### Compliance

- Maintain opt-in audit log (who, when, how) for Meta compliance reviews.
- Re-consent required if message templates change significantly.
- Double opt-in recommended for GDPR-adjacent Israeli privacy law compliance.

---

## 7. Localization

| Language | Direction | Template Suffix | Number Format |
|----------|-----------|-----------------|---------------|
| Hebrew (he) | RTL | `_he` | Standard |
| Arabic (ar) | RTL | `_ar` | Eastern Arabic numerals optional |

### Template Localization Rules

- Each template is submitted in both Hebrew and Arabic as separate templates.
- Language selected based on `locale` claim in student/parent JWT.
- Fallback: Hebrew if locale is not set.
- Mathematical expressions rendered as images (MathJax -> PNG) since WhatsApp does not support LaTeX.

---

## 8. Error Handling

| Error | Action |
|-------|--------|
| Phone number not on WhatsApp | Mark as `whatsapp_unreachable`, skip future sends |
| Template not approved | Alert ops, use fallback text-only message in 24h window |
| Rate limit exceeded (429) | Exponential backoff, re-queue |
| Webhook signature invalid | Reject with 403, log security event |
| Student not found for phone | Log orphan message, do not process |

### Delivery Tracking

- Track `sent`, `delivered`, `read`, `failed` statuses via Meta webhook callbacks.
- Store per-message status in outreach events table.
- Alert if delivery rate drops below 90% over 1-hour window.

---

## 9. Monitoring

| Metric | Alert Threshold |
|--------|-----------------|
| Message send latency (P95) | > 2 seconds |
| Delivery failure rate | > 10% over 1 hour |
| Webhook processing latency | > 500ms |
| Daily opt-out rate | > 5% (indicates notification fatigue) |
| Template rejection rate | Any rejection triggers ops alert |
