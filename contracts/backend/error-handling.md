# Cena Platform — Global Error Handling Strategy Contract

**Layer:** Cross-cutting | **Runtime:** .NET 9 + Python 3.12 (FastAPI)
**Status:** BLOCKER — no unified error taxonomy; errors are ad-hoc strings

---

## 1. Error Hierarchy

```
CenaException (base)
├── DomainError          — Business rule violations, validation failures
│   ├── ValidationError        — Input validation failed
│   ├── BusinessRuleError      — Domain invariant violated
│   ├── ConceptLockedError     — Prerequisite not met
│   └── BudgetExhaustedError   — Token budget exceeded
├── InfrastructureError  — External system failures
│   ├── DatabaseError          — PostgreSQL / Marten failures
│   ├── NetworkError           — Timeout, DNS, connection refused
│   ├── LLMProviderError       — LLM API errors (rate limit, server error)
│   ├── NatsError              — NATS publish/subscribe failures
│   └── CacheError             — Redis failures
└── UserError            — Auth and rate limit errors
    ├── AuthenticationError    — Invalid/expired token
    ├── AuthorizationError     — Insufficient permissions
    └── RateLimitError         — Too many requests
```

---

## 2. Error Code Registry

All error codes follow the pattern `CENA-{category}{sequence}`.

### CENA-1xxx: Domain Errors

| Code | Name | Description |
|------|------|-------------|
| CENA-1001 | `VALIDATION_FAILED` | Input validation failed (details in `details` field) |
| CENA-1002 | `CONCEPT_LOCKED` | Prerequisite concepts not mastered at gate threshold |
| CENA-1003 | `SESSION_LIMIT_REACHED` | Free tier daily concept limit (2) exceeded |
| CENA-1004 | `BUDGET_EXHAUSTED` | Daily LLM token budget exceeded |
| CENA-1005 | `INVALID_STATE_TRANSITION` | Actor state machine received invalid command |
| CENA-1006 | `CONCEPT_NOT_FOUND` | Concept ID does not exist in knowledge graph |
| CENA-1007 | `METHODOLOGY_EXHAUSTED` | All MCM methodologies attempted, escalation needed |
| CENA-1008 | `DUPLICATE_ATTEMPT` | Idempotency key collision on AttemptConcept |
| CENA-1009 | `SESSION_EXPIRED` | Learning session timed out (30 min inactivity) |
| CENA-1010 | `SUBSCRIPTION_REQUIRED` | Feature requires Premium subscription |

### CENA-2xxx: Infrastructure Errors

| Code | Name | Description |
|------|------|-------------|
| CENA-2001 | `DATABASE_UNAVAILABLE` | PostgreSQL connection failed or timed out |
| CENA-2002 | `DATABASE_CONFLICT` | Optimistic concurrency conflict in Marten |
| CENA-2003 | `NATS_PUBLISH_FAILED` | Failed to publish event to NATS |
| CENA-2004 | `NATS_TIMEOUT` | NATS request timed out |
| CENA-2005 | `CACHE_UNAVAILABLE` | Redis connection failed |
| CENA-2006 | `NEO4J_UNAVAILABLE` | Knowledge graph database unreachable |
| CENA-2007 | `EXTERNAL_SERVICE_DOWN` | Generic external dependency failure |
| CENA-2008 | `STORAGE_WRITE_FAILED` | S3/blob storage write failure |

### CENA-3xxx: Auth Errors

| Code | Name | Description |
|------|------|-------------|
| CENA-3001 | `TOKEN_INVALID` | JWT signature verification failed |
| CENA-3002 | `TOKEN_EXPIRED` | JWT has expired |
| CENA-3003 | `INSUFFICIENT_PERMISSIONS` | Role does not have access to resource |
| CENA-3004 | `ACCOUNT_LOCKED` | Too many failed login attempts |
| CENA-3005 | `RATE_LIMITED` | Request rate limit exceeded |
| CENA-3006 | `SESSION_REVOKED` | Server-side session revocation |

