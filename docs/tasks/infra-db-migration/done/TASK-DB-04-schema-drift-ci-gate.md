# TASK-DB-04: CI Schema-Drift Gate

**Priority**: MEDIUM
**Effort**: 1-2 days
**Depends on**: DB-02
**Track**: B
**Status**: Not Started

---

## You Are

A CI engineer who believes schema changes are the hardest thing to catch post-merge. You build a gate that runs on every PR, spins up a throwaway Postgres, applies the target branch's migrations, and then asserts the Marten configuration matches. If a developer added a new document type or changed a projection without an accompanying migration, the build goes red.

## The Problem

Even with DB-02 and DB-03 in place, a developer can still:
- Add `opts.Schema.For<NewDoc>()` in code without a migration
- Change a projection in a way that requires an ALTER
- Rename an indexed field

…and the issue only shows up at **deploy time**, when the host refuses to start. That's too late — it breaks the production deploy pipeline and blocks unrelated changes. The drift detection needs to move into the PR build.

## Your Task

Add a GitHub Actions (or whichever CI the project uses) job that:

1. **Spins up a fresh Postgres 16 + pgvector container** as a service on the CI runner.
2. **Runs `Cena.Db.Migrator apply`** against it. This applies all committed migrations + Marten config from the target branch.
3. **Runs `Cena.Db.Migrator validate`** (the mode added in DB-02). Exit code 0 means the Marten config matches the applied schema; non-zero means there's a drift (a code change that needs a migration) — fail the build.
4. **Reports the diff** into the PR check output so the author sees exactly which table/column is out of sync.

### CI job shape (reference, not prescriptive)

```yaml
jobs:
  schema-drift:
    runs-on: ubuntu-latest
    services:
      postgres:
        image: pgvector/pgvector:pg16
        env:
          POSTGRES_USER: cena
          POSTGRES_PASSWORD: cena
          POSTGRES_DB: cena
        ports: ['5432:5432']
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
      - run: dotnet restore
      - run: dotnet build src/api/Cena.Db.Migrator/Cena.Db.Migrator.csproj -c Release --no-restore
      - name: Apply migrations
        run: dotnet run --project src/api/Cena.Db.Migrator -c Release -- apply
        env:
          ConnectionStrings__Cena: "Host=localhost;Port=5432;Database=cena;Username=cena;Password=cena"
      - name: Validate Marten config matches DB
        run: dotnet run --project src/api/Cena.Db.Migrator -c Release -- validate
        env:
          ConnectionStrings__Cena: "Host=localhost;Port=5432;Database=cena;Username=cena;Password=cena"
```

### Auxiliary gates

While you're in the CI file, add two sibling sanity checks:

1. **Append-only migration enforcement** — diff the PR against `main` and fail if any file matching `db/migrations/V*.sql` was modified (only new files allowed).
2. **Monotonic numbering** — fail if the highest migration version in the PR branch is not exactly `max(main) + 1`.

These cost ~10 lines of bash each and prevent the two most common migration-workflow mistakes.

## Files You Must Touch

- `.github/workflows/ci.yml` (or the existing primary CI file — verify the actual path)
- Possibly `scripts/ci/check-migrations-append-only.sh` if you factor the bash out

## Files You Must Read First

- Existing CI workflow files to understand the current structure
- [TASK-DB-02-migrator-project.md](TASK-DB-02-migrator-project.md) — to know the migrator's CLI contract

## Acceptance Criteria

- [ ] A `schema-drift` CI job runs on every PR.
- [ ] The job fails when a Marten config change has no matching migration.
- [ ] The failure output shows a readable diff of what's missing.
- [ ] Append-only enforcement blocks PRs that modify existing `V*.sql` files.
- [ ] Monotonic numbering enforcement blocks non-sequential version numbers.
- [ ] A deliberately broken PR (add an unreferenced column) is reproduced in a draft PR and shown in the main PR description.
- [ ] Green builds unblock merges; red builds clearly communicate the fix.

## Out of Scope

- Blocking deploys on schema drift in a non-CI environment — DB-03 already handles that at host start.
- Automatically generating missing migration files from a diff — future work.
- Running the drift gate against staging's actual DB — future work, gated on DB-07 observability.
