# RDY-075 — F4: Offline PWA with sync conflict resolution

- **Wave**: D (needs sync design doc before build)
- **Priority**: MED (market reach for Tariq cohort + phone-only users)
- **Effort**: 6 engineer-weeks (Iman's realistic estimate, not 1 sprint)
- **Dependencies**: existing PWA shell; session pre-pack schema design
- **Source**: [panel review](../../docs/research/cena-panel-review-user-personas-2026-04-17.md) Round 2.F4

## Problem

Tariq (rural Druze, intermittent 3G on bus) and Sarah (phone-only, shared bandwidth) are both excluded by always-online assumptions. Cellular data costs in rural Israel/Palestine are real. If the app doesn't work offline on Tariq's commute, it doesn't work for him at all.

## Scope

**Session pre-pack**:
- On school wifi, student taps "Download today's session" → 15-30 min of problems, hints, assets, CAS snapshots cached to service worker
- Max 30 MB per pre-pack
- Includes next-likely items per adaptive schedule (pre-resolve top-3 branches)

**Offline runtime**:
- Student completes problems offline, answers queued locally with timestamp + item-version
- "Last sync" indicator visible; stale-state warning after 24h

**Sync on reconnect** (Dina + Oren + Iman):
- Idempotent: re-submitting an answer event does not corrupt mastery
- Order-tolerant: out-of-order events (3 sessions batched) cannot corrupt state
- **Item-version-freeze** (Dina's rule): frozen item difficulty/CAS at time served; server-side recalibration does not retroactively affect grading
- Conflict resolution: events are additive (student answered X at T) — no field merges needed if we serialize properly

**"Cache empty" UX** (Rami's challenge):
- When pre-pack runs out: calm message, "more items will be here next time you connect"
- Do NOT silently spin or show an error

## Files to Create / Modify

- `src/student/full-version/src/serviceworker/session-prepack.ts`
- `src/student/full-version/src/stores/offlineAnswerQueue.ts`
- `src/shared/Cena.Domain/Sessions/ItemVersionFreeze.cs`
- `src/api/Cena.Student.Api/Features/Sessions/SyncOnReconnect.cs` — idempotent ingest
- `docs/engineering/offline-sync-design.md` — MUST ship before build starts
- `ops/grafana/offline-sync-dashboard.json` — Iman's requirement

## Acceptance Criteria

- [ ] Design doc (`offline-sync-design.md`) approved by Dina + Iman BEFORE implementation begins
- [ ] Pre-pack downloads in ≤ 30s on 3G
- [ ] 30-min offline session runs to completion without crash
- [ ] Answers sync within 30s of reconnect; duplicates ignored
- [ ] Item-version-freeze verified: recalibrating an item mid-offline-session does not change the student's experience or grade
- [ ] Cache-empty UX tested: graceful message, no error spinner
- [ ] Grafana dashboard tracks queue depth, sync latency, failure rate

## Success Metrics

- **Offline session completion rate**: target ≥ 85% (vs online ~70%)
- **Sync success rate**: target ≥ 99%
- **Reconnect-to-sync latency p99**: target < 60s
- **Rural-cohort retention delta**: target measurable uplift for users with low connectivity

## ADR Alignment

- ADR-0002: cached items carry CAS snapshot for offline grading
- ADR-0003: no long-term offline-session analytics beyond current session
- Item-version-freeze consistent with event-sourcing principles

## Out of Scope

- Full offline mode for ALL features (diagnostic, social features stay online-only for v1)
- Peer-to-peer sync (no; always cloud as source of truth on reconnect)
- Offline for teacher console (F6) — teachers are always on wifi

## Assignee

Unassigned; Iman must approve sync design before build; Dina for item-version-freeze architecture.
