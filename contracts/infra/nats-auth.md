# Cena Platform — NATS Authorization Contract

**Layer:** Infrastructure / Messaging | **Provider:** Synadia Cloud (NATS)
**Status:** BLOCKER — all services share one NATS credential with full pub/sub access

---

## 1. Authentication Model

NATS uses decentralized JWT-based authentication with NKey cryptographic identities.

| Component | Description |
|-----------|-------------|
| Operator | Cena platform operator (top-level trust anchor) |
| Accounts | Isolated security domains per service |
| Users | Per-instance credentials within an account |
| NKeys | Ed25519 key pairs for signing JWTs |

### Key Hierarchy

```
Operator: cena-operator (NKey: OA...)
├── Account: actor-cluster (NKey: AA...)
│   ├── User: actor-cluster-prod-1 (NKey: UA...)
│   ├── User: actor-cluster-prod-2 (NKey: UA...)
│   └── User: actor-cluster-staging (NKey: UA...)
├── Account: outreach (NKey: AA...)
│   └── User: outreach-prod-1 (NKey: UA...)
├── Account: analytics (NKey: AA...)
│   └── User: analytics-prod-1 (NKey: UA...)
├── Account: content (NKey: AA...)
│   └── User: content-prod-1 (NKey: UA...)
└── Account: admin (NKey: AA...)
    └── User: admin-debug (NKey: UA...)
```

---

## 2. Account Isolation

Each service runs under a separate NATS account. Accounts are isolated by default -- no cross-account messaging unless explicitly configured via exports/imports.

| Account | Service | Purpose |
|---------|---------|---------|
| `actor-cluster` | .NET Actor Cluster | Core domain events, actor commands |
| `outreach` | Outreach Scheduler | WhatsApp, push notifications, spaced repetition |
| `analytics` | Analytics Pipeline | Event consumption for dashboards, ML retraining |
| `content` | Content Service | Curriculum graph updates, content delivery |
| `admin` | Admin/Debug Tools | Debugging, message replay (restricted environments) |

---

## 3. Subject Namespace

All NATS subjects follow the convention: `cena.{domain}.{entity}.{event_type}`.

| Subject Pattern | Description | Publisher |
|-----------------|-------------|-----------|
| `cena.learner.events.*` | Student learning events (attempts, mastery, sessions) | actor-cluster |
| `cena.learner.commands.*` | Commands to student actors | actor-cluster |
| `cena.outreach.scheduled.*` | Scheduled outreach messages | outreach |
| `cena.outreach.whatsapp.*` | WhatsApp inbound/outbound events | outreach |
| `cena.analytics.events.*` | Aggregated analytics events | analytics |
| `cena.content.updates.*` | Curriculum graph changes | content |
| `cena.system.health.*` | Health check and monitoring | all accounts |
| `cena.dlq.*` | Dead-letter queue messages | actor-cluster |

---

## 4. Per-Account Permissions

### Account: actor-cluster

```
Publish Allow:
  - cena.learner.events.>
  - cena.learner.commands.>
  - cena.dlq.>
  - cena.system.health.actor-cluster

Subscribe Allow:
  - cena.learner.commands.>
  - cena.outreach.whatsapp.inbound
  - cena.content.updates.>
  - cena.system.health.>

Publish Deny:
  - cena.outreach.scheduled.>
  - cena.analytics.events.>
```

### Account: outreach

```
Publish Allow:
  - cena.outreach.scheduled.>
  - cena.outreach.whatsapp.outbound
  - cena.system.health.outreach

Subscribe Allow:
  - cena.learner.events.>          (read-only: consume learning events for scheduling)
  - cena.outreach.whatsapp.inbound
  - cena.system.health.>

Publish Deny:
  - cena.learner.events.>          (CRITICAL: outreach CANNOT publish learner events)
  - cena.learner.commands.>
  - cena.content.updates.>
```

### Account: analytics

```
Publish Allow:
  - cena.analytics.events.>
  - cena.system.health.analytics

Subscribe Allow:
  - cena.learner.events.>          (read-only: consume for dashboards)
  - cena.outreach.scheduled.>     (read-only: outreach effectiveness metrics)
  - cena.system.health.>

Publish Deny:
  - cena.learner.events.>
  - cena.learner.commands.>
  - cena.outreach.>
  - cena.content.>
```

### Account: content

```
Publish Allow:
  - cena.content.updates.>
  - cena.system.health.content

Subscribe Allow:
  - cena.content.updates.>
  - cena.system.health.>

Publish Deny:
  - cena.learner.>
  - cena.outreach.>
  - cena.analytics.>
```

