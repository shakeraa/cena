# ADR-0043: Bagrut Reference-Only Enforcement

Date: 2026-04-20
Status: Accepted
Relates to:
- [ADR-0002](0002-sympy-correctness-oracle.md) (SymPy CAS correctness oracle)
- [ADR-0032](0032-cas-gated-question-ingestion.md) (CAS-gated question ingestion — the write-side half of this invariant)
- [ADR-0040](0040-accommodation-scope-and-bagrut-parity.md) (Bagrut parity accommodations)
- CLAUDE.md non-negotiable "Bagrut reference-only" (2026-04-15 decision)

## Context

On 2026-04-15 the team locked the posture that Ministry-published Bagrut exam items are **reference material only**. Student-facing exam items are AI-authored recreations, CAS-gated per ADR-0002 and produced through the `BagrutRecreationAggregate` review workflow (see `src/actors/Cena.Actors/Content/BagrutRecreation.cs`).

ADR-0032 covers the **write side**: items land in the question bank only after running the CAS gate. It does not, however, guarantee the **delivery side**: a code path that reads a raw Ministry item and serves it to a student bypasses the invariant just as thoroughly as an un-verified AI-generated one. Pre-release review prr-008 surfaced this as a P0 ship-blocker.

## Decision

Enforce the Bagrut-reference-only invariant with three defensive layers.

### §1 — Compile-time phantom-type

`src/actors/Cena.Actors/Content/Provenance.cs` introduces:

- `enum ProvenanceKind { AiRecreated = 1, TeacherAuthoredOriginal = 2, MinistryBagrut = 3 }`
- `readonly record struct Provenance(ProvenanceKind Kind, DateTimeOffset Recorded, string Source)`
- `readonly record struct Deliverable<T>(T Value, Provenance Provenance)` with a factory `Deliverable<T>.From(value, provenance)` that throws `InvalidOperationException` when `provenance.Kind == MinistryBagrut`.

Call sites that type their outbound payloads as `Deliverable<T>` get the invariant enforced by construction.

### §2 — Runtime delivery-gate chokepoint

`src/actors/Cena.Actors/Assessment/IItemDeliveryGate.cs` defines the `IItemDeliveryGate` interface and its default `ItemDeliveryGate` implementation. Every student-delivery seam (exam simulation today; diagnostic, practice session, and tutor playback under migration in Sprint-2) MUST call `AssertDeliverable(provenance, itemId, sessionId, tenantId, actorId)` immediately before serialising an item onto the outbound wire.

On a `MinistryBagrut` attempt the gate:

1. Emits a structured error log under pinned `EventId(8008, "BagrutReferenceOnlyViolation")` — SIEM pipelines key on the event id, not log-line text.
2. Throws `InvalidOperationException`. This is a **bug**, not a graceful fallback: a Ministry item reaching a student is a data-leak incident, and the caller must propagate the failure up to the API boundary as 5xx.

The gate deliberately never logs the raw item body — only identifiers (`itemId`, `sessionId`, `tenantId`, `actorId`) and the `Provenance.Source` metadata. Ministry text is what we are refusing to emit; it never crosses the gate.

`ExamSimulationDelivery.AssertDeliverable` in `ExamSimulationMode.cs` is the ergonomic wrapper that the exam-simulation HTTP endpoint calls; analogous wrappers will land for each new delivery surface.

### §3 — Architecture test

`src/actors/Cena.Actors.Tests/Architecture/BagrutRecreationOnlyTest.cs` scans student-facing DTO surfaces (`src/api/Cena.Student.Api.Host/**`, `src/api/Cena.Api.Contracts/{Sessions,Challenges,Tutor,Me,Hub}/**`) and fails the build if a field name leaks the Ministry reference link (`MinistryBagrut*`, `BagrutReferenceId`, `MinistryExamId`, `MinistryCode`, `MoedSlug`). Admin surfaces are explicitly out of scope — the Ministry reference is load-bearing for the expert-review queue.

### §4 — Event-stream audit

