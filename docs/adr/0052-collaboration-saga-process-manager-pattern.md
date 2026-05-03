# ADR-0052 — Saga / process-manager pattern for cross-student collaboration flows

- **Status**: Accepted
- **Date**: 2026-04-21
- **Decision Makers**: Shaker (project owner), persona-enterprise, persona-redteam
- **Task**: prr-023
- **Related**: [ADR-0001](0001-multi-institute-enrollment.md) (tenancy),
  [ADR-0003](0003-misconception-session-scope.md) (session scope),
  [ADR-0012](0012-aggregate-decomposition.md) (aggregate boundaries),
  [ADR-0038](0038-event-sourced-right-to-be-forgotten.md) (RTBF)

---

## Context

Several pre-release workstreams ask StudentActor to coordinate cross-student
flows:

- **Peer-explain** (F4) — student A records an explanation, student B receives
  it, both actors must book-keep the exchange.
- **Collab whiteboard** (axis-7) — N students co-edit a shared canvas tied to a
  session.
- **Team challenge** (axis-7) — a small cohort runs a shared item set, partial
  answers tally to a group score.
- **Peer-confused-too signal** (prr-159) — a session-scoped aggregate count
  that fans in from N students.

The naive implementation drops shared state onto StudentActor
(`PendingPeerHandoffs`, `CurrentWhiteboardId`, `TeamGroupMembers`, etc.). That
breaks three invariants:

1. **Aggregate cohesion** ([ADR-0012](0012-aggregate-decomposition.md))  —
   StudentActor's job is a single student's pedagogy state, not the state of
   pairs/triads/groups. Stuffing cross-student state in makes it a god-actor.
2. **RTBF** ([ADR-0038](0038-event-sourced-right-to-be-forgotten.md)) — when
   student B requests deletion, any cross-student state on student A's stream
   becomes a leaked reference we can't clean without rewriting A's history.
3. **Event ordering** — two students emitting events against each other's
   streams creates a cross-actor write ordering problem that actor-framework
   mailboxes do not solve.

We need a coordination seam that is:

- An aggregate with its own identity (so it has a bounded lifetime and a DLQ
  target when it wedges).
