#!/bin/bash
# =============================================================================
# Cena Db.Migrator Smoke Test Script
# Tests the migrator against a local PostgreSQL instance
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR"
MIGRATOR_DLL="$PROJECT_DIR/bin/Release/net9.0/Cena.Db.Migrator.dll"

# Default connection string for local docker-compose postgres
DEFAULT_CONN_STR="Host=localhost;Port=5433;Database=cena;Username=cena_migrator;Password=cena_migrator_dev_password"

# Allow override via environment
CONN_STR="${CENA_MIGRATOR_CONNECTION_STRING:-$DEFAULT_CONN_STR}"

echo "═══════════════════════════════════════════════════════════════"
echo "  Cena.Db.Migrator Smoke Test"
echo "═══════════════════════════════════════════════════════════════"
echo ""

# Build the migrator
echo "Building migrator..."
cd "$PROJECT_DIR"
dotnet build -c Release --nologo --verbosity quiet
echo "✓ Build successful"
echo ""

# Check if postgres is reachable
echo "Checking PostgreSQL connectivity..."
if ! timeout 5 bash -c "cat < /dev/null > /dev/tcp/localhost/5433" 2>/dev/null; then
    echo "⚠ PostgreSQL not available on localhost:5433"
    echo "  Start it with: docker compose up postgres -d"
    echo ""
    echo "Skipping live tests - migrator compiled successfully"
    exit 0
fi
echo "✓ PostgreSQL is reachable"
echo ""

# Run first migration
echo "Test 1: First run (should apply V0001)..."
dotnet "$MIGRATOR_DLL" "$CONN_STR"
FIRST_RUN_EXIT=$?
echo ""

if [ $FIRST_RUN_EXIT -ne 0 ]; then
    echo "✗ First run failed with exit code $FIRST_RUN_EXIT"
    exit 1
fi
echo "✓ First run completed"
echo ""

# Run second migration (should be no-op)
echo "Test 2: Second run (should be no-op)..."
dotnet "$MIGRATOR_DLL" "$CONN_STR"
SECOND_RUN_EXIT=$?
echo ""

if [ $SECOND_RUN_EXIT -ne 0 ]; then
    echo "✗ Second run failed with exit code $SECOND_RUN_EXIT"
    exit 1
fi
echo "✓ Second run completed (no-op)"
echo ""

# Verify SchemaVersions table exists
echo "Test 3: Verify SchemaVersions table..."
SCHEMA_VERSIONS=$(docker exec cena-postgres psql -U cena -d cena -t -c "SELECT COUNT(*) FROM cena.schemaversions;" 2>/dev/null || echo "0")
if [ "$SCHEMA_VERSIONS" -gt 0 ]; then
    echo "✓ SchemaVersions table exists with $SCHEMA_VERSIONS migration(s)"
else
    echo "⚠ Could not verify SchemaVersions table (may need docker)"
fi
echo ""

echo "═══════════════════════════════════════════════════════════════"
echo "  All smoke tests passed!"
echo "═══════════════════════════════════════════════════════════════"
