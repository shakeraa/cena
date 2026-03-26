# Cena Platform — Architecture Design

> **Status:** Approved
> **Last updated:** 2026-03-26
> **Audience:** Engineering team, technical advisors, investors

## 1. Executive Summary

Cena is an adaptive learning platform that acts as a personal AI mentor for high school STEM students, initially targeting the Israeli Bagrut examination system. The platform treats learning as a real-time game: each student is a "player" with a knowledge inventory, XP resources, streaks, and the system proactively reaches out through WhatsApp, Telegram, voice, and push notifications to keep them engaged.

The architecture is built on **Domain-Driven Design** with **event sourcing** on the student aggregate, an **actor-based distributed runtime** (Proto.Actor on .NET 9) serving as the "game server brain," and **NATS JetStream** as the event backbone connecting bounded contexts. LLM capabilities are delivered through a tiered multi-model routing strategy (Kimi K2.5 / Claude Sonnet 4.6 / Claude Opus 4.6) behind an Anti-Corruption Layer.

At 10K users, the estimated infrastructure cost is **~1,230-1,970 NIS/month (~$344-549)** excluding LLM API costs, which run at approximately 480 NIS per 1,000 active users/month.

---

## 2. Guiding Principles

| Principle | Implication |
|-----------|-------------|
| **Game engine-grade real-time** | Actor cluster as distributed game server; sub-millisecond hot-path reads; SignalR WebSocket push to clients |
| **Students are autonomous agents** | Each student is a virtual actor that proactively schedules outreach — no external cron jobs |
| **Event sourcing as the source of truth** | Every state change is an immutable event; enables replay, audit, crypto-shredding, and CQRS projections |
| **Polyglot by design** | .NET core cluster, Python LLM layer, Node.js video pipeline, React Native/React clients — connected via gRPC and NATS |
| **Hebrew RTL from day one** | All UI components bidirectional by default; i18n architecture supports future Arabic expansion |
| **Privacy by design** | GDPR-compliant crypto-shredding; no PII sent to Kimi (China-based); anonymized LLM identifiers throughout |

---

## 3. Core Pattern: Domain-Driven Design

The system is decomposed into **nine bounded contexts** with clear seams. Cross-context communication is asynchronous via NATS JetStream domain events. The core domain (Learner + Pedagogy) is event-sourced; supporting contexts consume projections or subscribe to event streams.

### 3.1 Bounded Context Map

```
    ┌─────────────────┐
    │ Content Authoring│  (supporting, produces curriculum artifacts)
    │    Context       │
    └────────┬────────┘
             │ publishes reviewed graph
    ┌────────▼─────────┐
    │   Curriculum      │  (upstream, rarely changes)
    │   Context         │
    └──────┬───────────┘
           │ publishes domain graph
           ┌───────────────┼───────────────┐
           ▼               ▼               ▼
    ┌─────────────┐ ┌─────────────┐ ┌─────────────┐
    │   Learner   │ │  Pedagogy   │ │  Delivery   │
    │   Context   │ │  Context    │ │  Context    │
    │  (core)     │ │  (core)     │ │ (supporting)│
    └──────┬──────┘ └──────┬──────┘ └─────────────┘
           │               │
           │  domain events via NATS JetStream
           ▼               ▼
    ┌─────────────┐ ┌─────────────┐ ┌─────────────┐
    │ Engagement  │ │  Outreach   │ │  Analytics  │
    │  Context    │ │  Context    │ │  Context    │
    │(supporting) │ │(supporting) │ │(downstream) │
    └─────────────┘ └─────────────┘ └──────┬──────┘
                                           │
                                    ┌──────▼──────┐
                                    │   School    │
                                    │  Context    │
                                    │(supporting) │
                                    └─────────────┘
```

### 3.2 Bounded Context Definitions

#### 3.2.0 Content Authoring Context (supporting)

