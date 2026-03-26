# ACT-005: OutreachSchedulerActor — HLR Timers, Priority Queue, Throttling, Channel Routing

**Priority:** P1 — blocks spaced repetition delivery
**Blocked by:** ACT-001 (Cluster Bootstrap), INF-004 (Redis)
**Estimated effort:** 3 days
**Contract:** `contracts/actors/outreach_scheduler_actor.cs` (full implementation contract)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

The OutreachSchedulerActor is a classic child actor of StudentActor. It manages Half-Life Regression (HLR) timers for spaced repetition scheduling, prioritized reminder delivery, daily throttling (max 3 messages/day), quiet hours (22:00-07:00), and channel routing (WhatsApp > Push > Telegram > Voice). Publishes to NATS JetStream for the Outreach bounded context to dispatch.

## Subtasks

### ACT-005.1: HLR Timer Lifecycle

**Files to create/modify:**
- `src/Cena.Actors/OutreachSchedulerActor.cs`
- `src/Cena.Actors/Messages/OutreachMessages.cs`

**Acceptance:**
- [ ] `ConceptMasteredNotification` creates HLR timer with initial half-life from domain service
- [ ] Timer fires when predicted recall drops below 0.85: `p(t) = 2^(-delta/h)`, solve for delta
- [ ] Successful review doubles half-life, failed review halves it
- [ ] Periodic HLR check every 15 minutes as safety net
- [ ] Timer cancellation on concept re-mastery or student passivation

**Test:**
```csharp
[Fact]
public async Task HlrTimer_FiresWhenRecallDrops()
{
    var actor = CreateOutreachActor();
    await actor.Tell(new ConceptMasteredNotification("student-1", "math-1", initialHalfLifeHours: 1.0));
    // With h=1h, recall drops to 0.85 at delta = -1 * log2(0.85) ≈ 0.234h ≈ 14 min
    await AdvanceClock(TimeSpan.FromMinutes(15));
    var dispatched = await GetNatsMessages("cena.outreach.dispatch.*");
    Assert.Single(dispatched);
}
```

---

### ACT-005.2: Priority Queue + Throttling

**Files to create/modify:**
- `src/Cena.Actors/OutreachSchedulerActor.cs` (HandleReminderFire)

**Acceptance:**
- [ ] Priority order: StreakExpiring(1) > ReviewDue(2) > StagnationDetected(3) > SessionAbandoned(4) > CooldownComplete(5)
- [ ] Daily limit: 3 messages/day per student, reset at midnight in student's timezone
- [ ] When throttled: only priority 1 (StreakExpiring) breaks through
- [ ] Lower priority messages silently dropped (not deferred)
- [ ] Metrics: `cena.outreach.messages_throttled_total`

**Test:**
```csharp
[Fact]
public async Task Throttling_AllowsStreakExpiringWhenLimitReached()
{
    var actor = CreateOutreachActor();
    // Send 3 messages to hit limit
    for (int i = 0; i < 3; i++)
        await actor.Tell(new ScheduleReminder("stu-1", $"r-{i}", "ReviewDue", DateTimeOffset.UtcNow, OutreachChannel.Push));
    await FireAllReminders(actor);

    // 4th message: ReviewDue -> throttled
    await actor.Tell(new ScheduleReminder("stu-1", "r-4", "ReviewDue", DateTimeOffset.UtcNow, OutreachChannel.Push));
    await FireAllReminders(actor);
    var dispatched = await GetNatsMessages("cena.outreach.dispatch.*");
    Assert.Equal(3, dispatched.Count);

    // 5th message: StreakExpiring -> allowed despite throttle
    await actor.Tell(new ScheduleReminder("stu-1", "r-5", "StreakExpiring", DateTimeOffset.UtcNow, OutreachChannel.Push));
    await FireAllReminders(actor);
    dispatched = await GetNatsMessages("cena.outreach.dispatch.*");
    Assert.Equal(4, dispatched.Count);
}
```

---

### ACT-005.3: Quiet Hours + Timezone Handling

**Files to create/modify:**
- `src/Cena.Actors/OutreachSchedulerActor.cs` (IsQuietHoursNow, AdjustForQuietHours)

**Acceptance:**
- [ ] Default quiet hours: 22:00-07:00 in student's timezone (default: `Asia/Jerusalem`)
- [ ] Messages during quiet hours deferred to 07:00 next day
- [ ] Quiet hours configurable per student via `UpdateContactPreferences`
- [ ] Overnight quiet hours (start > end, e.g., 22:00-07:00) handled correctly
- [ ] Unknown timezone -> fall back to UTC with WARNING log

**Test:**
```csharp
[Fact]
public void QuietHours_DefersToMorning()
{
    var actor = CreateOutreachActorWithTime(new TimeOnly(23, 0)); // 11 PM
    var adjusted = actor.AdjustForQuietHours(DateTimeOffset.UtcNow);
    Assert.True(adjusted > DateTimeOffset.UtcNow);
    // Should be deferred to 07:00 next day
}
```

---

### ACT-005.4: Channel Routing

**Files to create/modify:**
- `src/Cena.Actors/OutreachSchedulerActor.cs` (SelectChannel)

**Acceptance:**
- [ ] Default preference: WhatsApp > Push > Telegram > Voice
- [ ] Preferred channel from reminder request honored if in student's preference list
- [ ] Channel preferences updatable via `UpdateContactPreferences` message
- [ ] NATS subject includes channel: `cena.outreach.dispatch.{channel}`

**Test:**
```csharp
[Fact]
public void ChannelRouting_UsesStudentPreference()
{
    var actor = CreateOutreachActor(preferences: new[] { OutreachChannel.Telegram, OutreachChannel.Push });
    var channel = actor.SelectChannel(OutreachChannel.WhatsApp);
    Assert.Equal(OutreachChannel.Telegram, channel); // WhatsApp not in prefs, use first pref
}
```

---

### ACT-005.5: NATS Dispatch + Telemetry

**Files to create/modify:**
- `src/Cena.Actors/OutreachSchedulerActor.cs` (DispatchToNats)

**Acceptance:**
- [ ] NATS JetStream publish with `Nats-Msg-Id` for deduplication
- [ ] Payload includes: studentId, reminderId, triggerType, channel, priority, language, timezone
- [ ] NATS publish failure: non-fatal, log ERROR, HLR check will retry
- [ ] Metrics: `cena.outreach.messages_dispatched_total` with trigger type and channel labels

**Test:**
```csharp
[Fact]
public async Task NatsDispatch_IncludesAllFields()
{
    await DispatchTestReminder();
    var msg = await GetLastNatsMessage("cena.outreach.dispatch.push");
    var payload = JsonSerializer.Deserialize<OutreachPayload>(msg.Data);
    Assert.NotEmpty(payload.StudentId);
    Assert.NotEmpty(payload.ReminderId);
    Assert.Equal("he", payload.Language);
}
```

---

## Rollback Criteria
- Disable outreach actor; students miss spaced repetition reminders but core learning continues

## Definition of Done
- [ ] All 5 subtasks pass their tests
- [ ] HLR timer fires at correct intervals
- [ ] Throttling, quiet hours, channel routing all working
- [ ] NATS dispatch verified in staging
- [ ] PR reviewed by architect