- Event-sourced (so RTBF can redact a participant without reconstructing the
  other participants' views).
- Tenant-scoped (so school A's peer-explain never sees school B's students).
- Bounded in retention (so we do not accumulate dead process state).

## Decision

Use the **saga / process-manager pattern** ([Hohpe & Woolf 2003], [Vernon 2013
ch.10]) for every cross-student flow. Each collaboration flow is modelled as a
**CollabSaga aggregate** with its own actor, its own event stream, its own
compensation actions, and its own timeout policy.

### Pattern shape

```
┌─────────────┐         ┌──────────────────┐         ┌─────────────┐
│ StudentActor│ ◄──────►│  CollabSaga      │◄───────►│StudentActor │
│     A       │  events │ (process-manager)│  events │     B       │
└─────────────┘         └──────────────────┘         └─────────────┘
                                 │
                                 │ timeouts, compensations,
                                 │ DLQ on wedge
                                 ▼
                        ┌──────────────────┐
                        │  SagaCoordinator │
                        │  (host-wide)     │
                        └──────────────────┘
```

Participating StudentActors communicate **only** with the saga, never with
each other. The saga owns all cross-student coordination state; StudentActors
only hold their own participation handle (`SagaId`, role, status).

### Saga aggregate contract

A CollabSaga is an event-sourced aggregate with:

- `SagaId` — ULID
- `SagaKind` — one of `peer-explain`, `whiteboard`, `team-challenge`,
  `peer-confused-signal`
- `TenantContext` — school + institute (ADR-0001) captured at saga creation,
  immutable for the saga's lifetime
- `Participants` — list of `StudentId` + `Role` + `ParticipantState`
- `Status` — `pending` | `running` | `completed` | `compensating` |
  `compensated` | `failed` | `expired`
- `TimeoutPolicy` — see below
- `Events` stream — append-only, retained per saga kind retention policy

### Compensation actions

Every saga command that has observable side-effects (XP awarded, mastery
updated, peer-feedback recorded) MUST declare a `Compensate()` method that
reverses the side-effect on failure/timeout/veto. Compensation is **not
optional** — a saga that cannot be compensated is rejected at saga-kind
registration time.

Examples:

| Forward action | Compensation |
|---|---|
| `AwardPeerExplainXp(A, 15)` | `RevokePeerExplainXp(A, 15, reason="saga-timeout")` |
| `RecordPeerFeedback(A→B, score=4)` | `RetractPeerFeedback(A→B, score=4)` |
| `UpdateTeamScore(team, +3)` | `UpdateTeamScore(team, -3)` |
| `FanInConfusedCount(sessionId, +1)` | `FanInConfusedCount(sessionId, -1)` |

Compensation is idempotent by design: replaying the same `CompensatedEvent`
twice on a StudentActor is a no-op.

### Timeout policy

Every saga kind declares a timeout (wall-clock, not actor-mailbox):

| Saga kind | Timeout | Reason |
|---|---|---|
| `peer-explain` | 15 min | Asynchronous, single-turn exchange; student B has up to 15 min to view and rate |
| `whiteboard` | session duration + 5 min | Bound to active session; closes when session ends |
| `team-challenge` | 30 min | Short synchronous co-op |
| `peer-confused-signal` | session duration | Bound to session |

Timeout transitions the saga to `expired` and fires `Compensate()` on every
`AppliedForward` event. Expired sagas retain their events for RTBF purposes
but are ineligible for resumption.

### Retention policy

Saga event streams follow session-scope rules
([ADR-0003](0003-misconception-session-scope.md) treatment): **30 days
max** for any saga that touched misconception-adjacent data; **90 days** for
saga kinds that are purely positive-framed (team-challenge XP awards).
RTBF ([ADR-0038](0038-event-sourced-right-to-be-forgotten.md)) redacts the
participant's StudentId in the event body while keeping the saga's shape,
so other participants' views replay cleanly.

### DLQ

Sagas that cannot progress for > 2× timeout without a compensation path (e.g.
compensation action fails) are routed to the `cena.dlq.saga.<kind>` NATS
topic with the full event stream payload. Ops pages on DLQ depth > 10 per
hour. The DLQ entry is reviewed, resolved by-hand (refund XP manually,
notify the participants), and the saga is then marked `dlq-resolved` with
the resolver's operator-id.

### No StudentActor state about other students

Normative rule: **a StudentActor's state MUST NOT reference another student's
identity**. Every cross-student link is held on the saga. This is enforced by
an arch-test (`NoCrossStudentReferencesTest`) that fails build if any event
body under `StudentActor/Events/` deserialises to a type containing another
`StudentId`.

## Rationale

### Why a process-manager, not a choreography?

Choreography (events directly between StudentActor A and StudentActor B)
seems simpler but fails three tests:

- **Failure isolation**: if B's mailbox is wedged, A's event sits in B's
  queue forever. A saga with explicit timeout breaks this.
- **Compensation**: choreographed events have no natural owner for the
  "reverse this if it fails" operation; compensation becomes ad-hoc and
  unreliable.
- **Auditability**: a regulator asking "what happened in peer-explain
  session X?" needs a single event stream to read, not N streams
  reconstructed across student boundaries.

### Why saga aggregates, not a single global saga table?

A global table is a hotspot and a multi-tenancy leak waiting to happen. Per-
saga aggregates give us:

- Natural tenant boundary (saga is tagged to school + institute at creation)
- Independent retention policies per saga kind
- Clean RTBF (delete one saga stream without touching others)
- Actor-mailbox concurrency control per saga (no global lock)

### Why declare compensation at registration time?

We have been burned by "we'll add compensation in phase 2" (no-stubs-in-prod
banned 2026-04-11). Enforcing it at saga-kind registration means no saga
kind ships without a reversal story.

### 03:00 Bagrut exam morning failure runbook

Symptom: peer-confused-signal counts blow up on a high-traffic session
because the saga is not compensating phantom emissions.

Steps:

1. `GET /api/admin/sagas/stats?kind=peer-confused-signal` — current open
   sagas + DLQ depth.
2. If DLQ depth > threshold: `GET /api/admin/sagas/dlq/peer-confused-signal`
   — list wedged sagas.
3. Per saga: `POST /api/admin/sagas/{id}/compensate-force` — runs
   compensation synchronously, skipping the wedged step. Idempotent.
4. If symptom persists: `POST /api/admin/sagas/{kind}/disable` — disables
   new saga creation for this kind only. Sessions still run; just no
   peer-confused button. Decide-to-ship gate lifts once the wedge is
   understood.

## Consequences

### Positive

- Clean separation: StudentActor stays single-student; saga owns N-way
  coordination.
- Compensable by construction.
- Tenant-safe (saga captures tenant at birth, immutable).
- RTBF-safe (per-saga stream).
- DLQ-able (one NATS topic per saga kind).

### Negative

- One more aggregate type per collaboration feature.
- Cross-actor latency: saga round-trip adds one hop (~5 ms local, ~20 ms
  over network).
- Writers must remember to pair every forward action with a compensation.
  Arch-test enforces, but adds review load.

### Neutral

- Saga aggregates are cheap: they live in the same actor system as
  StudentActor, same cluster, same event store.

## Implementation seams

- **SagaCoordinator** (`src/actors/Cena.Actors/Collaboration/SagaCoordinator.cs`)
  — static registry of saga kinds + factory for `CollabSagaActor`. Holds
  timeout policies and compensation registrations.
- **CollabSagaActor** (`src/actors/Cena.Actors/Collaboration/CollabSagaActor.cs`)
  — one actor per saga instance, receives commands, emits events.
- **CollabSagaAggregate** — event-sourced state, pure logic.
- **SagaDefinition<TCommand, TEvent>** — typed registration helper
  (forward action + compensation + timeout).
- **Arch-test**: `NoCrossStudentReferencesTest` in
  `src/actors/Cena.Actors.Tests/Architecture/`.

## Open items (deferred with task IDs)

- Fan-in projection for DLQ depth alerting — task to follow this ADR.
- Reviewer workflow for DLQ resolution (ties into ops queue) — covered by
  prr-034 (Cultural context DLQ sets the pattern, saga DLQ reuses it).
- Team-challenge kind ships later; peer-explain + peer-confused-signal ship
  first. ADR applies to all.

## References

- Gregor Hohpe and Bobby Woolf. *Enterprise Integration Patterns: Designing,
  Building, and Deploying Messaging Solutions*. Addison-Wesley, 2003 —
  Process Manager, Compensating Transaction chapters.
- Vaughn Vernon. *Implementing Domain-Driven Design*. Addison-Wesley, 2013 —
  ch. 10, Sagas and long-running processes.
- García-Molina & Salem, *Sagas*, SIGMOD 1987 — the original formalisation.
