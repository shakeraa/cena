# ACT-005: OutreachSchedulerActor (Classic, Timer-Based)

**Priority:** P1 — drives spaced repetition and re-engagement
**Blocked by:** ACT-001 (cluster), ACT-002 (StudentActor parent), EVT-003 (NATS JetStream), DATA-004 (Redis)
**Estimated effort:** 4 days
**Contract:** `contracts/actors/outreach_scheduler_actor.cs`, `contracts/backend/actor-contracts.cs` (lines 570-645), `contracts/backend/domain-services.cs` (lines 167-279)

---

## Context
The OutreachSchedulerActor is a classic child of the StudentActor that lives across sessions. It manages Half-Life Regression (HLR) timers for spaced repetition, streak expiration warnings, and outreach throttling. The actor IS the scheduler -- no external cron jobs needed. It enforces quiet hours (22:00-07:00 in the student's timezone), daily message limits (max 3/day), priority-based throttle bypass (only `StreakExpiring` can break the limit), and channel routing (WhatsApp > Push > Telegram > Voice). All dispatched messages go to NATS JetStream for the Outreach bounded context to handle delivery.

## Subtasks

### ACT-005.1: Actor Scaffold + State + Lifecycle
**Files to create/modify:**
- `src/Cena.Actors/Outreach/OutreachSchedulerActor.cs` -- main actor
- `src/Cena.Actors/Outreach/OutreachState.cs` -- persistent state
- `src/Cena.Actors/Outreach/HlrTimerState.cs` -- per-concept HLR state
- `src/Cena.Actors/Outreach/PendingReminder.cs` -- scheduled reminder record
- `src/Cena.Actors/Outreach/OutreachPriority.cs` -- priority enum

**Acceptance:**
- [ ] `OutreachSchedulerActor : IActor` with `INatsConnection` and `ILogger<OutreachSchedulerActor>` constructor (contract lines 189-195)
- [ ] `ReceiveAsync` dispatches: `Started`, `Stopping`, `ScheduleReminder`, `CancelReminder`, `UpdateContactPreferences`, `ConceptMasteredNotification`, `StreakStateUpdate`, `ReminderFireTick`, `HlrCheckTick` (contract lines 197-219)
- [ ] `OnStarted` starts periodic HLR check timer with 15-minute interval (contract lines 225-232)
- [ ] `OnStopping` cancels HLR check CTS and all pending reminder timers (contract lines 235-252)
- [ ] `OutreachState` has all fields from contract lines 37-84: `StudentId`, `HlrTimers` dict, `PendingReminders` (SortedDictionary by fire time), `ReminderIdToFireTime` dict, `CurrentStreak`, `LastActivityDate`, `StreakExpiryReminderSent`, `ChannelPreference` (default WhatsApp > Push > Telegram > Voice), `QuietHoursStart` (22:00), `QuietHoursEnd` (07:00), `Timezone` ("Asia/Jerusalem"), `ContentLanguage` ("he"), `MessagesSentToday`, `LastMessageDate`
- [ ] Constants: `MaxMessagesPerDay = 3`, `ReviewRecallThreshold = 0.85` (contract lines 83-84)
- [ ] `OutreachPriority` enum: `StreakExpiring=1`, `ReviewDue=2`, `StagnationDetected=3`, `SessionAbandoned=4`, `CooldownComplete=5` (contract lines 122-138)
- [ ] Telemetry: 4 counters -- `messages_scheduled_total`, `messages_dispatched_total`, `messages_throttled_total`, `messages_deferred_total` (contract lines 181-187)

**Test:**
```csharp
[Fact]
public async Task OutreachActor_StartsHlrCheckTimer()
{
    var actor = CreateTestOutreachActor();
    await ActivateActor(actor);

    // HLR check timer should be active
    Assert.NotNull(actor.HlrCheckCts);
    Assert.False(actor.HlrCheckCts.IsCancellationRequested);
}

[Fact]
public async Task OutreachActor_StoppingCancelsAllTimers()
{
    var actor = CreateTestOutreachActor();
    await ActivateActor(actor);

    // Schedule some reminders
    await SendMessage(actor, CreateTestScheduleReminder("r-1",
        DateTimeOffset.UtcNow.AddHours(1)));
    await SendMessage(actor, CreateTestScheduleReminder("r-2",
        DateTimeOffset.UtcNow.AddHours(2)));

    await StopActor(actor);

    Assert.True(actor.HlrCheckCts!.IsCancellationRequested);
    Assert.True(actor.State.PendingReminders.Values
        .All(r => r.TimerCts == null || r.TimerCts.IsCancellationRequested));
}

[Fact]
public void OutreachState_DefaultChannelPreference()
{
    var state = new OutreachState();
    Assert.Equal(new[] {
        OutreachChannel.WhatsApp,
        OutreachChannel.Push,
        OutreachChannel.Telegram,
        OutreachChannel.Voice
    }, state.ChannelPreference);
}
```

---

### ACT-005.2: HLR Timer Management (Concept Mastered + Periodic Check)
**Files to create/modify:**
- `src/Cena.Actors/Outreach/OutreachSchedulerActor.cs` -- `HandleConceptMastered()`, `HandleHlrCheck()`

**Acceptance:**
- [ ] `HandleConceptMastered`: creates `HlrTimerState` with `HalfLifeHours = notification.InitialHalfLifeHours`, `LastReviewAt = now`, `SuccessfulReviews = 0` (contract lines 464-473)
- [ ] Computes first review time: `hours = -halfLife * log2(0.85)` (solve `0.85 = 2^(-delta/h)`) (contract lines 476-478)
- [ ] Schedules `ReviewDue` reminder via `ScheduleReminder` self-message (contract lines 480-486)
- [ ] `HandleHlrCheck` (periodic, every 15 min): scans all HLR timers, computes `predictedRecall = 2^(-deltaHours / halfLife)` (contract lines 501-509)
- [ ] If recall < 0.85 and no reminder scheduled: schedules review 5 minutes from now (safety net) (contract lines 511-527)
- [ ] Re-schedules next HLR check timer after each run (contract line 531)

**Test:**
```csharp
[Fact]
public async Task ConceptMastered_CreatesHlrTimer()
{
    var actor = CreateTestOutreachActor();
    await ActivateActor(actor);

    await SendMessage(actor, new ConceptMasteredNotification(
        "stu-1", "algebra-1", InitialHalfLifeHours: 24));

    Assert.True(actor.State.HlrTimers.ContainsKey("algebra-1"));
    var hlr = actor.State.HlrTimers["algebra-1"];
    Assert.Equal(24, hlr.HalfLifeHours);
    Assert.Equal(0, hlr.SuccessfulReviews);
    Assert.NotNull(hlr.ScheduledReminderId);
}

[Fact]
public async Task ConceptMastered_SchedulesFirstReview()
{
    var actor = CreateTestOutreachActor();
    await ActivateActor(actor);

    await SendMessage(actor, new ConceptMasteredNotification(
        "stu-1", "algebra-1", InitialHalfLifeHours: 24));

    // Expected: -24 * log2(0.85) = ~5.57 hours
    double expectedHours = -24 * Math.Log2(0.85);
    Assert.True(actor.State.PendingReminders.Count >= 1);

    var reminder = actor.State.PendingReminders.Values.First();
    Assert.Equal("ReviewDue", reminder.TriggerType);
    Assert.Equal("algebra-1", reminder.ConceptId);

    var delay = (reminder.ScheduledAt - DateTimeOffset.UtcNow).TotalHours;
    Assert.InRange(delay, expectedHours - 0.5, expectedHours + 0.5);
}

[Fact]
public async Task HlrCheck_SchedulesReviewForDecayedConcepts()
{
    var actor = CreateTestOutreachActor();
    await ActivateActor(actor);

    // Manually add an HLR timer with decayed recall
    actor.State.HlrTimers["c-1"] = new HlrTimerState
    {
        HalfLifeHours = 24,
        LastReviewAt = DateTimeOffset.UtcNow.AddDays(-5), // Heavily decayed
        ScheduledReminderId = null // No reminder scheduled
    };

    await SendMessage(actor, new HlrCheckTick());

    // Should have scheduled a review
    Assert.NotNull(actor.State.HlrTimers["c-1"].ScheduledReminderId);
    Assert.True(actor.State.PendingReminders.Count >= 1);
}

[Fact]
public async Task HlrCheck_SkipsConcepts_WithExistingReminder()
{
    var actor = CreateTestOutreachActor();
    await ActivateActor(actor);

    actor.State.HlrTimers["c-1"] = new HlrTimerState
    {
        HalfLifeHours = 24,
        LastReviewAt = DateTimeOffset.UtcNow.AddDays(-5),
        ScheduledReminderId = "already-scheduled" // Already has reminder
    };

    await SendMessage(actor, new HlrCheckTick());

    // No additional reminder created
    Assert.Equal("already-scheduled", actor.State.HlrTimers["c-1"].ScheduledReminderId);
}

[Fact]
public void HlrRecallFormula_Correct()
{
    // p(t) = 2^(-delta/h)
    double halfLife = 24;
    double delta = 24; // exactly 1 half-life
    double recall = Math.Pow(2, -delta / halfLife);
    Assert.Equal(0.5, recall, 3); // 50% at 1 half-life

    delta = 0;
    recall = Math.Pow(2, -delta / halfLife);
    Assert.Equal(1.0, recall, 3); // 100% at t=0

    delta = 48; // 2 half-lives
    recall = Math.Pow(2, -delta / halfLife);
    Assert.Equal(0.25, recall, 3); // 25% at 2 half-lives
}
```

**Edge cases:**
- `InitialHalfLifeHours` is 0 -> division by zero in recall formula -> guard with `Math.Max(1, halfLife)`
- Concept mastered twice (re-mastered after decay) -> HLR timer overwritten with new initial half-life
- HLR check finds 50+ concepts needing review -> all get scheduled (no throttle on scheduling)

---

### ACT-005.3: Quiet Hours + Throttling + Priority Queue
**Files to create/modify:**
- `src/Cena.Actors/Outreach/OutreachSchedulerActor.cs` -- `HandleReminderFire()`, `AdjustForQuietHours()`, `IsQuietHoursNow()`, `ResetDailyCounterIfNeeded()`

**Acceptance:**
- [ ] `HandleScheduleReminder`: maps trigger type to `OutreachPriority` enum (contract lines 269-277)
- [ ] `AdjustForQuietHours`: converts scheduled time to student timezone, checks against `QuietHoursStart`/`QuietHoursEnd`, defers to next `QuietHoursEnd` if in quiet window (contract lines 692-727)
- [ ] Overnight quiet hours handled: 22:00-07:00 spans midnight (contract lines 639, 701)
- [ ] `HandleReminderFire`: re-checks quiet hours at fire time (timezone may have changed), defers if needed (contract lines 382-406)
- [ ] `ResetDailyCounterIfNeeded`: resets `MessagesSentToday` at midnight in student timezone (contract lines 732-756)
- [ ] Daily throttle: max 3 messages/day (contract line 412)
- [ ] Throttle bypass: only `OutreachPriority.StreakExpiring` (priority 1) breaks the limit (contract lines 414-416)
- [ ] Throttled messages are discarded (not deferred), counter incremented (contract lines 417-427)
- [ ] `HandleCancelReminder`: cancels timer CTS, removes from both maps, idempotent (contract lines 335-352)
- [ ] Reminder ID idempotency: scheduling same ID replaces existing (contract lines 289-293)

**Test:**
```csharp
[Fact]
public async Task QuietHours_DefersReminderToMorning()
{
    var actor = CreateTestOutreachActor();
    actor.State.Timezone = "Asia/Jerusalem";
    actor.State.QuietHoursStart = new TimeOnly(22, 0);
    actor.State.QuietHoursEnd = new TimeOnly(7, 0);
    await ActivateActor(actor);

    // Schedule reminder at 23:00 local (in quiet hours)
    var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");
    var localMidnight = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).Date.AddDays(1);
    var at23 = new DateTimeOffset(localMidnight.AddHours(-1), tz.GetUtcOffset(localMidnight.AddHours(-1)));

    await SendMessage(actor, new ScheduleReminder(
        "stu-1", "r-1", "ReviewDue", at23,
        OutreachChannel.Push, "c-1", "standard"));

    var reminder = actor.State.PendingReminders.Values.First();
    var localFireTime = TimeZoneInfo.ConvertTime(reminder.ScheduledAt, tz);
    var fireTimeOnly = TimeOnly.FromDateTime(localFireTime.DateTime);

    // Should be deferred to 07:00
    Assert.True(fireTimeOnly >= new TimeOnly(7, 0),
        $"Expected fire at or after 07:00, got {fireTimeOnly}");
}

[Fact]
public async Task Throttle_BlocksAfter3Messages()
{
    var actor = CreateTestOutreachActor();
    actor.State.MessagesSentToday = 3; // Already at limit
    actor.State.LastMessageDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
    await ActivateActor(actor);

    // Schedule and fire a ReviewDue reminder (priority 2)
    await SendMessage(actor, new ScheduleReminder(
        "stu-1", "r-throttled", "ReviewDue",
        DateTimeOffset.UtcNow.AddSeconds(-1), // Fire immediately
        OutreachChannel.Push, "c-1", "standard"));

    await SendMessage(actor, new ReminderFireTick("r-throttled"));

    // Should NOT dispatch to NATS
    var natsMessages = GetNatsPublished();
    Assert.DoesNotContain(natsMessages, m => m.Contains("r-throttled"));
}

[Fact]
public async Task Throttle_StreakExpiring_BypassesLimit()
{
    var actor = CreateTestOutreachActor();
    actor.State.MessagesSentToday = 3; // At limit
    actor.State.LastMessageDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
    await ActivateActor(actor);

    // Schedule StreakExpiring (priority 1)
    await SendMessage(actor, new ScheduleReminder(
        "stu-1", "r-streak", "StreakExpiring",
        DateTimeOffset.UtcNow.AddSeconds(-1),
        OutreachChannel.Push, null, "high"));

    await SendMessage(actor, new ReminderFireTick("r-streak"));

    // SHOULD dispatch despite throttle
    var natsMessages = GetNatsPublished();
    Assert.Contains(natsMessages, m => m.Contains("r-streak"));
}

[Fact]
public async Task CancelReminder_Idempotent()
{
    var actor = CreateTestOutreachActor();
    await ActivateActor(actor);

    await SendMessage(actor, new ScheduleReminder(
        "stu-1", "r-cancel", "ReviewDue",
        DateTimeOffset.UtcNow.AddHours(1),
        OutreachChannel.Push, "c-1", "standard"));

    // Cancel twice -- should not error
    var result1 = await SendMessage<ActorResult>(actor,
        new CancelReminder("stu-1", "r-cancel"));
    Assert.True(result1.Success);

    var result2 = await SendMessage<ActorResult>(actor,
        new CancelReminder("stu-1", "r-cancel"));
    Assert.True(result2.Success);

    Assert.Empty(actor.State.PendingReminders);
}

[Fact]
public async Task ScheduleReminder_SameId_ReplacesExisting()
{
    var actor = CreateTestOutreachActor();
    await ActivateActor(actor);

    var time1 = DateTimeOffset.UtcNow.AddHours(1);
    var time2 = DateTimeOffset.UtcNow.AddHours(2);

    await SendMessage(actor, new ScheduleReminder(
        "stu-1", "r-dup", "ReviewDue", time1,
        OutreachChannel.Push, "c-1", "standard"));
    await SendMessage(actor, new ScheduleReminder(
        "stu-1", "r-dup", "ReviewDue", time2,
        OutreachChannel.Push, "c-1", "standard"));

    // Only one reminder in the map
    Assert.Single(actor.State.ReminderIdToFireTime);
}

[Fact]
public void DailyCounter_ResetsAtMidnight()
{
    var actor = CreateTestOutreachActor();
    actor.State.MessagesSentToday = 3;
    actor.State.LastMessageDate = DateOnly.FromDateTime(
        DateTimeOffset.UtcNow.AddDays(-1).DateTime);

    actor.ResetDailyCounterIfNeeded();

    Assert.Equal(0, actor.State.MessagesSentToday);
}
```

**Edge cases:**
- Unknown timezone string -> falls back to UTC quiet hours check (contract lines 649-655)
- `QuietHoursStart == QuietHoursEnd` -> no quiet hours in effect
- Reminder fires but already cancelled (race) -> logs debug, returns silently (contract lines 367-370)
- Negative delay (past fire time) -> fires immediately via `context.Send` (contract lines 300-304)

---

### ACT-005.4: Streak Tracking + Contact Preferences
**Files to create/modify:**
- `src/Cena.Actors/Outreach/OutreachSchedulerActor.cs` -- `HandleStreakUpdate()`, `HandleUpdatePreferences()`

**Acceptance:**
- [ ] `HandleStreakUpdate`: sets `CurrentStreak` and `LastActivityDate` from update message (contract lines 547-548)
- [ ] Resets `StreakExpiryReminderSent` flag to false (contract line 549)
- [ ] If streak > 0: schedules `StreakExpiring` reminder 4 hours before 24-hour expiry (contract lines 550-568)
- [ ] Warning time: `lastActivity.Date.AddDays(1).AddHours(24).AddHours(-4)` (contract line 553)
- [ ] Only schedules if warning time is in the future (contract line 556)
- [ ] `HandleUpdatePreferences`: updates `ChannelPreference`, `QuietHoursStart`, `QuietHoursEnd`, `Timezone`, `ContentLanguage` (contract lines 581-599)
- [ ] `SelectChannel()`: uses preferred if in student's list, else first available, fallback to Push (contract lines 610-619)

**Test:**
```csharp
[Fact]
public async Task StreakUpdate_SchedulesExpiryWarning()
{
    var actor = CreateTestOutreachActor();
    await ActivateActor(actor);

    await SendMessage(actor, new StreakStateUpdate(
        "stu-1", CurrentStreak: 7,
        LastActivityDate: DateTimeOffset.UtcNow));

    Assert.Equal(7, actor.State.CurrentStreak);
    Assert.False(actor.State.StreakExpiryReminderSent);

    // Should have scheduled a StreakExpiring reminder
    var streakReminders = actor.State.PendingReminders.Values
        .Where(r => r.TriggerType == "StreakExpiring");
    Assert.Single(streakReminders);
}

[Fact]
public async Task StreakUpdate_ZeroStreak_NoReminder()
{
    var actor = CreateTestOutreachActor();
    await ActivateActor(actor);

    await SendMessage(actor, new StreakStateUpdate(
        "stu-1", CurrentStreak: 0,
        LastActivityDate: DateTimeOffset.UtcNow));

    var streakReminders = actor.State.PendingReminders.Values
        .Where(r => r.TriggerType == "StreakExpiring");
    Assert.Empty(streakReminders);
}

[Fact]
public async Task UpdatePreferences_ChangesChannelOrder()
{
    var actor = CreateTestOutreachActor();
    await ActivateActor(actor);

    var result = await SendMessage<ActorResult>(actor,
        new UpdateContactPreferences(
            "stu-1",
            new[] { OutreachChannel.Telegram, OutreachChannel.Push },
            new TimeOnly(23, 0), new TimeOnly(8, 0),
            "Europe/London", "en"));

    Assert.True(result.Success);
    Assert.Equal(OutreachChannel.Telegram, actor.State.ChannelPreference[0]);
    Assert.Equal(new TimeOnly(23, 0), actor.State.QuietHoursStart);
    Assert.Equal("en", actor.State.ContentLanguage);
}

[Fact]
public void SelectChannel_FallsBackThroughPreferences()
{
    var actor = CreateTestOutreachActor();
    actor.State.ChannelPreference = new List<OutreachChannel>
    {
        OutreachChannel.Telegram, OutreachChannel.Push
    };

    // Preferred WhatsApp not in list -> uses first preference (Telegram)
    var selected = actor.SelectChannel(OutreachChannel.WhatsApp);
    Assert.Equal(OutreachChannel.Telegram, selected);

    // Preferred Push is in list -> use it
    selected = actor.SelectChannel(OutreachChannel.Push);
    Assert.Equal(OutreachChannel.Push, selected);
}
```

---

### ACT-005.5: NATS JetStream Dispatch
**Files to create/modify:**
- `src/Cena.Actors/Outreach/OutreachSchedulerActor.cs` -- `DispatchToNats()`

**Acceptance:**
- [ ] Creates `NatsJSContext` from injected `INatsConnection` (contract line 770)
- [ ] Publishes to subject `cena.outreach.dispatch.{channel}` (lowercase) (contract line 771)
- [ ] Payload includes: `StudentId`, `ReminderId`, `TriggerType`, `Channel`, `ConceptId`, `Priority` (int), `Language`, `Timezone`, `Timestamp` (contract lines 773-784)
- [ ] `MessagesSentToday` incremented AFTER successful NATS publish (contract line 435)
- [ ] `MessagesDispatchedCounter` incremented with tags: trigger.type, channel, priority (contract lines 437-440)
- [ ] NATS failure: caught, logged as ERROR, non-fatal (reminder state preserved, HLR check retries) (contract lines 788-796)

**Test:**
```csharp
[Fact]
public async Task DispatchToNats_PublishesCorrectSubject()
{
    var nats = new MockNatsConnection();
    var actor = CreateTestOutreachActor(nats: nats);
    await ActivateActor(actor);

    var reminder = new PendingReminder(
        "r-1", "ReviewDue", OutreachChannel.WhatsApp,
        "algebra-1", OutreachPriority.ReviewDue,
        DateTimeOffset.UtcNow, null);

    await actor.DispatchToNats(reminder, OutreachChannel.WhatsApp);

    Assert.Single(nats.Published);
    Assert.Equal("cena.outreach.dispatch.whatsapp", nats.Published[0].Subject);
}

[Fact]
public async Task DispatchToNats_IncludesAllPayloadFields()
{
    var nats = new MockNatsConnection();
    var actor = CreateTestOutreachActor(nats: nats);
    actor.State.StudentId = "stu-1";
    actor.State.ContentLanguage = "he";
    actor.State.Timezone = "Asia/Jerusalem";
    await ActivateActor(actor);

    var reminder = new PendingReminder(
        "r-1", "StreakExpiring", OutreachChannel.Push,
        null, OutreachPriority.StreakExpiring,
        DateTimeOffset.UtcNow, null);

    await actor.DispatchToNats(reminder, OutreachChannel.Push);

    var payload = nats.Published[0].Payload;
    Assert.Equal("stu-1", payload.StudentId);
    Assert.Equal("r-1", payload.ReminderId);
    Assert.Equal("StreakExpiring", payload.TriggerType);
    Assert.Equal("Push", payload.Channel);
    Assert.Equal(1, payload.Priority); // StreakExpiring = 1
    Assert.Equal("he", payload.Language);
}

[Fact]
public async Task DispatchToNats_Failure_NonFatal()
{
    var nats = new MockNatsConnection { ShouldThrow = true };
    var actor = CreateTestOutreachActor(nats: nats);
    await ActivateActor(actor);

    var reminder = new PendingReminder(
        "r-1", "ReviewDue", OutreachChannel.Push,
        "c-1", OutreachPriority.ReviewDue,
        DateTimeOffset.UtcNow, null);

    // Should not throw
    await actor.DispatchToNats(reminder, OutreachChannel.Push);

    // Verify error was logged (check log output)
    Assert.True(actor.Logger.ContainsError("Failed to dispatch reminder"));
}

[Fact]
public async Task FullReminderFire_IncrementsMessageCount()
{
    var nats = new MockNatsConnection();
    var actor = CreateTestOutreachActor(nats: nats);
    actor.State.MessagesSentToday = 0;
    actor.State.LastMessageDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
    await ActivateActor(actor);

    await SendMessage(actor, new ScheduleReminder(
        "stu-1", "r-fire", "ReviewDue",
        DateTimeOffset.UtcNow.AddSeconds(-1),
        OutreachChannel.Push, "c-1", "standard"));

    await SendMessage(actor, new ReminderFireTick("r-fire"));

    Assert.Equal(1, actor.State.MessagesSentToday);
    Assert.Single(nats.Published);
}
```

**Edge cases:**
- NATS connection is closed -> publish throws, caught, logged, actor continues
- Channel is `Voice` (unusual) -> subject becomes `cena.outreach.dispatch.voice`
- Reminder fires twice (race between timer and explicit trigger) -> second fire finds no entry, returns silently

---

## Integration Test (full outreach lifecycle)

```csharp
[Fact]
public async Task OutreachScheduler_FullLifecycle()
{
    var nats = new MockNatsConnection();
    var actor = CreateTestOutreachActor(nats: nats);
    await ActivateActor(actor);

    // 1. Concept mastered -> HLR timer created + review scheduled
    await SendMessage(actor, new ConceptMasteredNotification(
        "stu-1", "algebra-1", InitialHalfLifeHours: 24));

    Assert.True(actor.State.HlrTimers.ContainsKey("algebra-1"));
    Assert.True(actor.State.PendingReminders.Count >= 1);

    // 2. Streak update -> expiry warning scheduled
    await SendMessage(actor, new StreakStateUpdate(
        "stu-1", CurrentStreak: 5, LastActivityDate: DateTimeOffset.UtcNow));

    var streakReminders = actor.State.PendingReminders.Values
        .Where(r => r.TriggerType == "StreakExpiring").ToList();
    Assert.Single(streakReminders);

    // 3. Update preferences -> change channel + quiet hours
    await SendMessage(actor, new UpdateContactPreferences(
        "stu-1",
        new[] { OutreachChannel.Telegram, OutreachChannel.Push },
        new TimeOnly(23, 0), new TimeOnly(8, 0),
        "Europe/London", "en"));

    Assert.Equal(OutreachChannel.Telegram, actor.State.ChannelPreference[0]);

    // 4. Fire a reminder -> dispatched to NATS
    var reviewReminder = actor.State.PendingReminders.Values
        .First(r => r.TriggerType == "ReviewDue");
    await SendMessage(actor, new ReminderFireTick(reviewReminder.ReminderId));

    Assert.Single(nats.Published);
    Assert.Contains("telegram", nats.Published[0].Subject); // Updated preference
    Assert.Equal(1, actor.State.MessagesSentToday);

    // 5. Send 2 more messages to hit throttle
    for (int i = 0; i < 2; i++)
    {
        var r = CreateTestScheduleReminder($"r-{i}",
            DateTimeOffset.UtcNow.AddSeconds(-1));
        await SendMessage(actor, r);
        await SendMessage(actor, new ReminderFireTick($"r-{i}"));
    }

    Assert.Equal(3, actor.State.MessagesSentToday);

    // 6. 4th message throttled (unless StreakExpiring)
    await SendMessage(actor, CreateTestScheduleReminder("r-throttled",
        DateTimeOffset.UtcNow.AddSeconds(-1), "SessionAbandoned"));
    await SendMessage(actor, new ReminderFireTick("r-throttled"));

    Assert.Equal(3, nats.Published.Count); // Still 3, throttled

    // 7. StreakExpiring bypasses throttle
    await SendMessage(actor, CreateTestScheduleReminder("r-streak-bypass",
        DateTimeOffset.UtcNow.AddSeconds(-1), "StreakExpiring"));
    await SendMessage(actor, new ReminderFireTick("r-streak-bypass"));

    Assert.Equal(4, nats.Published.Count); // Bypassed throttle

    // 8. HLR periodic check (safety net)
    actor.State.HlrTimers["algebra-2"] = new HlrTimerState
    {
        HalfLifeHours = 12,
        LastReviewAt = DateTimeOffset.UtcNow.AddDays(-3),
        ScheduledReminderId = null
    };
    await SendMessage(actor, new HlrCheckTick());
    Assert.NotNull(actor.State.HlrTimers["algebra-2"].ScheduledReminderId);

    // 9. Cancel a reminder
    var cancelTarget = actor.State.PendingReminders.Values.First();
    await SendMessage(actor, new CancelReminder("stu-1", cancelTarget.ReminderId));
    Assert.DoesNotContain(cancelTarget.ReminderId,
        actor.State.ReminderIdToFireTime.Keys);
}
```

## Performance Benchmarks
- `HandleScheduleReminder`: < 100 microseconds (SortedDictionary insert)
- `HandleReminderFire`: < 1ms (includes NATS publish)
- `HandleHlrCheck` for 100 concepts: < 5ms
- `AdjustForQuietHours`: < 50 microseconds (timezone conversion)
- Memory per actor: ~20KB base + ~200 bytes per HLR timer + ~500 bytes per pending reminder

## Rollback Criteria
- If NATS dispatch is unreliable: queue locally and retry on HLR check (batch dispatch)
- If quiet hours logic has timezone bugs: fall back to UTC-only quiet hours
- If throttling is too aggressive: raise `MaxMessagesPerDay` from 3 to 5
- If HLR timers drift significantly: shorten periodic check interval from 15 min to 5 min

## Definition of Done
- [ ] All 5 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `dotnet test --filter "Category=OutreachScheduler"` -> 0 failures
- [ ] HLR formula verified: hand-calculated recall at t=0, t=h, t=2h matches implementation
- [ ] Quiet hours tested for 3 timezones: Asia/Jerusalem, US/Eastern, UTC
- [ ] Throttle verified: 3 ReviewDue blocked, StreakExpiring passes
- [ ] NATS failure resilience: actor survives 10 consecutive NATS failures
- [ ] PR reviewed by architect (you)