### CENA-4xxx: LLM Errors

| Code | Name | Description |
|------|------|-------------|
| CENA-4001 | `LLM_RATE_LIMITED` | LLM provider rate limit (429) |
| CENA-4002 | `LLM_SERVER_ERROR` | LLM provider server error (5xx) |
| CENA-4003 | `LLM_TIMEOUT` | LLM response exceeded timeout |
| CENA-4004 | `LLM_CONTENT_FILTERED` | Response blocked by safety filter |
| CENA-4005 | `LLM_PARSE_ERROR` | Failed to parse structured LLM response |
| CENA-4006 | `LLM_CIRCUIT_OPEN` | All models in fallback chain are circuit-broken |
| CENA-4007 | `LLM_HEBREW_QUALITY` | Hebrew output quality below threshold |
| CENA-4008 | `LLM_PII_LEAK` | PII detected in LLM response (blocked) |

---

## 3. REST Error Envelope

All REST API errors use a consistent JSON envelope.

```json
{
  "error": {
    "code": "CENA-1002",
    "message_he": "לא ניתן להתחיל נושא זה — יש להשלים קודם את הנושאים הנדרשים",
    "message_ar": "لا يمكن بدء هذا الموضوع - يجب إكمال المواضيع المطلوبة أولا",
    "message_en": "Cannot start this concept — prerequisite concepts are not mastered",
    "details": {
      "concept_id": "calculus-limits-01",
      "missing_prerequisites": ["algebra-functions-03", "trigonometry-basics-02"],
      "prerequisite_gaps": {
        "algebra-functions-03": 0.12,
        "trigonometry-basics-02": 0.28
      }
    },
    "trace_id": "cena-tr-a1b2c3d4-e5f6-7890-abcd-ef1234567890"
  }
}
```

### HTTP Status Code Mapping

| Error Category | HTTP Status |
|---------------|-------------|
| CENA-1xxx (domain) | 400 Bad Request (validation), 409 Conflict (state), 422 Unprocessable |
| CENA-2xxx (infra) | 503 Service Unavailable, 504 Gateway Timeout |
| CENA-3xxx (auth) | 401 Unauthorized, 403 Forbidden, 429 Too Many Requests |
| CENA-4xxx (LLM) | 502 Bad Gateway (provider error), 503 (circuit open) |

---

## 4. SignalR Error Frame

```json
{
  "type": "ErrorEvent",
  "payload": {
    "code": "CENA-1004",
    "message_he": "תקציב הלמידה היומי נגמר — חזור מחר!",
    "message_ar": "انتهت ميزانية التعلم اليومية - عد غدا!",
    "severity": "warning",
    "action": "show_dialog",
    "trace_id": "cena-tr-xxx"
  }
}
```

### Severity Levels

| Severity | Client Behavior |
|----------|----------------|
| `info` | Toast notification, auto-dismiss |
| `warning` | Dialog with "OK" button, session continues |
| `error` | Dialog with action button, may end session |
| `fatal` | Force disconnect, navigate to error screen |

---

## 5. gRPC Status Code Mapping

For .NET actor cluster <-> Python LLM ACL communication.

| Cena Error | gRPC Status | Description |
|------------|-------------|-------------|
| CENA-1001 | `INVALID_ARGUMENT` | Validation failure |
| CENA-1006 | `NOT_FOUND` | Resource not found |
| CENA-1005 | `FAILED_PRECONDITION` | Invalid state for operation |
| CENA-2001 | `UNAVAILABLE` | Transient infrastructure failure |
| CENA-2002 | `ABORTED` | Concurrency conflict, retry |
| CENA-3001 | `UNAUTHENTICATED` | Auth failure |
| CENA-3003 | `PERMISSION_DENIED` | Authorization failure |
| CENA-4003 | `DEADLINE_EXCEEDED` | LLM timeout |
| CENA-4006 | `UNAVAILABLE` | All LLM providers down |
| Any unknown | `INTERNAL` | Unhandled exception |

