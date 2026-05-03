# Cena.Db.Migrator

DbUp-based database migration console application for the Cena platform.

## Quick Start

```bash
# Build
dotnet build -c Release

# Run migrations (connection string via argument)
dotnet run --project src/api/Cena.Db.Migrator \
  "Host=localhost;Port=5433;Database=cena;Username=cena_migrator;Password=cena_dev_password"

# Or via environment variable
export CENA_MIGRATOR_CONNECTION_STRING="Host=localhost;Port=5433;Database=cena;Username=cena_migrator;Password=..."
dotnet run --project src/api/Cena.Db.Migrator
```

## Connection String Sources (in priority order)

1. **Command line argument**: First argument to the executable
2. **CENA_MIGRATOR_CONNECTION_STRING**: Environment variable
3. **ConnectionStrings__cena_migrator**: ASP.NET Core style environment variable

## How It Works

- Discovers SQL scripts from `db/migrations/V*.sql` embedded in the assembly
- Scripts are executed in alphabetical order (V0001, V0002, etc.)
- DbUp tracks applied scripts in the `schemaversions` table
- Scripts are executed exactly once per database
- Second run with same scripts is a no-op

## Docker

```bash
# Build image
docker build -f src/api/Cena.Db.Migrator/Dockerfile -t cena-db-migrator:latest .

# Run migrations
docker run --rm \
  -e CENA_MIGRATOR_CONNECTION_STRING="Host=host.docker.internal;Port=5433;..." \
  cena-db-migrator:latest
```

## Smoke Test

```bash
# Ensure postgres is running
docker compose up postgres -d

# Run smoke tests
./src/api/Cena.Db.Migrator/smoke-test.sh
```

## Exit Codes

- `0`: Success (migrations applied or no-op)
- `1`: Error (connection failed, migration failed, etc.)

## Migration Naming

See `db/migrations/README.md` for the naming convention:
- `V{4-digit version}__{snake_case_description}.sql`
- Example: `V0001__pgvector_extension_and_embeddings.sql`