### Account: admin

```
Publish Allow:
  - cena.>                         (full access for debugging)

Subscribe Allow:
  - cena.>                         (full access for debugging)

Restrictions:
  - Admin account credentials ONLY provisioned in dev and staging environments.
  - Production: admin account disabled by default; enabled via break-glass procedure.
  - All admin actions logged to audit trail.
```

---

## 5. Cross-Account Exports/Imports

Since accounts are isolated, cross-account messaging uses NATS export/import declarations.

| Export (from) | Import (to) | Subject | Type |
|---------------|-------------|---------|------|
| actor-cluster | outreach | `cena.learner.events.>` | Stream (read-only) |
| actor-cluster | analytics | `cena.learner.events.>` | Stream (read-only) |
| outreach | actor-cluster | `cena.outreach.whatsapp.inbound` | Stream |
| outreach | analytics | `cena.outreach.scheduled.>` | Stream (read-only) |
| content | actor-cluster | `cena.content.updates.>` | Stream (read-only) |

### Export Configuration (actor-cluster account)

```
Exports:
  - subject: cena.learner.events.>
    type: stream
    accounts: [outreach, analytics]
    response_type: singleton   # No request-reply, stream only
```

---

## 6. JWT Token Management

### Token Lifecycle

| Parameter | Value |
|-----------|-------|
| User JWT validity | 90 days |
| Rotation | New JWT issued 7 days before expiry |
| Revocation | Via operator-signed revocation list |
| Storage | AWS Secrets Manager (per environment) |

### Credential File Structure

Each service has a credentials file (`.creds`) containing:

```
-----BEGIN NATS USER JWT-----
eyJ0eXAiOiJKV1QiLCJhbGciOiJlZDI1NTE5LW5rZXkifQ...
------END NATS USER JWT------

-----BEGIN USER NKEY SEED-----
SUAM...
------END USER NKEY SEED------
```

### Credential Distribution

1. Operator generates account + user JWTs using `nsc` CLI.
2. Credentials stored in AWS Secrets Manager: `cena/{env}/nats/{account}/{user}`.
3. Service loads credentials from Secrets Manager on startup.
4. Credentials are never stored in container images or git.

---

## 7. Connection Limits

| Account | Max Connections | Max Payload | Max Subscriptions |
|---------|----------------|-------------|-------------------|
| actor-cluster | 50 | 1 MB | 1,000 |
| outreach | 10 | 256 KB | 200 |
| analytics | 20 | 1 MB | 500 |
| content | 5 | 512 KB | 100 |
| admin | 2 | 1 MB | unlimited |

---

## 8. JetStream Authorization

JetStream streams and consumers inherit account-level permissions.

| Stream | Account Owner | Consumers |
|--------|---------------|-----------|
| `LEARNER_EVENTS` | actor-cluster | outreach (push), analytics (pull) |
| `OUTREACH_EVENTS` | outreach | analytics (pull) |
| `CONTENT_UPDATES` | content | actor-cluster (push) |
| `DLQ` | actor-cluster | admin (pull, manual inspection) |

### Consumer Permissions

- Consumers can only be created by the stream's owning account or the admin account.
- Consumer ACK operations are scoped to the consumer's account.
- Durable consumer names follow: `{account}-{purpose}` (e.g., `outreach-review-scheduler`).

---

## 9. Monitoring & Audit

| Metric | Alert Threshold |
|--------|-----------------|
| Authorization failures | > 5 in 1 minute (per account) |
| Connection count per account | > 80% of max connections |
| Credential expiry | < 14 days remaining |
| Cross-account export latency | > 100ms |

### Audit Log

All authorization denials are logged:

```json
{
  "timestamp": "2026-03-26T10:00:00Z",
  "event": "authorization_denied",
  "account": "outreach",
  "user": "outreach-prod-1",
  "action": "publish",
  "subject": "cena.learner.events.attempt",
  "reason": "publish denied by account permissions",
  "client_ip": "10.0.1.42"
}
```

---

## 10. Break-Glass Procedure (Production Admin)

1. On-call engineer requests admin access via PagerDuty incident.
2. Two-person approval required (engineering lead + security).
3. Admin credentials enabled in production Secrets Manager (TTL: 4 hours).
4. All admin NATS operations logged with engineer identity.
5. After incident: admin credentials rotated and disabled.
6. Post-incident review of all admin NATS operations.