---

## 6. Retry Policies

| Policy | Applies To | Strategy | Max Retries | Backoff |
|--------|-----------|----------|-------------|---------|
| **Transient** | CENA-2001, 2003, 2004, 2005, 4001, 4002 | Retry with jitter | 3 | Exponential: 200ms, 1s, 5s |
| **Fatal** | CENA-1xxx, 3xxx | No retry, return error | 0 | N/A |
| **Degraded** | CENA-2006 (Neo4j), 4003 (LLM timeout) | Use cached/stale data | 1 | Immediate, then cache |
| **Circuit Break** | 5 failures in 60s for any provider | Open circuit 30s, half-open probe | N/A | 30s open, 1 probe |

### Degraded Mode Behaviors

| Failure | Degraded Behavior |
|---------|-------------------|
| Neo4j down | Use cached knowledge graph (stale up to 1h) |
| LLM timeout | Return last cached response for same concept+mastery range |
| Redis down | Bypass cache, hit database directly (higher latency) |
| NATS down | Buffer events in-memory (up to 1000), flush on reconnect |

---

## 7. Poison Message Handling

Messages that repeatedly fail processing are quarantined.

| Step | Action |
|------|--------|
| 1st failure | Retry immediately (transient policy) |
| 2nd failure | Retry after 5 seconds |
| 3rd failure | Move to NATS dead-letter queue (DLQ) |
| DLQ | Subject: `cena.dlq.{original_subject}` |
| Alert | PagerDuty alert on DLQ message count > 10 in 5 minutes |
| Resolution | Manual inspection via admin dashboard, replay or discard |

### DLQ Message Envelope

```json
{
  "original_subject": "cena.learner.events.attempt",
  "original_payload": { "...original message..." },
  "failure_count": 3,
  "last_error": "CENA-2002: Optimistic concurrency conflict in Marten",
  "first_failed_at": "2026-03-26T10:00:00Z",
  "last_failed_at": "2026-03-26T10:00:12Z",
  "trace_id": "cena-tr-xxx"
}
```

---

## 8. Correlation ID (Trace ID)

A correlation ID (`trace_id`) is generated at the system boundary and propagated through the entire call chain.

### Propagation Path

```
HTTP Request (X-Trace-Id header, or auto-generated)
  -> .NET Middleware (sets AsyncLocal<TraceId>)
    -> Actor Message (TraceId field on all commands/events)
      -> Marten Event (TraceId in metadata)
        -> NATS Publish (TraceId in message headers)
          -> Python LLM ACL (X-Trace-Id gRPC metadata)
            -> LLM Provider (logged, not sent to provider)
```

### Format

```
cena-tr-{uuid-v4}
Example: cena-tr-a1b2c3d4-e5f6-7890-abcd-ef1234567890
```

### Rules

- If inbound request has `X-Trace-Id` header, use it (for client-side correlation).
- If no header, generate a new trace ID at the API gateway.
- All log entries MUST include the trace ID.
- All error responses MUST include the trace ID.
- Marten event metadata MUST include the trace ID for event replay debugging.
- NATS message headers MUST include `Cena-Trace-Id`.

---

## 9. Logging Standards

| Level | When |
|-------|------|
| `Error` | CENA-2xxx, CENA-4xxx — infrastructure failures |
| `Warning` | CENA-1xxx — domain errors (expected in normal flow) |
| `Info` | Successful operations, state transitions |
| `Debug` | Detailed actor message flow, LLM request/response payloads |

### Structured Log Fields (all entries)

```json
{
  "timestamp": "2026-03-26T10:00:00.123Z",
  "level": "Error",
  "trace_id": "cena-tr-xxx",
  "service": "actor-cluster",
  "actor_id": "student/stu_001",
  "error_code": "CENA-2002",
  "message": "Optimistic concurrency conflict on stream student-stu_001",
  "details": { "expected_version": 42, "actual_version": 43 }
}
```
