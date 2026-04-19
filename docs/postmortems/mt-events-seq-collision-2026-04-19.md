# Postmortem — `mt_events_seq_id` collision under emulator stress + Docker FS hang

- **Date**: 2026-04-19 ~15:55 UTC
- **Severity**: SEV-2 (dev-env only; production traffic unaffected)
- **Duration**: ~10 minutes (detection → Docker Desktop crash → restart)
- **Observer**: Shaker (user)

## What happened

During an extended dev session, the Cena emulator had been running at
`EMU_SPEED=25` (the repo default) against a fresh cold-boot stack.
Emulator simulates 50 students × 3600 simulation days at 25× real time —
several hundred concept-attempts per wall-clock second.

Two symptoms appeared in rapid succession:

1. **Bus rejection flood** on `cena.mastery.attempt`:
   ```
   NATS message rejected ... ConceptAttempt.QuestionType 'multiple_choice' is not recognised
   ```
   This was high-volume but benign — every emulator attempt was being
   discarded before reaching the actor. No data written at all on that
   subject.

2. **Marten event append collisions** on actor-host (separate flow):
   ```
   Npgsql.PostgresException (23505): duplicate key value violates unique
     constraint "pkey_mt_events_seq_id"
     SchemaName: cena  TableName: mt_events
   Marten.IDocumentStore: Discarding message Marten.Internal.UpdateBatch
     after 3 attempts
   ```
   Concurrent `LightweightSession` event appends collided on
   `mt_events.seq_id`. Marten's retry policy gave up after 3 attempts.

3. Sustained retry churn from (2) produced enough disk + syscall
   pressure that Docker Desktop's internal FS service hung:
   ```
   An unexpected error occurred: service fs failed: injecting event
   blocked for 60s
   ```
   Required a Docker Desktop quit + relaunch.

## Root causes (two, independent)

### Root cause A — validator drift (shipped fix)

`BusMessageValidator.ValidQuestionTypes` declared `"multiplechoice"` (no
underscore). Every emitter in the codebase — emulator, SimulationEventSeeder,
ReferenceCalibratedGenerationService — emits `"multiple_choice"`
(underscore). The validator had drifted out of sync with its callers
some time in the past and nobody noticed until the emulator ran long
enough to generate the volume of rejections that caught the user's eye.

**Fix**: widen `ValidQuestionTypes` to accept the three common naming
forms (snake_case, kebab-case, concatenated) for every question type.
Shipped in the same commit as this postmortem.

### Root cause B — Marten sequence collision (latent, not fixed)

Three independent `DocumentStore` instances (admin-api, student-api,
actor-host) each maintain their own event-sequence allocator against
the same Postgres `mt_events` table. Under sustained concurrent append
load, these allocators fight. Whether this is a HiLo cache race, a
Quick-append retry hazard, or a Marten v8 regression is still unknown.

**Mitigation**: lowered default `EMU_SPEED` from 25 → 5 in
`docker-compose.app.yml`. The bug is not fixed; it's parked behind a
gentler default.

**Tracked**: RDY-081 for root-cause investigation.

### Contributing factor — Docker Desktop VM

macOS Docker Desktop's virtiofs layer has a documented sensitivity to
high syscall volume. The Marten retry storm (3 attempts × every failed
batch) compounded quickly into a VM-level hang. Docker logs the
`injecting event blocked for 60s` message when the host→VM event
channel backlogs past 60 seconds.

## Timeline

| Time | Event |
|---|---|
| 15:06 | First rejection + first duplicate-key exception (concurrent; both from the emulator flood) |
| 15:56 | User screenshots the rejection logs and asks for investigation |
| ~15:57 | Second screenshot shows the `pkey_mt_events` stack + rejection churn |
| ~15:58 | Docker Desktop FS service hang; user sees the unexpected-error dialog |
| 16:00 | User quits Docker Desktop |
| 16:05 | User reports Docker Desktop back up |
| 16:35 | Validator fix shipped, EMU_SPEED lowered to 5, 1× emulator smoke-tested clean (1550 events acknowledged, zero rejections) |

## Impact

- **User-facing**: none (dev env only, no external users affected)
- **Data**: no corrupted state — failed appends were rejected before
  any partial write
- **Platform**: ~10 min loss of dev productivity while Docker restarted

## What worked

- The stack-trace-preserving log change from commit `c8d8e6a` was
  critical: it surfaced the Postgres constraint name instead of the
  earlier generic "Index was outside the bounds of the array" that
  would have made (B) invisible.
- Fail-fast Marten retry (3 attempts, then discard) prevented infinite
  write amplification against Postgres.
- The validator fix (root cause A) is a one-HashSet change — small
  blast radius.

## What didn't

- The validator drift went undetected for an unknown period because
  there's no test that round-trips an emulator `BusConceptAttempt`
  through `BusMessageValidator.Validate`. Added to RDY-081's
  regression test list.
- Running the emulator at 25× was a repo default, not an explicit
  load-test flag. A contributor could trigger (B) just by running
  `docker compose up`. Default is now 5×; 25× must be opted into.

## Action items

1. ✅ Widen `BusMessageValidator.ValidQuestionTypes` to accept all
   common naming forms (shipped).
2. ✅ Lower default `EMU_SPEED` 25 → 5 (shipped).
3. ✅ File RDY-081 for Marten sequence-collision investigation.
4. ☐ Add a round-trip test: emulator-generated `BusConceptAttempt`
   payload → validator → actor dispatch, asserting acceptance. Catches
   future validator drift. (Tracked in RDY-081.)
5. ☐ Investigate the Marten v8 sequence path (RDY-081 Scope §2).
