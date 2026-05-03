# REV-007: Fix JetStream Subject Mismatch & Dual Publishing

**Priority:** P1 -- HIGH (9 JetStream streams capture zero events due to subject pattern mismatch)
**Blocked by:** REV-002 (NATS auth -- setup script needs credentials)
**Blocks:** Event durability, replay capability, analytics pipeline
**Estimated effort:** 1 day
**Source:** System Review 2026-03-28 -- Solution Architect (Finding #1, NATS section 4.4)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

The NATS setup script (`nats-setup.sh`) creates 9 JetStream streams subscribing to subjects like `cena.learner.events.>`, `cena.pedagogy.events.>`, etc. However, the actual publishers (`NatsBusRouter` and `NatsOutboxPublisher`) emit events on subjects like `cena.events.session.started`, `cena.events.concept.attempted`. These subject patterns do NOT match -- JetStream captures nothing.

Additionally, two publishers emit overlapping events:
- `NatsBusRouter` publishes simplified bus events immediately
- `NatsOutboxPublisher` publishes full domain events from the Marten event store

Subscribers on `cena.events.>` receive both, causing duplicate processing.

## Architect's Decision

**Option A (recommended)**: Align the stream subjects to match what publishers actually emit. This is the lower-risk change -- modify `nats-setup.sh` rather than touching the publisher code paths which are production-tested.

**Option B**: Refactor publishers to emit on the stream-expected subjects (`cena.learner.events.>` etc.). Higher risk -- requires changing all publisher subject strings and consumer subscriptions.

Choose **Option A**. The subject hierarchy in `NatsSubjects.cs` is well-designed and already in use. The streams should match it.

For dual publishing: keep both publishers but assign distinct subject prefixes:
- `NatsBusRouter`: continues publishing on `cena.events.*` (real-time, best-effort)
- `NatsOutboxPublisher`: publishes on `cena.durable.*` (transactional, guaranteed)
- JetStream streams subscribe to `cena.durable.>` (only durable events)

## Subtasks

### REV-007.1: Update JetStream Stream Subjects

**File to modify:** `src/infra/docker/nats-setup.sh`

**Changes:**
```bash
# BEFORE (does not match publishers)
nats stream add LEARNER_EVENTS --subjects "cena.learner.events.>"

# AFTER (matches NatsOutboxPublisher subjects via new prefix)
nats stream add LEARNER_EVENTS --subjects "cena.durable.learner.>"
```

Full stream-to-subject mapping:
| Stream | Old Subject | New Subject |
|--------|------------|-------------|
| LEARNER_EVENTS | `cena.learner.events.>` | `cena.durable.learner.>` |
| PEDAGOGY_EVENTS | `cena.pedagogy.events.>` | `cena.durable.pedagogy.>` |
| ENGAGEMENT_EVENTS | `cena.engagement.events.>` | `cena.durable.engagement.>` |
| OUTREACH_EVENTS | `cena.outreach.events.>` | `cena.durable.outreach.>` |
| CURRICULUM_EVENTS | `cena.curriculum.events.>` | `cena.durable.curriculum.>` |
| ANALYTICS_EVENTS | `cena.analytics.events.>` | `cena.durable.analytics.>` |
| SCHOOL_EVENTS | `cena.school.events.>` | `cena.durable.school.>` |
| SYSTEM_HEALTH | `cena.system.health.>` | `cena.durable.system.>` |
| DEAD_LETTER | `cena.system.dlq.>` | `cena.durable.dlq.>` |

**Acceptance:**
- [ ] All 9 streams created with updated subjects
- [ ] `nats stream info LEARNER_EVENTS` shows subjects matching `cena.durable.learner.>`

### REV-007.2: Update NatsOutboxPublisher Subject Prefix

**File to modify:** `src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs`

**Change the subject construction** to use `cena.durable.{category}.{eventType}` instead of `cena.events.{eventType}`:

```csharp
// Map event types to stream categories
private static string GetDurableSubject(string eventTypeName) => eventTypeName switch
{
    var e when e.StartsWith("Concept") || e.StartsWith("Mastery") || e.StartsWith("Stagnation")
        => $"cena.durable.learner.{eventTypeName}",
    var e when e.StartsWith("Session") || e.StartsWith("Exercise") || e.StartsWith("Hint")
        => $"cena.durable.pedagogy.{eventTypeName}",
    var e when e.StartsWith("Xp") || e.StartsWith("Streak") || e.StartsWith("Badge")
        => $"cena.durable.engagement.{eventTypeName}",
    var e when e.StartsWith("Outreach")
        => $"cena.durable.outreach.{eventTypeName}",
    var e when e.StartsWith("Focus") || e.StartsWith("MindWandering") || e.StartsWith("Microbreak")
        => $"cena.durable.learner.{eventTypeName}",
    var e when e.StartsWith("Tutoring")
        => $"cena.durable.pedagogy.{eventTypeName}",
    var e when e.StartsWith("Question") || e.StartsWith("Pipeline") || e.StartsWith("File")
        => $"cena.durable.curriculum.{eventTypeName}",
    _ => $"cena.durable.system.{eventTypeName}"
};
```

**Acceptance:**
- [ ] NatsOutboxPublisher emits to `cena.durable.*` subjects
- [ ] NatsBusRouter continues emitting to `cena.events.*` subjects (unchanged)
- [ ] JetStream streams capture all outbox-published events
- [ ] `nats stream info LEARNER_EVENTS` shows message count > 0 after running emulator

### REV-007.3: Verify Stream Capture

**Test procedure:**
1. Start infrastructure: `docker compose up -d`
2. Start Actor Host
3. Start Emulator with 10 students for 30 seconds
4. Check stream stats:

```bash
nats stream ls
# All streams should show non-zero message counts

nats stream info LEARNER_EVENTS
# Should show messages matching ConceptAttempted, ConceptMastered, etc.
```

**Acceptance:**
- [ ] LEARNER_EVENTS stream has messages after emulator run
- [ ] PEDAGOGY_EVENTS stream has messages (session events)
- [ ] ENGAGEMENT_EVENTS stream has messages (XP, streaks)
- [ ] Admin API's `NatsEventSubscriber` still receives real-time events on `cena.events.>`
