#!/usr/bin/env bash
# =============================================================================
# Cena Platform — Development Start Script
#
# Usage:
#   ./scripts/start.sh                  # Start all services
#   ./scripts/start.sh --no-emulator    # Without student emulator
#   ./scripts/start.sh --emulator-only  # Only the emulator (services must be running)
#   ./scripts/start.sh --stop           # Stop all services
#
# Options:
#   --students=N     Number of simulated students (default: 100)
#   --speed=N        Emulator speed multiplier (default: 50)
#   --no-frontend    Skip Vite frontend
#   --no-emulator    Skip student emulator
#   --no-actors      Skip actor host (admin API only)
#   --emulator-only  Only start the emulator
#   --stop           Stop all running services
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m'

# Defaults
STUDENTS=100
SPEED=50
START_FRONTEND=true
START_ACTORS=true
START_EMULATOR=true
EMULATOR_ONLY=false
STOP_ALL=false

# Parse args
for arg in "$@"; do
  case $arg in
    --students=*)  STUDENTS="${arg#*=}" ;;
    --speed=*)     SPEED="${arg#*=}" ;;
    --no-frontend) START_FRONTEND=false ;;
    --no-emulator) START_EMULATOR=false ;;
    --no-actors)   START_ACTORS=false ;;
    --emulator-only) EMULATOR_ONLY=true; START_FRONTEND=false; START_ACTORS=false ;;
    --stop)        STOP_ALL=true ;;
    --help|-h)
      head -20 "$0" | grep "^#" | sed 's/^# //' | sed 's/^#//'
      exit 0
      ;;
  esac
done

stop_services() {
  echo -e "${YELLOW}Stopping services...${NC}"
  kill $(lsof -ti:5000 2>/dev/null) 2>/dev/null && echo "  Stopped Admin API (5000)" || true
  kill $(lsof -ti:5001 2>/dev/null) 2>/dev/null && echo "  Stopped Actor Host (5001)" || true
  kill $(lsof -ti:5174 2>/dev/null) 2>/dev/null && echo "  Stopped Frontend (5174)" || true
  kill $(lsof -ti:5119 2>/dev/null) 2>/dev/null && echo "  Stopped Actor Host alt port" || true
  # Don't kill NATS — it may be shared
  echo -e "${GREEN}All services stopped.${NC}"
}

if $STOP_ALL; then
  stop_services
  exit 0
fi

echo -e "${CYAN}"
echo "  ╔══════════════════════════════════════════════╗"
echo "  ║         Cena Platform — Dev Launcher         ║"
echo "  ╚══════════════════════════════════════════════╝"
echo -e "${NC}"

# ── Prerequisites ──

echo -e "${BLUE}Checking prerequisites...${NC}"

# NATS
if ! lsof -i:4222 >/dev/null 2>&1; then
  if command -v nats-server >/dev/null 2>&1; then
    echo -e "  Starting ${YELLOW}NATS${NC} server..."
    nats-server -p 4222 -m 8222 --jetstream --store_dir /tmp/nats-data > /tmp/nats.log 2>&1 &
    sleep 1
    echo -e "  ${GREEN}NATS${NC} started on port 4222"
  else
    echo -e "  ${RED}NATS not installed.${NC} Run: brew install nats-server"
    exit 1
  fi
else
  echo -e "  ${GREEN}NATS${NC} already running on port 4222"
fi

# PostgreSQL
if ! pg_isready -p 5433 -q 2>/dev/null; then
  echo -e "  ${YELLOW}Warning:${NC} PostgreSQL not detected on port 5433"
fi

# Redis
if ! redis-cli ping >/dev/null 2>&1; then
  echo -e "  ${YELLOW}Warning:${NC} Redis not detected"
fi

# ── Build ──

echo -e "\n${BLUE}Building projects...${NC}"

if $START_ACTORS; then
  echo -e "  Building ${CYAN}Actor Host${NC}..."
  (cd "$PROJECT_ROOT/src/actors/Cena.Actors.Host" && dotnet build --no-restore -q) || {
    echo -e "  ${RED}Actor Host build failed${NC}"; exit 1;
  }
fi

echo -e "  Building ${CYAN}Admin API${NC}..."
(cd "$PROJECT_ROOT/src/api/Cena.Api.Host" && dotnet build --no-restore -q) || {
  echo -e "  ${RED}Admin API build failed${NC}"; exit 1;
}

