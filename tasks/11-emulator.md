# 11 — Student Emulator Redesign

**Technology:** .NET 9, NATS, Proto.Actor (via NATS bus)
**Contract files:** `src/actors/Cena.Actors/Bus/NatsBusMessages.cs`, `config/emulator/emulator.yaml`
**Stage:** Parallel with content pipeline (Week 4+)
**Replaces:** Current `src/emulator/Program.cs` (300 students, replay-all-at-once)

---

## Design Principles

1. **1,000 students** with 8 archetypes, realistic demographic distribution
2. **Peak distribution** — sine-wave arrival curve modeling an Israeli school day
3. **30% max concurrency** — at most 300 active sessions at any time
4. **Students work and go** — sessions have realistic durations, students leave and return
5. **Zero timeout errors** — staggered cold-start, no burst activation

---

## EMU-001: Student Population Model

**Priority:** P0 | **Blocked by:** None | **Effort:** 2 days

- [ ] 1,000 student profiles: 8 archetypes, 5 schools, Hebrew/Arabic split
- [ ] Study habit profiles per archetype (session duration, sessions/day, peak hours)
- [ ] Deterministic generation (seeded RNG)
- See `tasks/emulator/EMU-001-population-model.md`

## EMU-002: Arrival Scheduler + Concurrency Limiter

**Priority:** P0 | **Blocked by:** EMU-001 | **Effort:** 2 days

- [ ] Daily schedule: sine-wave arrival rate, peak 14:00-18:00 Israel time
- [ ] Concurrency limiter: hard cap at 30% (300 of 1,000)
- [ ] Session lifecycle: arrive → study → focus degrade → break/leave → depart
- [ ] Weekend/Friday schedules
- See `tasks/emulator/EMU-002-arrival-scheduler.md`

## EMU-003: Realistic Session Behavior

**Priority:** P0 | **Blocked by:** EMU-002 | **Effort:** 3 days

- [ ] Dynamic concept attempt generation (not pre-computed replay)
- [ ] Focus degradation simulation with mind-wandering events
- [ ] Confusion/question annotations in Hebrew and Arabic
- [ ] Methodology switch on stagnation
- [ ] Realistic session end reasons (Completed/Fatigue/Abandoned/Timeout)
- See `tasks/emulator/EMU-003-session-behavior.md`

## EMU-004: Emulator Orchestrator

**Priority:** P0 | **Blocked by:** EMU-001, EMU-002, EMU-003 | **Effort:** 2 days

- [ ] Replace Program.cs with real-time simulation loop
- [ ] Time compression: `--speed 60` = 1 real second = 1 sim minute
- [ ] Console metrics dashboard every 10 seconds
- [ ] Warm-start stagger (ramp 10/s for first 30s on cold actor system)
- [ ] YAML configuration file
- [ ] Graceful shutdown (end all active sessions before exit)
- See `tasks/emulator/EMU-004-orchestrator.md`

## EMU-005: Admin Dashboard Integration

**Priority:** P1 | **Blocked by:** EMU-004 | **Effort:** 1 day

- [ ] Emulator status API (`GET /api/admin/emulator/status`)
- [ ] Actor Dashboard: emulator section with sim time, concurrency chart, arrival rate sparkline
- See `tasks/emulator/EMU-005-admin-integration.md`

---

## Total Effort: ~10 days

## Dependency Graph

```
EMU-001 (Population)
    ↓
EMU-002 (Scheduler)
    ↓
EMU-003 (Behavior)
    ↓
EMU-004 (Orchestrator)
    ↓
EMU-005 (Admin UI)
```
