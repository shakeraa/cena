# Actor Model Patterns: Fortnite vs Cena

---

## Fortnite's Actor Patterns

### Per-Player Actor (Cluster Sharding)

The MCP profile system gives each player distinct profiles (`athena` for Battle Royale, `campaign` for Save the World). This maps to Akka Cluster Sharding -- each player entity is a sharded actor activated on demand.

**Fortnite pattern:**
```
Player "epicplayer42" → Sharded Actor on Node 3
  ├── athena profile (BR inventory, loadouts, stats)
  ├── campaign profile (STW inventory, heroes, schematics)
  └── common_core profile (V-Bucks, shared items)
```

### Per-Match Actor

Each match session is a coordinating actor managing lifecycle from lobby → match → completion. The Scalable Solutions Akka gaming case study (closely mirrors Fortnite's pattern) describes:

```
GameDaemon (top-level manager)
  └── GameFramework (one per match)
        ├── Lobby (pre-game participants)
        ├── Feed (event broadcast -- enables state recovery by replaying)
        └── TurnController (action validation proxy)
```

Key design: **everything internal to a game lives on a single node**. Individual GameFramework actors are distributed across cluster nodes.

### Stateless Children, Stateful Parent

Children are lightweight and stateless; state lives in the parent actor. On child failure, the parent can simply restart the child without state loss.

---

## Cena's Actor Patterns (Current)

### Per-Student Virtual Actor

```
StudentActor (virtual, event-sourced, ClusterIdentity)
  ├── LearningSessionActor (classic child, session-scoped)
  ├── StagnationDetectorActor (classic child, monitoring)
  └── OutreachSchedulerActor (classic child, proactive)
```

**Already matches Fortnite's pattern:**
- StudentActor = per-player sharded actor (virtual actor / grain in Proto.Actor)
- Auto-activated on first message, passivated after 30min idle
- State recovery from Marten snapshot + event replay
- Children are stateless (state in parent) -- identical to Fortnite's approach

### Key Differences

| Aspect                | Fortnite                          | Cena                              |
|-----------------------|-----------------------------------|-----------------------------------|
| Activation            | Akka Cluster Sharding             | Proto.Actor virtual actors/grains |
| State recovery        | Unknown (likely Akka Persistence) | Marten snapshots + event replay   |
| Profile multiplexing  | Multiple profiles per player      | Single StudentState aggregate     |
| Child actor scope     | Per-match (game session)          | Per-session (learning session)    |
| Passivation           | Unknown timeout                   | 30 minutes idle                   |

---

## Insights for Cena

### 1. Profile Multiplexing (steal this)

Fortnite's multi-profile-per-player is powerful. Cena could benefit from a similar concept:

```
StudentActor
  ├── "math" profile (mastery state, BKT params for math concepts)
  ├── "science" profile (mastery state, BKT params for science)
  └── "common" profile (fatigue model, engagement metrics, preferences)
```

This allows subject-specific state without bloating a single aggregate. Each "profile" could be a separate event stream in Marten.

### 2. Feed Actor for Event Replay (steal this)

Fortnite's Feed actor broadcasts events and enables state recovery by replaying the feed. Cena already does event sourcing in Marten, but a dedicated Feed actor per student session could:
- Enable real-time dashboards for teachers watching a student's session
- Allow session replay for pedagogical review
- Decouple event consumers from the StudentActor

### 3. Action Validation Proxy (consider)

Fortnite's TurnController validates actions before they're applied. Cena's LearningSessionActor handles answer evaluation inline. A separate validation actor could enforce:
- Anti-cheating (answer timing, pattern detection)
- Rate limiting (prevent answer spam)
- Input sanitization (for free-text answers sent to LLMs)

### 4. Single-Node Locality (already doing this)

Fortnite's principle that "everything internal to a game lives on a single node" maps to Cena's approach of keeping children co-located with the parent StudentActor via Proto.Actor's child spawning. This eliminates network hops for intra-session communication.

## Source

- [Akka Actors Case Study: Multiplayer Games Backend (Scalable Solutions)](https://www.scalable-solutions.co.uk/blog/akka-multiplayer-games-architecture.html)
- [EpicResearch MCP Documentation (Community)](https://github.com/MixV2/EpicResearch/blob/master/docs/mcp/mcp_list.md)
