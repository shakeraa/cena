# RDY-017: NATS Dead-Letter Queue Stream + TLS

- **Priority**: High — lost events = lost student data
- **Complexity**: Mid engineer
- **Source**: Expert panel audit — Dina (Architecture)
- **Tier**: 2
- **Effort**: 1 week

## Problem

### 1. No DLQ stream
`NatsOutboxPublisher` retries failed events up to 10 times, then logs them as dead-lettered — but doesn't persist them to a DLQ stream. Failed events are lost forever after logging.

### 2. No TLS
NATS uses plaintext auth (`cena_api_user`, `dev_api_pass`). No mutual TLS for wire security. Acceptable for dev/docker-compose, not for production.

## Scope

### 1. Create DLQ JetStream stream

- Stream name: `CENA_DLQ`
- Subject: `cena.durable.dlq.>`
- Retention: 30 days (enough for investigation + replay)
- When outbox publisher exhausts retries, publish to DLQ stream instead of just logging

### 2. DLQ monitoring

- Add OTel counter for dead-lettered events
- Health dashboard section showing DLQ depth
- Alert when DLQ depth exceeds threshold

### 3. DLQ replay tool

- CLI command or admin endpoint to replay DLQ events back to their original stream
- Supports filtering by subject, time range, event type

### 4. TLS configuration

- Add TLS configuration to `CenaNatsOptions`
- Support for client certificates (mutual TLS)
- Document TLS setup for production deployment
- Keep plaintext auth as dev-only fallback

## Files to Modify

- `src/shared/Cena.Infrastructure/Messaging/NatsOutboxPublisher.cs` — publish to DLQ on exhaustion
- `src/actors/Cena.Actors.Host/Program.cs` — register DLQ stream
- `src/shared/Cena.Infrastructure/Messaging/CenaNatsOptions.cs` — add TLS fields
- New: `scripts/nats-dlq-replay.sh` — replay tool

## Acceptance Criteria

- [ ] DLQ stream created with 30-day retention
- [ ] Dead-lettered events published to DLQ (not just logged)
- [ ] OTel counter for DLQ events
- [ ] DLQ depth visible in health dashboard
- [ ] Replay tool can re-publish DLQ events to original streams
- [ ] TLS configuration available in `CenaNatsOptions`
- [ ] Dev environment continues to work with plaintext (no breaking change)
