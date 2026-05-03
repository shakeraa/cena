# EMU-004: Emulator Orchestrator — Main Loop, Metrics, Graceful Shutdown

**Priority:** P0 — replaces current Program.cs
**Blocked by:** EMU-001, EMU-002, EMU-003
**Estimated effort:** 2 days
**Contract:** `src/emulator/Program.cs`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.**

## Context

The new emulator orchestrator replaces the current "replay all history" approach with a real-time simulation loop. It manages the daily schedule, spawns student sessions, monitors concurrency, and provides live metrics.

## Subtasks

### EMU-004.1: Main Simulation Loop

**Files to create/modify:**
- `src/emulator/Program.cs` (rewrite)
- `src/emulator/EmulatorOrchestrator.cs`

**Acceptance:**
- [ ] CLI args: `--students 1000 --max-concurrent 300 --speed 60 --nats nats://localhost:4222`
  - `--speed 60`: 1 real second = 60 simulated seconds (1 minute). Default: 60x.
  - `--max-concurrent`: hard cap on active sessions (default: 30% of students)
- [ ] Orchestrator loop:
  ```
  while (!cancelled):
    simTime += realDeltaTime * speedMultiplier

    // Arrival: check if new students should arrive based on schedule
    for each eligible student not currently in session:
      if schedule.shouldArrive(student, simTime) AND limiter.hasSlot():
        spawn studentSession(student)

    // Active sessions: tick each active session
    for each active session:
      session.tick(simTime)  // generates attempts, focus events, annotations
      if session.isComplete():
        session.end()
        limiter.release()

    // Metrics: emit every 10s
    emitMetrics()

    await Task.Delay(100ms)  // 10 ticks/second real time
  ```
- [ ] Time compression: at 60x speed, a 30-minute session completes in 30 real seconds
- [ ] Day cycle: after 24 simulated hours, start a new day (students can return)
- [ ] Graceful shutdown: Ctrl+C sends session end for all active students before exit

### EMU-004.2: Live Metrics Dashboard (Console)

**Files to create/modify:**
- `src/emulator/Metrics/EmulatorMetrics.cs`

**Acceptance:**
- [ ] Console output every 10 seconds:
  ```
  [14:32:15] Day 1, 16:45 sim | Active: 287/300 | Queue: 12 | Sessions: 423 | Attempts: 8,291 | Errors: 0
             Arrivals/min: 18 | Departures/min: 15 | Avg session: 24m | Peak today: 298
  ```
- [ ] Track: total sessions started, total attempts, total errors, peak concurrency, avg session duration
- [ ] NATS event: `cena.emulator.metrics` published every 30s for the admin dashboard to consume

### EMU-004.3: Warm-Start Mode (Stagger Activation)

**Files to create/modify:**
- `src/emulator/EmulatorOrchestrator.cs`

**Acceptance:**
- [ ] On first startup with empty actor system: stagger student activation over 5 minutes
  - Don't send 300 session starts at once
  - Ramp: 10/s for first 30s, then full arrival rate
- [ ] On restart with warm actors: skip stagger, use normal arrival rate
- [ ] Detection: check `/api/actors/stats` — if `activeActorCount > 0`, actors are warm

### EMU-004.4: Configuration File

**Files to create/modify:**
- `config/emulator/emulator.yaml`

**Acceptance:**
- [ ] All emulator parameters configurable:
  ```yaml
  emulator:
    students: 1000
    maxConcurrentPercent: 30
    speedMultiplier: 60
    staggerOnColdStart: true
    staggerDurationSeconds: 300

  schedule:
    peakHourStart: 14
    peakHourEnd: 18
    peakArrivalRate: 25  # students/min
    offPeakArrivalRate: 5
    fridayShutdownHour: 14
    weekendVolumePercent: 60

  behavior:
    minAttemptIntervalMs: 500    # compressed time between attempts
    maxAttemptIntervalMs: 3000
    confusionAnnotationRate: 0.10
    questionAnnotationRate: 0.07
    microbreakProbability: 0.20
    abandonmentRatePerMinute: 0.05
  ```
