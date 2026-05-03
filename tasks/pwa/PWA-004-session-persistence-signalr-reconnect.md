# PWA-004: Session Persistence + SignalR Reconnect

## Goal
Implement the two-layer crash/disconnect recovery system from architecture doc Improvement #46: localStorage draft persistence (client) + full session snapshot on SignalR reconnect (server). A student mid-step who loses network or accidentally closes the browser tab must return to exactly where they left off — same question, same step, same typed-but-not-submitted expression.

## Context
- Architecture doc: `docs/research/cena-question-engine-architecture-2026-04-12.md` §42.4 (Improvement #46)
- PWA approach doc: `docs/research/cena-mobile-pwa-approach.md` §2.2
- Mobile networks in Israel drop frequently (elevator, tunnel, poor coverage in periphery towns)
- Students type complex math expressions that take 30-60 seconds — losing that input is a session-killer
- Proto.Actor rehydrates `StudentSessionActor` from Marten snapshot in <50ms

## Scope of Work

### 1. Client-Side Draft Persistence
Create `src/student/full-version/src/composables/useStepDraft.ts`:

```typescript
interface StepDraft {
  sessionId: string;
  stepNumber: number;
  inputValue: string;        // Raw input (LaTeX or text)
  inputType: 'math' | 'verbal' | 'numeric';
  timestamp: number;         // When saved
  scaffoldingLevel: string;  // Current scaffolding context
}
```

- **Save**: Debounced 2 seconds on every input change → `localStorage` key: `cena:draft:${sessionId}:${stepNumber}`
- **Restore**: On component mount, check for existing draft. If found and `timestamp` is < 1 hour old, restore the input value. If > 1 hour old, discard (session likely expired server-side).
- **Clear**: On successful step submission (`StepVerified` event), delete the draft for that step
- **Cleanup**: On session end, delete all drafts for that session
- **Storage budget**: Max 20 drafts (LRU eviction). Each draft is ~500 bytes. Total: ~10KB — negligible.

### 2. SignalR Reconnect with Session Snapshot
Modify the existing SignalR hub connection (or create if not present):

`src/student/full-version/src/services/signalr.ts`:

- **Reconnect policy**: Automatic reconnect with exponential backoff: 0s, 2s, 5s, 10s, 30s, then every 30s indefinitely
- **On reconnect**: Client sends `RequestSessionSnapshot(sessionId)` to `SessionHub`
- **Server response**: `SessionSnapshot` containing:
  ```typescript
  interface SessionSnapshot {
    sessionId: string;
    currentQuestion: QuestionDto;
    currentStepNumber: number;
    bktSnapshot: Record<string, SkillMasteryDto>;
    scaffoldingLevel: 'full' | 'partial' | 'minimal' | 'exploratory';
    completedSteps: StepResultDto[];
    sessionStartedAt: string;   // ISO 8601
    sessionDuration: number;    // seconds elapsed
  }
  ```
- **Reconciliation**: On receiving snapshot, compare with local state:
  1. If server has a newer completed step than client → client was behind, update UI to show the completed step
  2. If client has a draft for the current step → restore draft input, do NOT overwrite with server state (server doesn't have the draft)
  3. If server session has ended (e.g., teacher ended it remotely) → show "session ended" message, navigate to mastery map
  4. If server session doesn't exist (actor evicted) → show "session expired, starting fresh" and begin new session

### 3. Offline Queue for Step Submissions
Create `src/student/full-version/src/services/offlineQueue.ts`:

When the student submits a step but the network is down:
1. Store the submission in IndexedDB: `{ sessionId, stepNumber, answer, submittedAt }`
2. Show a "Saved — will submit when online" indicator
3. On reconnect, replay queued submissions in order via SignalR
4. Server validates each submission — if the session state has changed (e.g., timeout), the server returns an error and the client shows "Session expired"
5. Max queue depth: 5 submissions. If the student answers 5 questions offline, stop accepting input and show "Please reconnect to continue"

### 4. Connection State UI
Create `src/student/full-version/src/components/ConnectionStatus.vue`:

| State | UI | Color |
|-------|-----|-------|
| Connected | Hidden (no indicator) | — |
| Reconnecting | Subtle top bar: "Reconnecting..." | Yellow |
| Offline | Top bar: "Offline — your work is saved" | Orange |
| Reconnected | Brief flash: "Back online ✓" (2s, then hide) | Green |

- Use `navigator.onLine` + SignalR connection state for combined status
- Accessible: `role="status"`, `aria-live="polite"`
- RTL-aware: bar direction matches document direction

### 5. Tab Visibility Handling
When the browser tab becomes hidden (student switches apps):
- Save current draft immediately (don't wait for debounce)
- Keep SignalR connection alive (do NOT disconnect)
- When tab becomes visible again:
  - If SignalR is still connected → no action needed
  - If SignalR disconnected while hidden → trigger reconnect + snapshot

```typescript
document.addEventListener('visibilitychange', () => {
  if (document.hidden) {
    saveDraftImmediately();
  } else {
    if (!signalr.isConnected) {
      signalr.reconnect();
    }
  }
});
```

## Files to Create/Modify
- `src/student/full-version/src/composables/useStepDraft.ts`
- `src/student/full-version/src/services/signalr.ts` (modify or create)
- `src/student/full-version/src/services/offlineQueue.ts`
- `src/student/full-version/src/components/ConnectionStatus.vue`
- `src/student/full-version/src/stores/sessionStore.ts` (modify — add snapshot reconciliation)

## Non-Negotiables
- **Draft must survive browser crash** — `localStorage` writes must be synchronous and complete before any async operation
- **No data loss on reconnect** — the reconciliation logic must handle every combination of client-ahead, server-ahead, and diverged states
- **Offline queue has a hard limit (5)** — unbounded queuing creates reconciliation nightmares when the student reconnects after an hour
- **Connection status must be accessible** — `aria-live`, screen reader friendly
- **No phantom submissions** — if the session expired server-side, queued submissions must fail gracefully, not create orphaned events

## Acceptance Criteria
- [ ] Student types in step input → closes tab → reopens → typed content is restored
- [ ] Student loses network mid-session → reconnects → session continues from exact state
- [ ] Student submits step while offline → reconnects → step is submitted and verified
- [ ] Student is offline for >1 hour → reconnects → "session expired" shown, new session offered
- [ ] Connection status bar shows correct state for all transitions (connected→offline→reconnecting→reconnected)
- [ ] Tab hidden → draft saved immediately → tab visible → reconnect if needed
- [ ] Max 5 offline submissions → 6th shows "please reconnect" message
- [ ] All UI strings i18n'd (Arabic + Hebrew)
- [ ] Screen reader announces connection state changes

## Testing Requirements
- **Unit**: `useStepDraft.ts` — test save/restore/clear/LRU eviction/expiry
- **Unit**: `offlineQueue.ts` — test enqueue/dequeue/max depth/replay
- **Unit**: Session reconciliation — test all 4 scenarios (client-ahead, server-ahead, diverged, expired)
- **Integration**: Playwright — simulate offline (page.context().setOffline(true)), verify draft persistence, verify reconnect flow
- **Manual**: Real device — toggle airplane mode mid-session on iOS and Android

## DoD
- PR merged to `main`
- Video showing: type → offline → reconnect → content restored (iOS + Android)
- Unit test coverage ≥ 90% for all new composables/services
- No regressions in existing session flow

## Reporting
Complete with: `branch=<worker>/<task-id>-pwa-session-persistence,test_coverage=<n>%,scenarios_tested=<n>`
