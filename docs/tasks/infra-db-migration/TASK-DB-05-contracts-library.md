# TASK-DB-05: Extract `Cena.Api.Contracts` Shared Library

**Priority**: HIGH — prerequisite for the host split
**Effort**: 2-3 days
**Depends on**: DB-02 (ordering, not technical)
**Track**: C
**Status**: Not Started

---

## You Are

A senior architect who knows that splitting a host without first extracting shared contracts creates two slowly-diverging copies of the same DTOs. You do the boring work of carving out a library first, so the split is a rename, not a rewrite.

## The Problem

Today, both admin and student HTTP surfaces live in the same process ([src/api/Cena.Api.Host/Program.cs](../../../src/api/Cena.Api.Host/Program.cs) maps both), and DTOs are colocated with endpoints. When DB-06 splits the hosts, the natural mistake is to copy DTOs into both projects, leading to:

- Student and admin drifting on event envelopes.
- Two upcaster lists that have to be kept in sync manually.
- Two sets of hub contracts that subtly disagree.
- No single project to point the migrator at.

Extracting a `Cena.Api.Contracts` library up front forces the split to share one source of truth.

## Your Task

### 1. Create the library

```
src/api/Cena.Api.Contracts/
├── Cena.Api.Contracts.csproj
├── Dtos/                      (request/response records used by endpoints)
│   ├── Sessions/
│   ├── Content/
│   ├── Analytics/
│   ├── Admin/
│   └── Common/
├── Hub/
│   ├── HubContracts.cs        (move from Hubs/HubContracts.cs)
│   ├── BusEnvelope.cs
│   └── HubEventTypes.cs
└── Events/
    └── (shared event type references where needed)
```

Target framework: `.NET 9`. No ASP.NET references — this is a pure contracts library, usable by both hosts and (eventually) by code-gen for the student web TS types.

### 2. Move, don't copy

Identify every DTO, hub contract, and envelope in:

- [src/api/Cena.Api.Host/Endpoints/](../../../src/api/Cena.Api.Host/Endpoints/) — session, content, analytics DTOs
- [src/api/Cena.Api.Host/Hubs/](../../../src/api/Cena.Api.Host/Hubs/) — `HubContracts.cs`, `BusEnvelope`, envelope wrapper types
- [src/api/Cena.Admin.Api/](../../../src/api/Cena.Admin.Api/) — admin-specific DTOs that are still part of the public HTTP surface

Move them into `Cena.Api.Contracts`. Keep the namespaces coherent (e.g. `Cena.Api.Contracts.Sessions`, `Cena.Api.Contracts.Hub`). Update all consumer references. The existing host projects reference the new contracts lib.

### 3. Do NOT move

- Endpoint definitions (`*Endpoints.cs`) — those stay with their host.
- Actor event types (`Cena.Actors.Events`) — those stay in the actor layer; Contracts references them where necessary.
- Infrastructure types (auth guards, rate limit policies) — those stay in `Cena.Api.Infrastructure` (may not exist yet; create a sibling project if needed, but prefer keeping it simple for now).
- Marten schema config — stays in `Cena.Actors/Configuration`.

### 4. TypeScript generation stub

The student web ([docs/student/15-backend-integration.md](../../student/15-backend-integration.md)) wants typed hub events and DTOs. Add a placeholder script under `scripts/codegen/generate-ts-contracts.sh` that reads `Cena.Api.Contracts` assembly metadata and produces TS type files. For this PR, stub it as "prints a TODO" — actual generation is a follow-up. The point is to reserve the slot.

### 5. Solution wiring

- Add to `Cena.sln`.
- Both `Cena.Api.Host` and `Cena.Admin.Api` reference `Cena.Api.Contracts`.
- `Cena.Db.Migrator` may reference it if helpful (unlikely — it mostly cares about Marten config, not DTOs).
- `Cena.Actors` does **not** reference `Cena.Api.Contracts` — contracts depends on actors, not the reverse.

### 6. Namespace migration

Run a repo-wide grep and update `using` statements. Keep the PR focused: no renames beyond the namespace move, no behavior changes.

## Files You Must Create

- `src/api/Cena.Api.Contracts/Cena.Api.Contracts.csproj`
- Files moved from hosts (path translation documented in PR description)
- `scripts/codegen/generate-ts-contracts.sh` (stub)

## Files You Must Modify

- `Cena.sln`
- `src/api/Cena.Api.Host/Cena.Api.Host.csproj` — add ProjectReference
- `src/api/Cena.Admin.Api/Cena.Admin.Api.csproj` — add ProjectReference
- Every C# file that imported the moved types

## Files You Must Read First

- [src/api/Cena.Api.Host/Program.cs](../../../src/api/Cena.Api.Host/Program.cs) — sees the full contract surface
- `src/api/Cena.Api.Host/Hubs/` — understand hub contract shapes before moving them
- Every `*Endpoints.cs` file — to inventory DTOs

## Acceptance Criteria

- [ ] `Cena.Api.Contracts` project exists and compiles.
- [ ] All DTOs, hub contracts, and envelope types live in `Cena.Api.Contracts` and nowhere else.
- [ ] Both `Cena.Api.Host` and `Cena.Admin.Api` reference the new project.
- [ ] `Cena.Actors` does **not** reference `Cena.Api.Contracts`.
- [ ] All endpoints still compile and work (run the existing integration tests).
- [ ] Namespaces are coherent and `using` statements have been updated repo-wide.
- [ ] Solution builds end-to-end with no warnings introduced by the move.
- [ ] TS codegen stub exists under `scripts/codegen/`.
- [ ] PR description lists the file mapping (old path → new path) for reviewer sanity.

## Out of Scope

- Actually splitting the host processes (DB-06).
- Generating TypeScript types for real (follow-up).
- Renaming any DTOs or changing their shape.
- Moving endpoint definitions.
