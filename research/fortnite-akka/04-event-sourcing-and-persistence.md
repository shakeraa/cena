# Event Sourcing & Persistence: Fortnite vs Cena

---

## Fortnite's Approach

### No Confirmed Event Sourcing

Fortnite uses **MongoDB document storage** for player profiles -- mutable documents, not event streams. The MCP reads/writes full profile documents.

However, event-driven patterns are present:
- Analytics pipeline processes 125M events/minute from game clients
- Read/write separation at the DB level (318K reads vs 132K writes)
- The Scalable Solutions Akka gaming case study describes: *"we can recover the game state by replaying the feed into the game"* -- textbook event sourcing

HN commenters on the 3.4M outage postmortem explicitly suggested Epic adopt CQRS + event sourcing with Cassandra, **implying they were NOT using it** at the time.

### Persistence Model

```
Player Profile (MongoDB Document)
├── athena: { items: [...], loadouts: [...], stats: {...} }
├── campaign: { heroes: [...], schematics: [...] }
└── common_core: { vbucks: 1200, gifts: [...] }
```

- Mutable documents (read-modify-write)
- No audit trail of individual changes
- No ability to replay history
- Simple and fast at the cost of temporal queries

---

## Cena's Approach (Superior)

### Full Event Sourcing with Marten

```
Student Event Stream (PostgreSQL via Marten)
├── LearnerRegistered { studentId, name, ... }
├── SessionStarted { sessionId, subject, methodology }
├── ConceptAttempted { conceptId, correct, responseTimeMs, bktMastery }
├── FatigueThresholdBreached { fatigueScore, action }
├── MethodologySwitched { from, to, reason }
├── SessionEnded { duration, questionsAttempted, accuracy }
└── ... (full temporal audit trail)
```

**Snapshots:** StudentState snapshots after N events (reduces replay cost on reactivation)

**Event delegation:** LearningSessionActor does NOT persist directly -- it delegates events to the parent StudentActor for batch writes. This is clean aggregate root discipline.

---

## Cena is Ahead of Fortnite Here

| Capability                     | Fortnite (MongoDB)       | Cena (Marten ES)                |
|--------------------------------|--------------------------|----------------------------------|
| Temporal queries               | No                       | Yes (replay to any point)        |
| Audit trail                    | No                       | Yes (every event recorded)       |
| State reconstruction           | Read latest document     | Snapshot + event replay          |
| Analytics from same store      | Separate Kinesis pipeline| Events are the analytics source  |
| Schema evolution               | MongoDB flexible schema  | Event upcasting in Marten        |
| Write conflicts                | Last-write-wins          | Optimistic concurrency (stream version) |

---

## Insights for Cena

### 1. Event Store as Analytics Source (big win)

Fortnite built a separate analytics pipeline (Kinesis → Spark → DynamoDB → EMR) that consumes 125M events/min from game clients. That's a **completely separate system** from their profile persistence.

Cena's event store IS the analytics source. Every `ConceptAttempted`, `SessionEnded`, `MethodologySwitched` event is already in PostgreSQL. You can:
- Project read models for teacher dashboards directly from the event store
- Run temporal queries ("show me this student's mastery progression over 6 months")
- Build cohort analytics without a separate pipeline
- CDC (Change Data Capture) to a NATS stream for real-time projections

This is a massive architectural advantage over Fortnite's approach.

### 2. Snapshot Frequency Tuning

Fortnite's MongoDB can read a full profile in one document fetch. Cena's event replay on activation has a cost proportional to events-since-last-snapshot.

Recommended: snapshot every 50-100 events. Monitor `cena.student.event_persist_ms` to tune this. If activation replay exceeds 200ms, snapshot more frequently.

### 3. Event Projections for Read Models

Fortnite's read path (318K reads/sec) hit the same MongoDB shards as writes. Cena should project read models to Redis for hot-path queries:

```
Event Store (Marten/PostgreSQL) → Projection → Redis (read model)
                                             → NATS (real-time notifications)
```

This gives CQRS benefits without Fortnite's read/write contention on the same store.

### 4. StudentProfileSnapshot is the Right Pattern

Cena already has `StudentProfileSnapshot.cs` -- this is the equivalent of Fortnite's full profile document but built from events. Keep this as a materialized view that rebuilds from events, never as the source of truth.

## Source

- [EpicResearch MCP Documentation](https://github.com/MixV2/EpicResearch/blob/master/docs/mcp/mcp_list.md)
- [Fortnite Postmortem - 3.4M CCU](https://www.fortnite.com/news/postmortem-of-service-outage-at-3-4m-ccu?lang=en-US)
- [Akka Actors Case Study: Multiplayer Games (Scalable Solutions)](https://www.scalable-solutions.co.uk/blog/akka-multiplayer-games-architecture.html)