- **Type:** Supporting context (9th bounded context, see `docs/content-authoring.md`)
- **Upstream dependency:** None (produces the Curriculum context's artifacts)
- **Downstream consumers:** Curriculum Context (consumes published graphs), Delivery Context (consumes questions/explanations)
- **Aggregate root:** `ContentGraph` (in-progress, unpublished version of a subject's knowledge graph)
- **Responsibilities:** Corpus ingestion, LLM-assisted knowledge graph extraction, question generation, expert review workflow, QA pipeline, content publication, content correction after student interaction
- **Full specification:** `docs/content-authoring.md`

#### 3.2.1 Curriculum Context (upstream)

- **Owns:** Domain knowledge graph, syllabus structure, concept metadata, difficulty ratings
- **Storage:** Neo4j AuraDB (source of truth) with in-memory cache loaded at silo startup
- **Change frequency:** Rare (annual syllabus updates, new subjects)
- **MCM Graph** (Mode x Capability x Methodology): Inspired by Squirrel AI's proprietary MCM model (Mode of Thinking, Capacity, Methodology — see "The Squirrel AI Adaptive Learning System", Springer, 2025), but adapted for Cena's error-type-driven switching rather than Squirrel AI's nano-knowledge-point decomposition. Stored as Neo4j edges connecting error types and concept categories to recommended teaching methodologies. Structure:

  ```
  MCM schema: (ErrorType, ConceptCategory) → [(Methodology, confidence)]

  Example entries:
    (conceptual, algebra)       → [(socratic, 0.85), (feynman, 0.70), (analogy, 0.55)]
    (procedural, trigonometry)  → [(drill, 0.90), (worked-example, 0.80)]
    (motivational, calculus)    → [(project-based, 0.75), (analogy, 0.60)]
  ```

  **Lookup algorithm** (`MethodologySwitchService`):
  1. Query: `MCM_LOOKUP(dominant_error_type, concept_category)` → returns candidate list sorted by confidence descending
  2. Filter: remove any methodology in `method_attempt_history` for this concept cluster (see `system-overview.md` cycling prevention)
  3. Select: first remaining candidate with confidence > 0.5. If none above 0.5, use first remaining regardless (best available)
  4. Fallback: if no MCM entry exists for this (error_type, concept_category) pair, fall back to the error-type-only defaults in `system-overview.md`

  **Population**: Initially hand-crafted by education advisor (one entry per error_type × concept_category combination for Math, ~50 entries). Confidence scores updated monthly by Intelligence Layer Flywheel 1 (see `docs/intelligence-layer.md`) based on post-switch outcome data.

- **Approximate size:** ~2,000 concept nodes per subject + ~50–100 MCM edges per subject

#### 3.2.2 Learner Context (core domain)

- **Aggregate Root:** `StudentProfile` (implemented as `StudentActor` — a Proto.Actor virtual actor, event-sourced)
- **Owns:**
  - Knowledge graph overlay (mastery level per concept)
  - Methodology effectiveness history per student
  - Spaced repetition schedule (half-life per concept)
  - Cognitive load profile
  - Experiment cohort assignments (A/B testing)
- **Domain Events:**
  - `ConceptAttempted` — student attempted a concept exercise
  - `ConceptMastered` — mastery threshold reached
  - `MasteryDecayed` — predicted recall dropped below threshold
  - `MethodologySwitched` — teaching approach changed for a concept
  - `StagnationDetected` — learning plateau identified
  - `AnnotationAdded` — student added a note or reflection
- **Persistence:** Proto.Persistence event sourcing to PostgreSQL/Marten, snapshots every 100 events
- **Knowledge tracing:**
  - Launch: Bayesian Knowledge Tracing (BKT) — fully implementable day one, parameters trained offline with pyBKT
  - Scale: migrate to MIRT (Multidimensional Item Response Theory) when interaction data volume justifies
  - Onboarding diagnostic: Knowledge Space Theory posterior update (ALEKS-inspired, 10-15 questions)

#### 3.2.3 Pedagogy Context (core domain)

- **Aggregate Root:** `LearningSession` (classic child actor, transactional, session-scoped)
- **Owns:** Active session state, current methodology, item selection, real-time adaptation
- **Session loop:** Present content -> student responds -> evaluate -> update state -> select next
- **Domain Services:**
  - `MethodologySwitchService` — reads across sessions + profile to decide methodology switches
- **Stagnation detection:** Timer-based sliding window in `StagnationDetectorActor` child actor, monitoring:
  - Accuracy plateau
  - Response time drift
  - Session abandonment patterns
  - Error repetition
  - Annotation sentiment
- **Domain Events:**
  - `SessionStarted`, `SessionEnded`
  - `ExerciseAttempted`
  - `MethodologySwitched`

#### 3.2.4 Delivery Context (supporting)

- **LLM Anti-Corruption Layer:** Translates domain commands into LLM API calls
  - `GenerateSocraticQuestion` -> Claude Sonnet prompt
  - `EvaluateAnswer` -> Kimi K2.5 structured evaluation
  - `ClassifyError` -> Kimi K2.5 classification
- **Model Router:** Selects Kimi / Sonnet / Opus based on task type (see Section 7)
- **Responsibilities:** Prompt construction, anonymized context injection, response validation, safety filtering, cost tracking
- **Hard per-student rate limiting:** The ACL enforces a daily token budget per student (default: 25,000 output tokens/day, ~50 interactions × ~500 tokens). When exhausted, the ACL returns cached/pre-generated content instead of making LLM calls. This is a hard cutoff — no exceptions, no override. The budget resets at midnight UTC. Budget exhaustion is logged as a `StudentBudgetExhausted` metric for monitoring (high exhaust rates indicate the cap is too low or a student is gaming the system)
- **Remotion video generation pipeline** (batch + personalized modes)

#### 3.2.5 Engagement Context (supporting)

- **Owns:** XP, streaks, badges, achievements, leaderboards, league membership
- **Subscribes to:** Events from Learner and Pedagogy contexts
- **Nature:** Lightweight — mostly counters and timers
- **Feeds:** Gamification UI layer in the mobile and web apps

#### 3.2.6 Outreach Context (supporting)

- **Subscribes to NATS events:** `StreakExpiring`, `ReviewDue`, `StagnationDetected`, `SessionAbandoned`, `CognitiveLoadCooldownComplete`
- **Routes to channels:**
  - WhatsApp Business API (Twilio / Meta Cloud API)
  - Telegram Bot API
  - Voice calls (Twilio)
  - Push notifications (FCM / APNs)
- **Manages:** Per-student channel preferences and optimal contact times
- **Supports:** Inline interactions (WhatsApp quiz responses fed back to the student actor)

#### 3.2.7 Analytics Context (downstream, read-only)

- **CQRS read models** projected from the event store
- **Dashboards:**
  - Teacher dashboards (class-level knowledge gaps)
  - Parent progress views (child's learning trajectory)
  - Retention analytics (cohort analysis, drop-off detection)
  - Methodology effectiveness analysis (which methods work for which student profiles)
- **Served via:** GraphQL for flexible frontend querying

#### 3.2.8 School Context (supporting)

- **Multi-tenancy** for B2B2C school partnerships
- **Wraps:** `StudentProfile` actors with school-scoped views
- **Provides:** Teacher dashboards with class-level analytics, student grouping by class
- **Supports:** Custom curriculum overlays per school

---

## 4. Actor Framework: Proto.Actor (.NET 9)

### 4.1 Why Proto.Actor

| Criterion | Proto.Actor | Akka.NET | Orleans |
|-----------|-------------|----------|---------|
| **License** | Apache 2.0 | Apache 2.0 (but Akka JVM is BSL — ecosystem confusion risk) | MIT but ecosystem stagnant |
| **Actor models** | Classic AND virtual | Classic only | Virtual only |
| **Cross-language** | gRPC Actor Standard Protocol (.NET, Go, Kotlin) | .NET only | .NET only |
| **Creator** | Roger Johansson (same as Akka.NET) | Roger Johansson | Microsoft Research |
| **Cluster discovery** | DynamoDB, Consul, etcd, Kubernetes | Varies | Azure-centric |

Proto.Actor provides **both** classic actors (supervision trees, lifecycle control) and virtual actors (grain-like automatic lifecycle management). This dual model maps naturally to the domain:

- **Virtual actors** for long-lived entities (students) — auto-activated on first message, passivated when idle
- **Classic actors** for transactional, session-scoped work — explicit lifecycle, supervised by parent

### 4.2 Actor Hierarchy

```
Proto.Actor Cluster (ECS/Fargate, 2-3 nodes)
│
├── StudentActor [virtual, event-sourced]     ← one per student
│   ├── LearningSessionActor [classic, child] ← transactional, session-scoped
│   ├── StagnationDetectorActor [classic, child, timer-based]
│   │   └── sliding window: accuracy, response time, abandonment, errors, sentiment
│   └── OutreachSchedulerActor [classic, child, timer-based]
│       └── schedules: streak reminders, spaced repetition reviews, re-engagement
│
├── MethodologySwitchService [domain service]
│   └── reads across sessions + profile, decides methodology switches
│
└── DomainGraphCache [in-memory, loaded from Neo4j at startup]
    └── ~2,000 nodes/subject, MCM graph, microsecond lookups
```

### 4.3 Event Sourcing on the Student Aggregate

The `StudentActor` is the event-sourced aggregate root for the Learner context. Every state mutation produces an immutable domain event persisted to PostgreSQL via Marten.

**Event flow:**

```
Command (e.g., AttemptConcept)
    │
    ▼
StudentActor validates + applies business rules
    │
    ▼
Domain Event emitted (e.g., ConceptAttempted)
    │
    ├──► Persisted to PostgreSQL/Marten (event store)
    ├──► Actor state updated in-memory
    ├──► Published to NATS JetStream (cross-context)
    └──► SignalR push to connected client
```

**Snapshots** are taken every 100 events to bound replay time on actor reactivation.

### 4.4 Spaced Repetition as Actor Timers

The spaced repetition engine runs **inside the actor**, not as an external cron job.

- **Model:** Half-Life Regression (Settles & Meeder, 2016, "A Trainable Spaced Repetition Model for Language Learning", ACL; open-sourced by Duolingo at github.com/duolingo/halflife-regression)
- **Formula:** `p(t) = 2^(-delta/h)` where `delta` is time elapsed and `h` is the concept's half-life
- **Trigger:** When predicted recall drops below 0.85, the `OutreachSchedulerActor` schedules a Proto.Actor reminder
- **Flow:** Reminder fires -> event to NATS -> Outreach service -> WhatsApp "Quick review: what's the derivative of sin(x)?"

This eliminates the need for any external scheduler — the actor IS the scheduler.

### 4.5 A/B Testing on Actor State

Experiment cohort assignment is stored directly on the `StudentProfile` actor. Actor behavior varies by cohort:

- Gamification intensity (XP multipliers, badge unlock thresholds)
- Stagnation detection thresholds (sensitivity tuning)
- Methodology switching triggers (eagerness to switch approaches)
- Notification timing (optimal outreach windows)

Feature flags are evaluated in-actor, not in middleware, ensuring zero-latency branching.

---

## 5. Event Backbone: NATS JetStream

### 5.1 Why NATS JetStream

| Criterion | NATS JetStream | Kafka | RabbitMQ |
|-----------|---------------|-------|----------|
| **Operational weight** | Single binary, minimal config | ZooKeeper/KRaft, partitions, ISR | Erlang runtime, queues |
| **Throughput** | 200K+ msg/sec | Higher ceiling | Lower |
| **Cost (managed)** | $49/month (Synadia Cloud) | $200-500+/month | Self-managed or expensive |
| **Durable delivery** | Yes (JetStream) | Yes | Yes |
| **Simplicity** | Excellent | Complex | Moderate |

### 5.2 Event Flow Architecture

Each bounded context publishes domain events to NATS JetStream subjects. Downstream contexts subscribe to the streams they care about.

```
Proto.Actor Cluster
│
├── cena.learner.events.>
│   ├── ConceptAttempted
│   ├── ConceptMastered
│   ├── MasteryDecayed
│   ├── StagnationDetected
│   └── ...
│
├── cena.pedagogy.events.>
│   ├── SessionStarted
│   ├── SessionEnded
│   ├── ExerciseAttempted
│   ├── MethodologySwitched
│   └── ...
│
Subscribers:
├── Engagement Context  ← cena.learner.events.>, cena.pedagogy.events.>
├── Outreach Context    ← cena.learner.events.StagnationDetected,
│                         cena.learner.events.MasteryDecayed, ...
├── Analytics Consumer  ← cena.*.events.>  (all events)
└── School Context      ← cena.learner.events.> (filtered by school tenant)
```

**Delivery guarantee:** NATS JetStream provides at-least-once delivery with consumer acknowledgment. This is critical for outreach messages — a WhatsApp streak reminder MUST be delivered.

---

## 6. Knowledge Graph: Neo4j + In-Memory Cache

### 6.1 Dual-Layer Architecture

```
┌───────────────────────────────────────────┐
│          HOT PATH (in-memory)             │
│  Loaded at silo startup from Neo4j        │
│  ~2,000 nodes per subject                 │
│  Microsecond lookups                      │
│  Used by: StudentActor, Pedagogy logic    │
└───────────────────┬───────────────────────┘
                    │ populated from
┌───────────────────▼───────────────────────┐
│        COLD PATH (Neo4j AuraDB)           │
│  Source of truth for domain graph          │
│  Used by: admin/authoring tools           │
│  Complex cross-student analytics          │
│  $65/GB/month (AuraDB Professional, 1-2GB) │
└───────────────────────────────────────────┘
```

### 6.2 Student Knowledge Overlay

The domain graph (concepts, prerequisites, difficulty) is shared and immutable at runtime. Each student's **overlay** — mastery levels, attempt history, half-life for spaced repetition — lives in the student actor's event-sourced state.

This means the hot-path query "what should this student learn next?" executes entirely in-memory, combining the cached domain graph with the actor's overlay state. No database round-trip required.

---

## 7. LLM Strategy: Tiered Multi-Model Routing

### 7.1 Model Tier Assignment

| Tier | Model | Use Cases | Cost (input) | Privacy |
|------|-------|-----------|-------------|---------|
| **Fast/Cheap** | Kimi K2.5 | Classification, extraction, filtering, structured evaluation | $0.45/MTok | No PII (anonymized data only) |
| **Balanced** | Claude Sonnet 4.6 | Real-time tutoring, Socratic dialogue, explanation generation | $3.00/MTok | Full context allowed |
| **Reasoning** | Claude Opus 4.6 | Methodology switching decisions, complex pedagogical reasoning | $5.00/MTok | Full context allowed |

### 7.2 Routing Logic

The LLM Router lives inside the Python FastAPI Anti-Corruption Layer. Task type determines model selection:

```
Domain Command              → Model Selection
─────────────────────────────────────────────
ClassifyError               → Kimi K2.5
ExtractConcepts             → Kimi K2.5
FilterContent               → Kimi K2.5
EvaluateAnswer (structured) → Kimi K2.5
GenerateSocraticQuestion    → Claude Sonnet 4.6
ExplainConcept              → Claude Sonnet 4.6
GenerateHint                → Claude Sonnet 4.6
DecideMethodologySwitch     → Claude Opus 4.6
AnalyzeLearningTrajectory   → Claude Opus 4.6
GenerateDiagnosticPlan      → Claude Opus 4.6
```

**Fallback chains (per tier):** Kimi tasks: Kimi K2.5 -> Kimi K2 -> Claude Haiku -> Claude Sonnet. Sonnet tasks: Sonnet 4.6 -> Sonnet 4.5 -> Haiku. Opus tasks: Opus 4.6 -> Sonnet 4.6 (with extended thinking) -> Sonnet 4.6. See `docs/llm-routing-strategy.md` Section 5 for full fallback design

**Privacy rule:** Kimi (China-based) never receives PII. Only anonymized, structured data crosses that boundary.

### 7.3 Cost Model

Estimated cost at steady state: **$13.32/student/month** (approximately 40% cheaper than a Sonnet-only approach). Full analysis in `docs/llm-routing-strategy.md`.

---

## 8. Polyglot Service Architecture

### 8.1 Technology Map

| Service | Language/Framework | Role |
|---------|-------------------|------|
| **Actor Cluster** | .NET 9 + Proto.Actor | Real-time game server brain, student actors, event sourcing |
| **LLM ACL** | Python FastAPI | Anti-Corruption Layer for Claude/Kimi API calls, model routing |
| **Video Pipeline** | Node.js (Remotion) | React-to-video rendering, personalized explainer videos |
| **Mobile App** | React Native | Primary student-facing platform (iOS + Android) |
| **Web App** | React (PWA) | Secondary platform, shared component library with mobile |
| **Outreach Service** | .NET 9 or Go | NATS subscriber, WhatsApp/Telegram/voice/push dispatch |

### 8.2 Inter-Service Communication

```
Client ──SignalR WebSocket──► Proto.Actor Cluster
                                    │
                              gRPC ──┤──► Python FastAPI (LLM ACL)
                                    │
                         NATS JetStream
                              │
              ┌───────────────┼───────────────┐
              ▼               ▼               ▼
        Outreach Svc    Remotion Worker   Analytics Consumer
```

Proto.Actor's gRPC transport (Actor Standard Protocol) enables future Go or Kotlin services to join the actor cluster natively.

---

## 9. Proactive Outbound Engagement

### 9.1 Philosophy

Student actors are **autonomous agents**. They do not wait passively for the student to open the app. When the actor detects that action is needed — a streak is about to expire, a concept's recall has decayed, the student has been stagnant — it initiates outreach.

### 9.2 Trigger Events

| Event | Source | Action |
|-------|--------|--------|
| `StreakExpiring` | OutreachSchedulerActor timer | WhatsApp: "Your 7-day streak expires in 2 hours!" |
| `ReviewDue` | Half-Life Regression threshold crossed | WhatsApp/Telegram: inline quiz question |
| `StagnationDetected` | StagnationDetectorActor sliding window | Push notification + personalized video |
| `SessionAbandoned` | LearningSessionActor timeout | WhatsApp: "Pick up where you left off?" |
| `CognitiveLoadCooldownComplete` | OutreachSchedulerActor timer | Push: "Ready for a fresh challenge?" |

### 9.3 Inline Interactions

Students can respond to quizzes **directly in WhatsApp and Telegram** without opening the app. Responses are routed back through the Outreach service to the student actor, updating the event-sourced state.

### 9.4 Channel Selection

The Outreach Context manages per-student channel preferences and optimal contact times. Default channel priority order: (1) WhatsApp, (2) Push notification, (3) Telegram, (4) Voice — configurable per student in their app settings. WhatsApp is the default primary channel for the Israeli market (90%+ penetration among ages 16–18, DataReportal Israel 2024). Default optimal contact window: 15:00–20:00 Israel time on weekdays, 10:00–20:00 on Fridays and Saturdays, personalized after 7 days of engagement data.

---

## 10. Video Generation Pipeline

### 10.1 Architecture

- **Engine:** Remotion (React components rendered to MP4)
- **Content generation:** Claude generates narration scripts and visual descriptions
- **Two modes:**
  - **Batch mode:** Pre-generate one video per concept (shared across all students)
  - **Personalized mode:** Per-student variations based on knowledge state and methodology preference
- **Storage:** S3 with CloudFront CDN
- **Distribution:** Sent via WhatsApp/Telegram as engagement hooks, embedded in app

### 10.2 Deployment

Remotion worker runs as a Fargate task that scales to zero when idle. Batch rendering jobs are triggered by curriculum updates; personalized rendering is triggered by pedagogy events.

---

## 11. Client Architecture

### 11.1 React Native Mobile App (Primary)

- **State management:** Local state machine mirroring the server actor state
- **Offline support:** Learning sessions continue offline; events queue locally
- **Sync:** On reconnect, queued events replay to server actor; server pushes any missed events back
- **Real-time:** SignalR WebSocket for live push when online (note: `@microsoft/signalr` does not officially support React Native — use a community wrapper like `react-signalr` or `react-native-signalr`, or polyfill the transport layer)
- **Knowledge graph visualization:** Interactive, zoomable graph of student's mastery overlay
- **Hebrew RTL:** Baked into the component library from the first commit

### 11.2 React Web App (PWA)

- Shared component library with React Native (via React Native Web or shared design tokens)
- Progressive Web App for browser-based access
- Secondary platform, feature-parallel with mobile

---

## 12. Persistence Layer

| Store | Technology | Purpose | Tier |
|-------|-----------|---------|------|
| **Event Store** | PostgreSQL + Marten | Student actor event sourcing, CQRS read model projections, user accounts | Primary |
| **Hot State** | Redis (ElastiCache) | Active session state, sub-millisecond reads, evicts to PostgreSQL when idle | Cache |
| **Knowledge Graph** | Neo4j AuraDB | Domain graph source of truth, admin/authoring, cross-student analytics | Primary |
| **Object Storage** | S3 | Generated videos, analytics archive, static curriculum artifacts | Archive |
| **Cluster State** | DynamoDB | Proto.Actor cluster discovery and membership | Infrastructure |

### 12.1 Event Store Design (Marten v7.x)

Marten v7.x on PostgreSQL 16 (pin to latest 7.x stable; Marten 8.x has breaking changes in projection API — evaluate before upgrading). Provides:
- **Event streams** per student aggregate (append-only)
- **Inline projections** for CQRS read models (teacher dashboards, parent views)
- **Snapshot storage** (every 100 events per student)
- **Async daemon** for background projection rebuilds

---

## 13. Deployment Architecture (AWS)

### 13.1 Infrastructure Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                          AWS                                │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  ECS / Fargate                                       │   │
│  │                                                      │   │
│  │  ┌──────────────────────┐  ┌─────────────────────┐   │   │
│  │  │ Proto.Actor Silo     │  │ Proto.Actor Silo    │   │   │
│  │  │ (Node 1)             │  │ (Node 2)            │   │   │
│  │  │ Student virtual actors│  │ Student virtual actors│  │   │
│  │  └──────────┬───────────┘  └──────────┬──────────┘   │   │
│  │             │  DynamoDB cluster discovery  │          │   │
│  │             └─────────────┬───────────────┘          │   │
│  │                           │                          │   │
│  │  ┌─────────────────┐  ┌──┴──────────┐  ┌─────────┐  │   │
│  │  │ Python FastAPI   │  │ Outreach    │  │Remotion │  │   │
│  │  │ (App Runner /    │  │ Service     │  │Worker   │  │   │
│  │  │  Fargate task)   │  │ (Fargate)   │  │(Fargate)│  │   │
│  │  └─────────────────┘  └─────────────┘  └─────────┘  │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌──────────┐  ┌───────────┐  ┌──────────┐  ┌──────────┐   │
│  │ RDS      │  │ElastiCache│  │ DynamoDB │  │ S3 +     │   │
│  │PostgreSQL│  │ Redis     │  │ (cluster │  │CloudFront│   │
│  │+ Marten  │  │           │  │  disc.)  │  │          │   │
│  └──────────┘  └───────────┘  └──────────┘  └──────────┘   │
│                                                             │
│  MANAGED EXTERNAL SERVICES:                                 │
│  ┌──────────────┐  ┌──────────────────┐                     │
│  │ Neo4j AuraDB │  │ Synadia Cloud    │                     │
│  │ (managed)    │  │ NATS JetStream   │                     │
│  └──────────────┘  └──────────────────┘                     │
└─────────────────────────────────────────────────────────────┘
```

### 13.2 Scaling Strategy

- **Proto.Actor cluster:** 2-3 Fargate nodes, auto-scaling based on active student count. Virtual actors distribute automatically across nodes.
- **Python FastAPI:** App Runner or single Fargate task; scales horizontally on request volume.
- **Remotion worker:** Fargate task, scales to zero when idle (batch jobs are bursty).
- **Outreach service:** Single Fargate task; scales on NATS consumer lag.
- **Region:** AWS `eu-west-1` or `il-central-1` for data residency compliance.

---

## 14. Cross-Cutting Concerns

### 14.1 Observability (OpenTelemetry)

Every actor message is instrumented with OpenTelemetry traces. A single student interaction produces a trace spanning:

```
WebSocket request
  └── StudentActor message processing
       └── LearningSessionActor exercise evaluation
            └── LLM API call (Python FastAPI)
                 └── NATS event publish
                      └── Outreach service dispatch
                           └── WhatsApp delivery confirmation
```

- **Structured logging** per actor instance (actor PID, student ID, session ID)
- **Actor state inspection tools** for debugging individual student state
- **Metrics:** Actor message throughput, LLM latency percentiles, NATS consumer lag, outreach delivery rates

### 14.2 GDPR / Privacy by Design

- **Crypto-shredding:** Each student's events are encrypted with a per-student key. On erasure request, the key is deleted, rendering all events unreadable without touching the event store.
- **Kimi isolation:** The LLM ACL strips all PII before routing to Kimi K2.5. Only anonymized, structured data (concept IDs, difficulty ratings, error categories) crosses that boundary.
- **Anonymized LLM identifiers:** All LLM calls use opaque student identifiers, never real names or emails.
- **Data residency:** Primary storage in AWS `eu-west-1` or `il-central-1`.

### 14.3 Client-Side Session State Machine

The React Native app maintains a **local state machine** mirroring the server actor:

- Enables **offline learning sessions** — students can continue learning without connectivity
- Events queue locally during offline periods
- On reconnect: client replays queued events to the server actor; server pushes any missed events back
- **Conflict resolution:** Server state is authoritative; client merges via event replay

### 14.4 Hebrew RTL Support

- RTL layout baked into the React Native component library from the first commit
- Hebrew typography and right-to-left knowledge graph visualization
- i18n architecture supports future Arabic expansion
- All UI components are bidirectional by default

---

## 15. System Architecture Diagram

```
┌─────────────────────────────────────────────────────────┐
│                    CLIENT LAYER                         │
│  React Native (iOS/Android) + React PWA                │
│  Local session state machine, offline support           │
│  SignalR WebSocket + gRPC                              │
└─────────────┬───────────────────────────────────────────┘
              │
┌─────────────▼───────────────────────────────────────────┐
│              PROTO.ACTOR CLUSTER (.NET 9)               │
│              ECS/Fargate, 2-3 nodes                     │
│                                                         │
│  ┌─────────────────────────────────────────────┐        │
│  │  StudentActor (virtual, event-sourced)       │        │
│  │  ├── LearningSessionActor (classic, child)   │        │
│  │  ├── StagnationDetectorActor (timer-based)   │        │
│  │  ├── OutreachSchedulerActor (timer-based)    │        │
│  │  └── State: BKT vector, KG overlay, XP,     │        │
│  │       streak, methodology history, HLR       │        │
│  └─────────────────────────────────────────────┘        │
│                                                         │
│  ┌────────────────┐  ┌──────────────────────────┐       │
│  │ MethodologySwitch│  │ Domain Graph (in-memory) │       │
│  │ Service (Domain) │  │ Loaded from Neo4j at     │       │
│  └────────────────┘  │ startup + MCM graph       │       │
│                       └──────────────────────────┘       │
│                                                         │
│  Publishes domain events ──────► NATS JetStream         │
└─────────────────────────────────┬───────────────────────┘
                                  │
          ┌───────────┬───────────┼───────────┬───────────┐
          ▼           ▼           ▼           ▼           ▼
   ┌──────────┐ ┌──────────┐ ┌────────┐ ┌─────────┐ ┌──────┐
   │ Python   │ │ Remotion │ │Outreach│ │Analytics│ │School│
   │ FastAPI  │ │ Worker   │ │Service │ │Consumer │ │Tenant│
   │          │ │          │ │        │ │         │ │      │
   │ LLM ACL  │ │ Video gen│ │WhatsApp│ │ S3      │ │Dashb.│
   │ Kimi/    │ │ Claude + │ │Telegram│ │ GraphQL │ │Multi-│
   │ Sonnet/  │ │ React→MP4│ │Voice   │ │ CQRS    │ │tenant│
   │ Opus     │ │ S3 upload│ │Push    │ │ Project.│ │      │
   │ Router   │ │          │ │        │ │         │ │      │
   └──────────┘ └──────────┘ └────────┘ └─────────┘ └──────┘

PERSISTENCE LAYER:
   ┌──────────┐ ┌──────────┐ ┌────────┐ ┌────────┐ ┌────────┐
   │PostgreSQL│ │  Neo4j   │ │ Redis  │ │  S3    │ │DynamoDB│
   │+ Marten  │ │ AuraDB   │ │ElastiC.│ │        │ │(cluster│
   │Events +  │ │Domain    │ │Hot     │ │Videos  │ │ disc.) │
   │Project.  │ │Graph SoT │ │State   │ │Archive │ │        │
   └──────────┘ └──────────┘ └────────┘ └────────┘ └────────┘
```

---

## 16. Cost Estimate (10K Users)

| Component | Monthly Cost (NIS) | Monthly Cost (USD) |
|-----------|-------------------|-------------------|
| ECS Fargate (Proto.Actor cluster, 2-3 nodes) | ~430 | ~$120 |
| Neo4j AuraDB Professional (1-2GB) | ~235-470 | ~$65-130 |
| NATS JetStream (Synadia Cloud) | ~175-355 | ~$49-99 |
| Python FastAPI (App Runner) | ~70-145 | ~$20-40 |
| PostgreSQL (RDS) | ~145-215 | ~$40-60 |
| Redis (ElastiCache) | ~105-180 | ~$30-50 |
| DynamoDB (actor clustering) | ~35-105 | ~$10-30 |
| LLM API costs (Kimi/Sonnet/Opus) | ~480 per 1K active users | ~$133 per 1K active users |
| S3 + CDN | ~35-70 | ~$10-20 |
| **Total (infra, excl. LLM)** | **~1,230-1,970** | **~$344-549** |
| **Total (infra + LLM at 10K)** | **~6,030-6,770** | **~$1,674-1,879** |

**Neo4j AuraDB cost at scale:** AuraDB is $65/GB/month. At launch (1 subject, ~2K nodes), 1GB is sufficient (~$65/month). At 5 subjects (~10K nodes + edges), may need 3-5GB (~$195-325/month). **Self-hosted fallback:** If AuraDB cost exceeds $300/month, migrate to self-hosted Neo4j Community Edition on EC2 m7i.large (~$180/month all-in) — same Cypher API, no code changes, just connection string swap.

---

## 17. Implementation Roadmap

| Phase | Scope | Duration | Dependencies |
|-------|-------|----------|-------------|
| 1 | **Curriculum** — Build knowledge graph for Math | 1-2 weeks | Neo4j setup |
| 2 | **Learner** — StudentActor with Proto.Actor, event sourcing, BKT | 2 weeks | Phase 1 |
| 3 | **Pedagogy** — LearningSession, item selection, Socratic tutoring | 2 weeks | Phase 2 |
| 4 | **Delivery** — LLM ACL with model router | 1 week | Phase 3 |
| 5 | **Client** — React Native app, onboarding flow, KG visualization | 2-3 weeks | Phases 2-4 |
| 6 | **Engagement** — Streaks, XP, badges | 1 week | Phase 2 |
| 7 | **Outreach** — WhatsApp/Telegram integration, spaced repetition timers | 1-2 weeks | Phases 2, 6 |
| 8 | **Analytics** — CQRS projections, teacher dashboard | 1-2 weeks | Phase 2 |
| 9 | **Video** — Remotion pipeline | 1 week | Phase 4 |
| 10 | **School** — Multi-tenancy | 1 week | Post-launch |

**Total estimated MVP: ~10-14 weeks** with AI coding agents as force multipliers.

---

## 18. Key Technical Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|-----------|
| Proto.Actor maturity vs. Akka/Orleans | Medium | Community is smaller but active; Roger Johansson maintains it; Apache 2.0 license avoids any licensing confusion with Akka JVM's BSL (note: Akka.NET itself remains Apache 2.0) |
| Actor cluster state loss on Fargate task cycling | High | Event sourcing provides full replay; snapshots bound recovery time; Redis hot state is a cache, not the source of truth |
| LLM cost escalation at scale | High | Tiered routing pushes 60%+ of calls to Kimi ($0.45/MTok); cost tracking in ACL; per-student cost caps |
| Kimi availability / geopolitical risk | Medium | Fallback chain (Opus -> Sonnet -> Kimi) means Kimi is never the only option; structured tasks can degrade to local heuristics |
| Neo4j AuraDB latency from Israel | Low | Domain graph loaded into memory at startup; Neo4j only hit for cold-path admin queries |
| WhatsApp Business API rate limits | Medium | Per-student outreach scheduling respects rate limits; priority queue for time-sensitive messages |

---

## Appendix A: Related Documents

- `docs/llm-routing-strategy.md` — Detailed LLM model routing, pricing analysis, and task mapping
- `docs/product-research.md` — Market analysis and competitive landscape
- `docs/system-overview.md` — High-level system overview
- `docs/content-authoring.md` — Content Authoring bounded context specification
- `docs/assessment-specification.md` — Question taxonomy, evaluation pipeline, diagnostic algorithm
- `docs/operations.md` — Notification throttling, backup/DR, monitoring, CI/CD
- `docs/stakeholder-experiences.md` — Parent and teacher dashboard specifications
- `docs/intelligence-layer.md` — Data flywheels and semantic search
- `docs/engagement-signals-research.md` — Behavioral signal research for stagnation detection
- `docs/event-schemas.md` — Domain event schemas and versioning strategy
- `docs/offline-sync-protocol.md` — Client-server sync protocol for offline sessions
- `docs/failure-modes.md` — Proto.Actor cluster failure mode analysis
