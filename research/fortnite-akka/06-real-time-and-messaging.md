# Real-Time Communication & Messaging

---

## Fortnite's XMPP Layer

### Architecture
- **101-node full-mesh cluster** (every node connects to every other node)
- 10 connections between each node pair → ~1,000 sockets per node just for intra-cluster
- **3M+ persistent client connections**
- **~600K messages/sec** (including forwarding, broadcast, auxiliary)
- Supports **TCP and WebSocket** protocols
- Depends on Friends Service for social graph data

### What XMPP Handles
- Player presence (online/offline/in-game)
- Friend-to-friend chat
- Party coordination and invites
- Real-time notifications

### Known Problems
- Full-mesh topology is O(N^2) -- does not scale well beyond ~100 nodes
- Single load balancer failure in Feb 2018 made ALL users appear offline
- Epic acknowledged this as an active problem they were working on

---

## Cena's Messaging: NATS JetStream

### Architecture
- NATS (distributed by design, no single LB)
- JetStream for durable messaging (event persistence, replay)
- NatsOutboxPublisher for reliable event publishing from actors
- Pub/sub for real-time notifications

### What NATS Handles
- Outreach scheduling (OutreachSchedulerActor publishes to NATS)
- Event propagation (student events published for downstream consumers)
- Future: teacher notifications, real-time dashboards

---

## Comparison

| Aspect                     | Fortnite (XMPP)                    | Cena (NATS JetStream)              |
|----------------------------|-------------------------------------|-------------------------------------|
| Topology                   | Full mesh (O(N^2))                 | Clustered mesh (O(N log N))        |
| Client connections         | 3M+ persistent                     | N/A (server-side only for now)     |
| Message durability         | No (fire-and-forget)               | Yes (JetStream persistence)        |
| Protocol                   | XMPP (XML-based)                   | NATS (binary, lightweight)         |
| Presence                   | Built-in                           | Not built-in (custom)              |
| Single point of failure    | Yes (2018 LB outage)              | No (NATS HA by design)            |
| Message rate               | ~600K/sec                          | NATS handles 10M+ msg/sec         |
| Operational complexity     | High (101 nodes, custom XMPP)     | Low (NATS is ops-friendly)         |

### Cena's NATS advantage is significant

NATS is architecturally superior to Fortnite's custom XMPP for this use case:
- No full-mesh scaling problem
- Built-in durability (JetStream) vs XMPP fire-and-forget
- Binary protocol (lower latency, less bandwidth than XML)
- Native support for queue groups (load balancing consumers)
- Subject-based routing maps perfectly to `student.{id}.events`

---

## Insights for Cena

### 1. Subject Hierarchy Design

NATS subjects should mirror the actor hierarchy:

```
cena.student.{studentId}.events          → all events for a student
cena.student.{studentId}.session.started → session lifecycle
cena.student.{studentId}.session.ended
cena.student.{studentId}.outreach        → outreach scheduling
cena.student.{studentId}.stagnation      → stagnation alerts
cena.teacher.{teacherId}.notifications   → teacher dashboard feed
cena.system.health                       → circuit breaker states
```

### 2. NatsOutboxPublisher Reliability

The outbox pattern (persist event to Marten, then publish to NATS) is critical. If NATS is temporarily down, events must not be lost. Ensure:
- Events are marked as "published" only after NATS ack
- A background sweep re-publishes events older than N seconds without ack
- This is exactly the gap Fortnite had with XMPP (fire-and-forget, no delivery guarantee)

### 3. WebSocket Gateway (When Needed)

When Cena needs real-time client notifications (teacher dashboard, student progress), NATS → WebSocket bridge is simpler than Fortnite's XMPP:

```
NATS Subject → WebSocket Gateway → Browser Client
```

No need for a 101-node custom messaging cluster.

## Source

- [Fortnite Postmortem - 3.4M CCU](https://www.fortnite.com/news/postmortem-of-service-outage-at-3-4m-ccu?lang=en-US)
- [22 Companies Using XMPP](https://www.rst.software/blog/22-companies-using-xmpp-and-ejabberd-to-build-instant-messaging-services)
