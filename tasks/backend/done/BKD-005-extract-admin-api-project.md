# BKD-005: Extract Admin API into Separate Project

**Priority:** P2 — architectural cleanup, no behavior change
**Blocked by:** None (can be done anytime)
**Estimated effort:** 1 day
**Trigger:** Before adding a second API surface (student-facing API)

---

## Problem

The admin HTTP API (31 files: endpoints, DTOs, services) lives inside `Cena.Actors`, which is a domain/actor library. This violates separation of concerns — HTTP routing, request/response DTOs, and Firebase Admin SDK calls don't belong alongside Proto.Actor grains and event sourcing.

## Current Structure (Wrong)

```
src/actors/
  Cena.Actors/
    Api/Admin/              ← 31 files: endpoints, DTOs, services (HTTP concerns)
    Infrastructure/Auth/    ← JWT validation, CORS (HTTP middleware)
    Infrastructure/Firebase/ ← Firebase Admin SDK (external service)
    Infrastructure/Documents/ ← AdminUser, CenaRoleDefinition (admin-only Marten docs)
    Infrastructure/Seed/    ← RoleSeedData (admin-only)
    Graph/                  ← domain (correct)
    Mastery/                ← domain (correct)
    Services/               ← domain (correct)
    Students/               ← domain (correct)
    Sessions/               ← domain (correct)
```

## Target Structure

```
src/
  actors/
    Cena.Actors/            ← pure domain: actors, events, services, mastery, focus
    Cena.Actors.Host/       ← host: Program.cs, DI wiring
    Cena.Actors.Tests/
  admin/
    Cena.Admin.Api/         ← NEW: admin HTTP endpoints, DTOs, services
    Cena.Admin.Api.Tests/   ← NEW: admin API integration tests
  shared/
    Cena.Infrastructure/    ← NEW: auth middleware, Firebase SDK, Marten docs, Redis
```

## Steps

### 1. Create `Cena.Infrastructure` class library
- Move: `Infrastructure/Auth/`, `Infrastructure/Firebase/`, `Infrastructure/Documents/`, `Infrastructure/Seed/`
- This project references: Marten, Firebase Admin SDK, StackExchange.Redis
- Both `Cena.Actors` and `Cena.Admin.Api` reference this

### 2. Create `Cena.Admin.Api` class library
- Move: `Api/Admin/` (all 31 files)
- References: `Cena.Infrastructure`, `Cena.Actors` (for querying actor state)
- Contains: endpoint registration, DTOs, admin business logic

### 3. Update `Cena.Actors.Host`
- Add project references to `Cena.Admin.Api` and `Cena.Infrastructure`
- Update `Program.cs`: call `app.MapAdminEndpoints()` from the new project
- Auth middleware registration stays in Host (it's pipeline config)

### 4. Update `Cena.Actors`
- Remove `Api/Admin/` folder entirely
- Remove `Infrastructure/Auth/`, `Infrastructure/Firebase/`, `Infrastructure/Documents/`, `Infrastructure/Seed/`
- Add reference to `Cena.Infrastructure` if actors need shared types

### 5. Fix namespaces
- `Cena.Actors.Api.Admin.*` → `Cena.Admin.Api.*`
- `Cena.Actors.Infrastructure.*` → `Cena.Infrastructure.*`
- Update all `using` statements

## Acceptance

- [ ] `Cena.Actors` contains zero HTTP/API concerns
- [ ] `dotnet build` succeeds for all projects
- [ ] All existing tests pass
- [ ] Admin API endpoints still work at same URLs
- [ ] No circular project references