if $START_EMULATOR || $EMULATOR_ONLY; then
  echo -e "  Building ${CYAN}Emulator${NC}..."
  (cd "$PROJECT_ROOT/src/emulator" && dotnet build --no-restore -q) || {
    echo -e "  ${RED}Emulator build failed${NC}"; exit 1;
  }
fi

echo -e "  ${GREEN}All builds succeeded.${NC}"

# ── Stop existing services ──

stop_services 2>/dev/null

sleep 1

# ── Start Services ──

echo -e "\n${BLUE}Starting services...${NC}"

if ! $EMULATOR_ONLY; then

  # Actor Host
  if $START_ACTORS; then
    echo -e "  Starting ${CYAN}Actor Host${NC} on port 5001..."
    (cd "$PROJECT_ROOT/src/actors/Cena.Actors.Host" && dotnet run --no-build --urls "http://localhost:5001" > /tmp/cena-actors.log 2>&1) &
    ACTOR_PID=$!
    echo "    PID=$ACTOR_PID → /tmp/cena-actors.log"
  fi

  # Admin API
  echo -e "  Starting ${CYAN}Admin API${NC} on port 5000..."
  (cd "$PROJECT_ROOT/src/api/Cena.Api.Host" && dotnet run --no-build > /tmp/cena-api.log 2>&1) &
  API_PID=$!
  echo "    PID=$API_PID → /tmp/cena-api.log"

  # Frontend
  if $START_FRONTEND; then
    echo -e "  Starting ${CYAN}Frontend${NC} on port 5174..."
    (cd "$PROJECT_ROOT/src/admin/full-version" && npx vite --clearScreen false > /tmp/cena-frontend.log 2>&1) &
    FE_PID=$!
    echo "    PID=$FE_PID → /tmp/cena-frontend.log"
  fi

  # Wait for services
  echo -e "\n${BLUE}Waiting for services to be ready...${NC}"
  for i in {1..20}; do
    sleep 1
    API_OK=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/health 2>/dev/null)
    ACTOR_OK="000"
    if $START_ACTORS; then
      ACTOR_OK=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5001/health/live 2>/dev/null)
    fi
    if [[ "$API_OK" == "200" ]] && { ! $START_ACTORS || [[ "$ACTOR_OK" == "200" ]]; }; then
      break
    fi
    echo -n "."
  done
  echo ""

fi

# ── Status ──

echo -e "\n${BLUE}Service Status:${NC}"

check_service() {
  local name=$1 url=$2
  local code=$(curl -s -o /dev/null -w "%{http_code}" "$url" 2>/dev/null)
  if [[ "$code" == "200" ]]; then
    echo -e "  ${GREEN}✓${NC} $name"
  else
    echo -e "  ${RED}✗${NC} $name (HTTP $code)"
  fi
}

check_service "NATS (4222)" "http://localhost:8222/varz"
if ! $EMULATOR_ONLY; then
  check_service "Admin API (5000)" "http://localhost:5000/health"
  if $START_ACTORS; then
    check_service "Actor Host (5001)" "http://localhost:5001/health/live"
  fi
  if $START_FRONTEND; then
    check_service "Frontend (5174)" "http://localhost:5174/"
  fi
fi

# ── Emulator ──

if $START_EMULATOR || $EMULATOR_ONLY; then
  echo -e "\n${BLUE}Starting Student Emulator...${NC}"
  echo -e "  Students: ${YELLOW}${STUDENTS}${NC}, Speed: ${YELLOW}${SPEED}x${NC}"
  echo ""
  (cd "$PROJECT_ROOT/src/emulator" && dotnet run --no-build -- --students=$STUDENTS --speed=$SPEED)
else
  echo -e "\n${GREEN}All services running.${NC} Emulator skipped (use --emulator-only to run later)."
  echo -e "\n${CYAN}URLs:${NC}"
  echo "  Admin Dashboard: http://localhost:5174"
  echo "  Admin API:       http://localhost:5000/health"
  if $START_ACTORS; then
    echo "  Actor Host:      http://localhost:5001/health/live"
  fi
  echo "  NATS Monitor:    http://localhost:8222"
  echo ""
  echo -e "To stop: ${YELLOW}./scripts/start.sh --stop${NC}"
  echo -e "To run emulator: ${YELLOW}./scripts/start.sh --emulator-only --students=100 --speed=50${NC}"

  # Keep script alive so Ctrl+C stops bg services
  wait
fi
