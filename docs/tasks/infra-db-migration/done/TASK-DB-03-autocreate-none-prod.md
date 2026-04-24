# TASK-DB-03: Flip Prod Marten from `CreateOrUpdate` to `None`

**Priority**: HIGH
**Effort**: 1-2 days
**Depends on**: DB-02
**Track**: B
**Status**: Not Started

---

## You Are

A platform engineer who knows `AutoCreate.CreateOrUpdate` in production is a loaded footgun. Once the migrator exists, the hosts should refuse to start against a stale schema — loud, fast, and before any traffic hits them.

## The Problem

[MartenConfiguration.cs:49](../../../src/actors/Cena.Actors/Configuration/MartenConfiguration.cs#L49):

```csharp
opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.CreateOrUpdate;
```

This setting is applied unconditionally across all environments. In prod it means:
- Any deploy can ALTER a live table.
- A bad code change can silently rename a column.
- Two hosts racing at boot can both try to ALTER the same table.
- There is no audit trail of what schema change ran when.

Once DB-02 ships and the migrator is the single authoritative schema authority, hosts must be dumb consumers that fail fast if the DB isn't where they expect.

## Your Task

### 1. Introduce per-environment AutoCreate

Modify [MartenConfiguration.cs](../../../src/actors/Cena.Actors/Configuration/MartenConfiguration.cs) so the `AutoCreateSchemaObjects` value is not hardcoded. Read it from config:

```csharp
opts.AutoCreateSchemaObjects = options.AutoCreate switch
{
    "None"           => AutoCreate.None,
    "CreateOnly"     => AutoCreate.CreateOnly,
    "CreateOrUpdate" => AutoCreate.CreateOrUpdate,
    "All"            => AutoCreate.All,
    _ => throw new InvalidOperationException($"Unknown AutoCreate mode: {options.AutoCreate}"),
};
```

Where `options` is a `MartenOptions` record bound from `appsettings.json`:

```json
{
  "Marten": {
    "AutoCreate": "None",
    "SchemaName": "cena"
  }
}
```

Add sensible defaults:

| Environment | AutoCreate |
|---|---|
| `Development` (local) | `CreateOrUpdate` (unchanged — speeds local iteration) |
| `Test` / CI | `None` (forces migrator to run first) |
| `Staging` | `None` |
| `Production` | `None` |

### 2. Fail fast on drift

Hosts must refuse to start if the DB schema does not match their Marten configuration. Add a startup check to both `Cena.Api.Host` (and eventually the split hosts from DB-06):

```csharp
// After store is built, before app.Run()
if (!env.IsDevelopment())
{
    await store.Storage.AssertDatabaseMatchesConfigurationAsync();
}
```

On mismatch this throws a descriptive exception listing which tables/columns are out of sync. The host process exits. Kubernetes will restart it, logs show the diff, the on-call rolls back or runs the migrator.

### 3. Rollout plan (document in PR description)

1. Merge DB-02 (migrator exists and runs on CI).
2. Deploy migrator to staging as a one-shot run via `kubectl run --rm -it`.
3. Deploy this PR to staging. Verify host starts cleanly.
4. Break it on purpose: add an unreferenced column to a document type locally, deploy, confirm the host fails the assert with a useful error.
5. Roll back the deliberate break, redeploy.
6. Repeat against production on a maintenance window.

### 4. Update local developer docs

Add a note to the repo README (or `docs/operations.md` if one exists) explaining that local dev still uses `CreateOrUpdate` for convenience, and that any new schema change must land in `db/migrations/` OR be expressible as a Marten `Schema.For<T>()` call. Nothing else will survive in deployed environments.

## Files You Must Modify

- [src/actors/Cena.Actors/Configuration/MartenConfiguration.cs](../../../src/actors/Cena.Actors/Configuration/MartenConfiguration.cs)
- [src/api/Cena.Api.Host/Program.cs](../../../src/api/Cena.Api.Host/Program.cs)
- [src/actors/Cena.Actors.Host/Program.cs](../../../src/actors/Cena.Actors.Host/Program.cs)
- `appsettings.json` / `appsettings.{Environment}.json` across hosts
- Repo README or `docs/operations.md`

## Files You Must Read First

- [MartenConfiguration.cs](../../../src/actors/Cena.Actors/Configuration/MartenConfiguration.cs) — understand the full `ConfigureCommon` surface
- Both host `Program.cs` files — where Marten is initialized
- `docs/operations.md` if it exists

## Acceptance Criteria

- [ ] `AutoCreate` mode is read from config, not hardcoded.
- [ ] Defaults: Dev = `CreateOrUpdate`, Test/Staging/Prod = `None`.
- [ ] `AssertDatabaseMatchesConfigurationAsync()` runs at startup in non-Dev environments.
- [ ] On schema drift the host process exits with a non-zero code and a diff in the logs.
- [ ] Staging deploy has been verified (migrator → host start clean).
- [ ] Deliberate-break test has been performed and documented in the PR.
- [ ] Local dev is unaffected (still boots with `CreateOrUpdate`).
- [ ] `docs/operations.md` (or README) documents the new posture.
- [ ] PR description includes the prod rollout plan and a rollback recipe.

## Out of Scope

- CI drift detection on PRs — that's DB-04.
- Splitting the hosts — DB-06.
- Removing `PgVectorMigrationService` — follow-up cleanup PR after DB-02 is stable.
