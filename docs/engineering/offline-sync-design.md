# Offline Sync Design — RDY-075 Phase 1A

> **Status**: Phase 1A design locked. Phase 1B (service worker + Vue
> store + API endpoint) + Phase 1C (Grafana dashboard + 24h stale
> warning) land in separate claims.

- **Task**: RDY-075 F4 offline PWA with sync conflict resolution
- **Panel-review source**: Round 2.F4 + Round 4 (Dina + Oren + Iman)

## 1. Why this document ships before the build

Dina's rule from Round 4: **design before code for any feature that
writes events from multiple clients**. Offline sync has four genuine
correctness hazards:

1. **Version drift** — item edited between serve and submit → wrong grade
2. **Duplicate ingest** — service worker retries → double-mastery-gain
3. **Out-of-order batches** — 3 sessions sync at once → corrupted aggregate
4. **Cache stampede** — 500 students reconnect after outage → API storms

This doc pre-registers the mitigation for each, and the Phase 1A code
lands the pieces that belong server-side. The SPA pieces (service
worker + IndexedDB queue + Vue "last sync" banner) come in Phase 1B.

## 2. Version drift — item-version freeze

When the student receives an item, the client stores a snapshot:

```
ItemVersionFreeze {
  ItemId
  ItemVersion
  QuestionText
  CorrectAnswerCanonical
  Difficulty + Discrimination
  CasSnapshotHash
  FrozenAtUtc
}
```

The offline answer event travels with this freeze. Server-side ingest
grades the answer **against the freeze**, not against the current item
version. This closes the "admin edits item between serve and submit"
hole:

| Scenario | Without freeze | With freeze |
|---|---|---|
| Admin raises difficulty v1→v2 | IRT parameter drift on old attempts | Attempt graded + IRT-weighted as v1 |
| Admin edits correct answer | Student's correct answer marked wrong | Frozen canonical answer used |
| CAS oracle re-publishes rule | Old attempts suddenly invalid | Hash-pinned snapshot still accepted |

## 3. Duplicate ingest — idempotency keys

Every offline answer event carries an `IdempotencyKey` generated on
the client as:

```
IdempotencyKey = base64(
  hash(studentAnonId || sessionId || itemId || answeredAtUtc.ticks)
)
```

Server-side ingest maintains a ledger of seen keys (Phase 1B: Marten
document with TTL). On arrival:

- `HasSeen(key)` → drop silently, return `Duplicate`
- Item exists + key unseen → grade + project + `MarkSeen`
- Item unknown server-side → dead-letter, return `Reject` for
  admin investigation

The ledger retention is 60 days — comfortably longer than the worst-
case service-worker cache lifetime + any reasonable reconnect delay.
After 60 days the key is pruned; a re-submission beyond that window
is indistinguishable from a fresh answer and grades again — this is
the intended behaviour (student who comes back 3 months later shouldn't
be punished for missing cache state).

## 4. Out-of-order batches — additive events

We never merge fields. Every ingest produces an immutable event on
the student's stream:

```
AnswerSubmittedV1 {
  StudentAnonId
  SessionId
  ItemId
  ItemVersion
  IsCorrect            // graded against the freeze
  TimeSpent
  AnsweredAtUtc
  IngestedAtUtc
}
```

The mastery projection folds events in **`AnsweredAtUtc` order**, not
ingest order. A batch of 3 sessions that arrives today can include
yesterday's + last-Tuesday's + today's events in any order; the
projection sorts them before replaying. Since every event is
additive (no "replace field X with Y" semantics) there are no merge
conflicts by construction.

## 5. Cache stampede — bounded pre-pack + backoff

Pre-pack hard cap: **30 MB / session**, exactly as the task spec
says. The service worker (Phase 1B) will:

- Download in chunks with exponential backoff
- Respect HTTP `429 Too Many Requests` + `Retry-After`
- Stop mid-pre-pack if the cap is reached; deliver a partial pack
  rather than failing the whole download

Phase 1C Grafana dashboard monitors:
- Pre-pack bytes served p50 / p95 / p99
- Pre-pack completion rate
- Reconnect-time ingest volume

## 6. Empty-cache UX (Rami's challenge)

When pre-pack runs out and the device is still offline, the UX shows
a **calm message**, not a spinner or error:

> "More items will be here next time you connect."

No countdown. No "TAP TO RECONNECT NOW". No red dot on the app icon.
The Phase 1B Vue component MUST render this copy verbatim from i18n;
the shipgate scanner RDY-077 patterns already cover the "Hurry" /
"Time's up" / "Don't waste" framings that would otherwise leak in.

## 7. Data handling

- Offline answer queue: IndexedDB in the browser, cleared on
  successful sync
- Explanation text / tutor conversation turns: **NEVER** pre-packed
  (ADR-0003 session scope; these come down on-demand + are
  session-scoped anyway)
- Idempotency ledger: server-side Marten document, 60-day TTL

## 8. Phase 1A ships (this commit)

- `src/actors/Cena.Actors/Sessions/ItemVersionFreeze.cs` — record + grading method
- `src/actors/Cena.Actors/Sessions/OfflineAnswerEvent` — queued event type
- `src/actors/Cena.Actors/Sessions/IOfflineSyncLedger` + `InMemoryOfflineSyncLedger`
- `src/actors/Cena.Actors/Sessions/OfflineSyncIngest.Decide()` — pure decision
- 17 tests covering freeze grading, ledger dedup, ingest decision matrix
- This design doc

## 9. Phase 1B scope (next claim)

- `src/student/full-version/src/serviceworker/session-prepack.ts`
- `src/student/full-version/src/stores/offlineAnswerQueue.ts`
- `src/api/Cena.Student.Api/Features/Sessions/SyncOnReconnect.cs` —
  wires `OfflineSyncIngest.Decide()` to a Marten-backed ledger
- Vue "last sync" banner + stale warning + empty-cache copy

## 10. Phase 1C scope (after 1B)

- Grafana `ops/grafana/offline-sync-dashboard.json`
- 24h-stale warning logic
- Reconnect-time rate limit + backoff UX polish

## References

- ADR-0003: `docs/adr/0003-misconception-session-scope.md`
- Panel review: `docs/research/cena-panel-review-user-personas-2026-04-17.md`
