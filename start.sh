#!/bin/bash
# ═══════════════════════════════════════════════════════════════════════
# Cena Platform — Smart Startup Script
# Usage: ./start.sh [all|infra|actors|llm|mobile|web]
# ═══════════════════════════════════════════════════════════════════════

set -e

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$PROJECT_ROOT"

log() { echo -e "${CYAN}[CENA]${NC} $1"; }
ok()  { echo -e "${GREEN}  ✓${NC} $1"; }
warn(){ echo -e "${YELLOW}  ⚠${NC} $1"; }
err() { echo -e "${RED}  ✗${NC} $1"; }

# ── Prerequisites Check ──
check_prerequisites() {
    log "Checking prerequisites..."

    command -v docker >/dev/null 2>&1 || { err "Docker not installed"; exit 1; }
    ok "Docker"

    command -v dotnet >/dev/null 2>&1 || { err ".NET SDK not installed (need 9.0+)"; exit 1; }
    ok ".NET SDK $(dotnet --version)"

    command -v python3 >/dev/null 2>&1 || { err "Python 3 not installed"; exit 1; }
    ok "Python $(python3 --version 2>&1 | cut -d' ' -f2)"

    if command -v flutter >/dev/null 2>&1; then
        ok "Flutter $(flutter --version 2>&1 | head -1 | cut -d' ' -f2)"
    else
        warn "Flutter not installed (mobile dev only)"
    fi

    if command -v node >/dev/null 2>&1; then
        ok "Node.js $(node --version)"
    else
        warn "Node.js not installed (web dev only)"
    fi

    echo ""
}

# ── Infrastructure (Docker Compose) ──
start_infra() {
    log "Starting infrastructure (PostgreSQL, Redis, NATS, Neo4j, DynamoDB)..."

    docker compose up -d postgres redis nats neo4j dynamodb

    log "Waiting for services to be healthy..."
    local retries=30
    while [ $retries -gt 0 ]; do
        local healthy=$(docker compose ps --format json 2>/dev/null | grep -c '"healthy"' || echo 0)
        if [ "$healthy" -ge 4 ]; then
            break
        fi
        sleep 2
        retries=$((retries - 1))
    done

    # Run NATS stream setup
    docker compose up nats-setup

    ok "PostgreSQL:  localhost:5432 (cena/cena_dev_password)"
    ok "Redis:       localhost:6379"
    ok "NATS:        localhost:4222 (monitoring: localhost:8222)"
    ok "Neo4j:       localhost:7474 (neo4j/cena_dev_password)"
    ok "DynamoDB:    localhost:8000"
    echo ""
}

# ── Actor Cluster (.NET) ──
start_actors() {
    log "Starting Actor Cluster (.NET 9)..."
    cd "$PROJECT_ROOT/src/actors/Cena.Actors.Host"

    export ConnectionStrings__PostgreSQL="Host=localhost;Database=cena;Username=cena;Password=cena_dev_password"
    export ConnectionStrings__Redis="localhost:6379"
    export ConnectionStrings__NATS="nats://localhost:4222"
    export ConnectionStrings__Neo4j="bolt://localhost:7687"
    export ConnectionStrings__DynamoDB="http://localhost:8000"
    export ASPNETCORE_ENVIRONMENT="Development"
    export PROTO_REMOTE_PORT="5001"

    dotnet run &
    ACTOR_PID=$!

    # Wait for health check
    sleep 5
    if curl -sf http://localhost:5000/health/ready >/dev/null 2>&1; then
        ok "Actor Cluster: http://localhost:5000 (PID: $ACTOR_PID)"
    else
        warn "Actor Cluster starting... (check http://localhost:5000/health/ready)"
    fi

    cd "$PROJECT_ROOT"
    echo ""
}

# ── LLM ACL (Python FastAPI) ──
start_llm() {
    log "Starting LLM ACL (Python FastAPI)..."
    cd "$PROJECT_ROOT/src/llm-acl"

    if [ ! -d ".venv" ]; then
        python3 -m venv .venv
        source .venv/bin/activate
        pip install -r requirements.txt -q
    else
        source .venv/bin/activate
    fi

    export REDIS_URL="redis://localhost:6379"
    export NATS_URL="nats://localhost:4222"
    export ANTHROPIC_API_KEY="${ANTHROPIC_API_KEY:-not-set}"
    export MOONSHOT_API_KEY="${MOONSHOT_API_KEY:-not-set}"

    uvicorn app.main:app --host 0.0.0.0 --port 8001 --reload &
    LLM_PID=$!

    sleep 3
    if curl -sf http://localhost:8001/health >/dev/null 2>&1; then
        ok "LLM ACL: http://localhost:8001 (PID: $LLM_PID)"
    else
        warn "LLM ACL starting... (check http://localhost:8001/health)"
    fi

    cd "$PROJECT_ROOT"
    echo ""
}

# ── Mobile (Flutter) ──
start_mobile() {
    log "Starting Mobile App (Flutter)..."
    cd "$PROJECT_ROOT/src/mobile"

    if [ ! -f "pubspec.lock" ]; then
        flutter pub get
    fi

    flutter run &
    ok "Flutter: running"

    cd "$PROJECT_ROOT"
    echo ""
}

# ── Web (React) ──
start_web() {
    log "Starting Web App (React)..."
    cd "$PROJECT_ROOT/src/web"

    if [ ! -d "node_modules" ]; then
        npm install
    fi

    npm run dev &
    ok "React Web: http://localhost:5173"

    cd "$PROJECT_ROOT"
    echo ""
}

# ── Main ──
echo ""
echo -e "${CYAN}╔═══════════════════════════════════════╗${NC}"
echo -e "${CYAN}║     CENA ADAPTIVE LEARNING PLATFORM   ║${NC}"
echo -e "${CYAN}║     Local Development Environment     ║${NC}"
echo -e "${CYAN}╚═══════════════════════════════════════╝${NC}"
echo ""

check_prerequisites

MODE="${1:-all}"

case $MODE in
    infra)
        start_infra
        ;;
    actors)
        start_infra
        start_actors
        ;;
    llm)
        start_infra
        start_llm
        ;;
    mobile)
        start_mobile
        ;;
    web)
        start_web
        ;;
    all)
        start_infra
        start_actors
        start_llm
        log "Infrastructure + Actors + LLM ACL running."
        log "Start mobile: cd src/mobile && flutter run"
        log "Start web:    cd src/web && npm run dev"
        ;;
    stop)
        log "Stopping all services..."
        docker compose down
        pkill -f "Cena.Actors.Host" 2>/dev/null || true
        pkill -f "uvicorn" 2>/dev/null || true
        ok "All services stopped"
        ;;
    *)
        echo "Usage: ./start.sh [all|infra|actors|llm|mobile|web|stop]"
        exit 1
        ;;
esac

echo ""
log "Done. Happy coding!"
