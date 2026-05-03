# TASK-PRR-310: `SubscriptionTier` propagation (student-api / actor-host / LLM router)

**Priority**: P0 — launch-blocker
**Effort**: M (1-2 weeks)
**Lens consensus**: all personas (foundation for tier enforcement)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev (cross-service)
**Tags**: epic=epic-prr-i, tier-enforcement, priority=p0, launch-blocker
**Status**: Ready
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Propagate the student's effective `SubscriptionTier` + `UsageCaps` through every backend surface that enforces tier behavior: student-api, actor-host (via NATS), LLM router, diagnostic pipeline, content pipeline.

## Scope

- `SubscriptionTier` pinned on every `SessionContext` at session start; immutable for session duration.
- NATS request envelope includes tier + caps headers on every student→actor call.
- LLM router reads tier from context (consumed by [PRR-311](TASK-PRR-311-per-tier-llm-routing-policy.md)).
- Diagnostic caps enforced at photo-upload intake boundary ([PRR-312](TASK-PRR-312-per-tier-photo-diagnostic-caps.md)).
- Tier change mid-session: honor current session's pinned tier; new tier applies next session (predictable UX, avoids mid-session caps).
- Test harness for each tier to verify caps enforce correctly.

## Files

- `src/backend/Cena.Domain/Subscriptions/SubscriptionTier.cs`
- `src/backend/Cena.Domain/Sessions/SessionContext.cs` — add tier field
- `src/backend/Cena.Actors/Coordination/SessionActor.cs` — pin tier at session start
- NATS envelope enrichment layer
- Tests: session spans tier upgrade correctly, NATS headers populated, LLM router reads tier.

## Definition of Done

- `SessionContext` carries tier in every request end-to-end.
- Tier upgrade during session does not apply until session end.
- Integration test: Basic session hits Sonnet escalation cap, receives fallback.
- Full sln + test green.

## Non-negotiable references

- Project convention: session-scoped state (ADR-0003 relevant for derived data).
- Memory "Labels match data" — UI tier matches enforced tier.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-291](TASK-PRR-291-tier-feature-matrix-data.md) — source of truth
- [PRR-311](TASK-PRR-311-per-tier-llm-routing-policy.md), [PRR-312](TASK-PRR-312-per-tier-photo-diagnostic-caps.md)
