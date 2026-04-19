# RDY-081: Marten event-sequence collision under high-throughput append

- **Status**: Proposed — investigation task
- **Priority**: Medium (production-realistic traffic unaffected; manifests only
  under emulator stress-test load)
- **Source**: Live session 2026-04-19 — emulator at 25× speed (50 students ×
  3600 sim-days) triggered Postgres
  `duplicate key value violates unique constraint "pkey_mt_events_seq_id"`,
  actor-host error-logged, and compounded I/O eventually hung Docker Desktop's
  FS service ("service fs failed: injecting event blocked for 60s")
- **Tier**: 3 (investigation; mitigation already in place)
- **Effort**: 1-3 days (needs Marten v8 internal knowledge)
- **Mitigation shipped**: default `EMU_SPEED=5` (was 25) in
  `docker-compose.app.yml`. Bug still latent under 25× load or future
  pilot-scale burst.

## Problem

Under high-throughput event-append load on the actor-host, Marten v8 emits:

```
Npgsql.PostgresException (23505): duplicate key value violates unique
  constraint "pkey_mt_events_seq_id"
  SchemaName: cena  TableName: mt_events
```

The exception cascades through `Marten.IDocumentStore: Discarding message
Marten.Internal.UpdateBatch after 3 attempts`, meaning the retry policy
gave up after three collisions. Under sustained load the retry churn
produced enough I/O to hang Docker Desktop's FS layer.

`mt_events.seq_id` is allocated by Marten's internal allocator (HiLo or
equivalent) + Postgres SEQUENCE. Duplicate-key means two distinct writes
converged on the same seq_id. Plausible root causes (ranked by our prior):

1. **Multiple DocumentStore instances** (admin-api + student-api +
   actor-host) each with their own HiLo cache on a shared Postgres DB.
   Under contention, two DocumentStores in different processes claim
   overlapping seq ranges.
2. **Concurrent Proto.Actor activations** on actor-host creating
   overlapping `LightweightSession` instances that race on seq allocation.
3. **Retry loop hazard**: on a transient failure, Marten's retry block
   re-submits the insert with the same pre-allocated seq_id; if another
   session has meanwhile committed that seq_id, the retry collides.
4. **Marten v8 known issue**: event-sequence allocator behaviour changed
   between v7 and v8; worth checking JasperFx/marten issue tracker for
   similar reports under 8.x.

## Why it's latent

Production traffic doesn't hit this because:
- One student generates ~20 attempts/hour (realistic), not 200+/second
  as the 25× emulator does.
- Three-process DocumentStore contention only matters when all three
  are writing events at high rate. Student-api + admin-api rarely do;
  actor-host writes most events and does so single-threaded per student.
- Docker FS hang was downstream — emulator retry storms triggered the
  hang, not the seq collision itself.

Default emulator speed dropped from 25× to 5× in the mitigation commit
removes the regular-dev-use trigger. Bug is NOT gone; just parked behind
a less-aggressive default.

## Scope of investigation

### 1. Reproduce on demand

```
EMU_SPEED=25 EMU_STUDENTS=100 docker compose up -d emulator
```

Within 60 seconds the actor-host should emit the exception. Verified
on 2026-04-19.

### 2. Identify the allocator path

- Open Marten v8 source — `StreamIdentity.AsString` + `Quick` append
  mode (what Cena uses per `MartenConfiguration.cs`) → trace `seq_id`
  generation.
- Confirm: does each `LightweightSession()` request a fresh HiLo block?
  Does the block size interact with parallel sessions from the same
  process?
- If HiLo is per-process, three processes × own HiLo × same sequence
  should still be safe because the Postgres SEQUENCE is atomic. Unless
  Marten is computing seq_id client-side without `nextval()`.

### 3. Candidate fixes

Without further knowledge, these are guesses ranked by likely impact:

A. **Disable HiLo for events**, force per-insert `nextval()`. Simplest;
   hot-path cost is one extra round-trip per batch. Document the
   throughput trade-off.

B. **Move admin-api + student-api off the event-append path entirely**
   — they should query, not write events. Actor-host becomes sole
   event-writer. Big architectural change; reduces contention surface.

C. **Upgrade Marten**: if 8.x has a known fix in a newer patch.

D. **Batch retry with seq-id reallocation**: custom retry policy that
   clears the pre-allocated seq_id on `23505` and re-requests a fresh
   block. Hacky but narrow.

### 4. Regression test

Once a fix is identified, add a stress test that spawns N concurrent
`LightweightSession` appends and asserts zero duplicate-key
exceptions. Include in CI (can be slow — nightly job, not per-PR).

## Acceptance criteria

- [ ] Root cause identified + documented (postmortem under
  `docs/postmortems/`)
- [ ] Fix applied (one of A/B/C/D above)
- [ ] 25× emulator run sustains ≥ 5 minutes without a
  duplicate-key exception
- [ ] Default emulator speed restored to 25× in `docker-compose.app.yml`
- [ ] Regression stress test checked in (nightly-only; too slow for
  per-PR)

## Out of scope

- Pilot-data simulation accuracy — this task is about infra resilience,
  not simulation fidelity.
- Multi-tenant / multi-replica production scaling (separate concern; the
  bug exists on a single replica too).

## Links

- Marten configuration: `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs`
- Event append sites: `src/actors/Cena.Actors/Students/StudentActor.cs` (primary)
- Reproducer command: `EMU_SPEED=25 docker compose up -d emulator`
- Postmortem: [docs/postmortems/mt-events-seq-collision-2026-04-19.md](../../docs/postmortems/mt-events-seq-collision-2026-04-19.md)