`ExamSimulationItemDelivered_V1` (new, `src/actors/Cena.Actors/Events/ExamSimulationEvents.cs`) records the `ProvenanceKind` of each delivered item. Blocked deliveries never reach this event — by construction, the gate throws before the event-store write — so the event stream is auditable for Bagrut-reference-only compliance.

## Consequences

- **Positive**: three independent enforcement layers (compile-time phantom-type, runtime gate, arch test) make it hard for a new delivery path to accidentally bypass the invariant. Ministry items can still be ingested freely as inspiration for the recreation pipeline (see ADR-0032); the gate only fires at the student-delivery seam.
- **Negative**: full provenance threading through every existing item-read path is Sprint-2 work. Today the exam-simulation seam is fully wired; diagnostic + practice-session seams will migrate next. Until then, those surfaces rely on the arch test (defense-in-depth) and on the same single-seam default-fail-closed pattern being adopted as each seam is touched.
- **Operational**: any `BagrutReferenceOnlyViolation` SIEM event should be treated as a P0 data-leak incident. The runbook is `docs/ops/alerts/bagrut-reference-only.md` (to be authored in Sprint-2 alongside the remaining seam migrations).

## Enforcement summary

| Layer | Artefact | Catches |
|-------|----------|---------|
| Compile-time | `Deliverable<T>.From` | Construction of a Ministry-provenanced payload at any call site that types through the wrapper |
| Runtime | `ItemDeliveryGate.AssertDeliverable` | Last-moment-before-serialisation check; SIEM-logged + thrown on violation |
| Architecture | `BagrutRecreationOnlyTest` | Static scan of student-facing DTO field names |
| Audit | `ExamSimulationItemDelivered_V1` | Every successful delivery carries its `ProvenanceKind` in the event store |

See prr-008 for the originating pre-release review task and the negative-integration tests in `src/actors/Cena.Actors.Tests/Assessment/BagrutRecreationOnlyDeliveryTests.cs`.

## Sibling change 2026-04-20: V1→V2 readiness field migration (prr-013)

On 2026-04-20 the prr-013 backend retirement landed alongside this ADR. The change shipped `ExamSimulationSubmitted_V2` (in the same `ExamSimulationEvents.cs` file this ADR governs) and retired the `ReadinessLowerBound` / `ReadinessUpperBound` scalars from the on-stream shape:

- `ExamSimulationSubmitted_V1` is marked `[Obsolete]` (error: false) and retained in source for historical Marten replay only. A 2026-04-20 grep confirmed no handler, projection, or emitter in the codebase reads or writes V1 — the record existed as a declaration but no production code path produced instances. The `[Obsolete]` tag makes any NEW emitter surface as a compiler warning.
- `ExamSimulationSubmitted_V2` is the only event shape emitters may construct going forward. It drops the readiness bounds entirely. Per ADR-0003 + RDY-080, readiness is **session-scoped**: if the session actor computes a `SessionRiskAssessment`, it lives on the session actor only and never crosses the persistence boundary.
- No upcaster was required: V1 has no in-code aggregate/projection consumers, so there is no V1→V2 replay path that must be kept compatible. If a future aggregate consumer ever needs to fold historical V1 events, it should apply them as if the readiness scalars were absent (treat `ReadinessLowerBound` / `ReadinessUpperBound` as `null` / ignored) — the point of the migration is that those numbers do not survive the session.
- The two architecture tests (`NoAtRiskPersistenceTest`, `NoThetaInOutboundDtoTest`) continue to legacy-allowlist the V1 readiness fields so historical replay does not trip the ban. V2 is NOT allowlisted — any regression that puts a readiness-shaped field on V2 fails the build. The allowlist posture flipped from "pending retirement" to "V1 is legacy-only; V2 is the authoritative clean replacement."

This sibling change is intentionally narrow: the provenance-gate contract that ADR-0043 introduces (compile-time phantom type + runtime gate + arch test + audit event) is unchanged. V2's event shape still carries everything the delivery-audit path needs (student, simulation, attempt counts, score, time, visibility warnings, submitted-at) — dropping the readiness bounds only removes a field that persistent downstream consumers were never allowed to read.
