# ACT-021: Implement Outreach Timer Scheduling

**Priority:** P2 — outreach is non-functional without periodic triggers
**Blocked by:** None
**Estimated effort:** 0.5 days
**Source:** Actor system review — missing timer scheduling for OutreachSchedulerActor

---

## Context
`OutreachSchedulerActor` handles `CheckHlrTimers` and `CheckStreakExpiry` messages but never schedules them itself. No external component sends these periodic messages either. As a result, HLR review reminders and streak expiry warnings are never triggered.

## Subtasks

### ACT-021.1: Add Self-Scheduling Timers
**Files:**
- `src/actors/Cena.Actors/Outreach/OutreachSchedulerActor.cs` — modify

**Acceptance:**
- [ ] On `Started` message, schedule periodic `CheckHlrTimers` every 15 minutes via self-send pattern
- [ ] On `Started` message, schedule periodic `CheckStreakExpiry` every 30 minutes
- [ ] Use Proto.Actor-safe pattern: capture `context.Self` and `context.System`, use `Task.Delay` + `system.Root.Send(self, msg)`
- [ ] Cancel timers on `Stopping`
- [ ] Add `Started` and `Stopping` handlers to `ReceiveAsync` switch

### ACT-021.2: Timer Configuration
**Acceptance:**
- [ ] Timer intervals configurable via constants (not hardcoded in timer setup)
- [ ] `CheckHlrTimersIntervalMinutes = 15`
- [ ] `CheckStreakExpiryIntervalMinutes = 30`
- [ ] Unit test: verify timers are started on actor activation
