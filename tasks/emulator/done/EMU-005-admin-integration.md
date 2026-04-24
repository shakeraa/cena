# EMU-005: Admin Dashboard Integration — Emulator Controls & Monitoring

**Priority:** P1 — visual feedback for emulator operation
**Blocked by:** EMU-004
**Estimated effort:** 1 day

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.**

## Context

The admin dashboard should show emulator status and provide basic controls (start/stop/speed). The Actor Dashboard already shows live actor stats; this task adds emulator-specific context.

## Subtasks

### EMU-005.1: Emulator Status API

**Files to create/modify:**
- `src/api/Cena.Admin.Api/EmulatorStatusEndpoints.cs`
- `src/api/Cena.Admin.Api/EmulatorStatusService.cs`

**Acceptance:**
- [ ] `GET /api/admin/emulator/status` — returns:
  ```json
  {
    "running": true,
    "simTime": "2026-03-29T16:45:00Z",
    "simDay": 1,
    "totalStudents": 1000,
    "activeStudents": 287,
    "maxConcurrent": 300,
    "queueDepth": 12,
    "totalSessionsToday": 423,
    "totalAttemptsToday": 8291,
    "peakConcurrencyToday": 298,
    "speedMultiplier": 60,
    "arrivalRate": 18.5,
    "avgSessionMinutes": 24.3
  }
  ```
- [ ] Data sourced from NATS `cena.emulator.metrics` subscription
- [ ] Returns `{ "running": false }` if emulator is not connected

### EMU-005.2: Actor Dashboard Enhancement

**Files to create/modify:**
- `src/admin/full-version/src/pages/apps/system/actors.vue`

**Acceptance:**
- [ ] Add "Emulator" section to Actor Dashboard showing:
  - Sim time and day
  - Active/max concurrent bar chart
  - Arrival rate over time (sparkline)
  - Session duration distribution (histogram)
- [ ] Show emulator status badge: "Running" (green) / "Stopped" (gray) / "Ramping" (yellow)
