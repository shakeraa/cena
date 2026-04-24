#!/bin/bash
# =============================================================================
# REV-002: NATS Authentication & Subject Isolation Tests
# Verifies that each service user can only pub/sub on permitted subjects.
#
# Prerequisites: NATS running with auth (docker compose up nats)
#                nats CLI installed locally
# =============================================================================

set -euo pipefail

NATS_SERVER="${NATS_SERVER:-nats://localhost:4222}"
ACTOR_PASS="${NATS_ACTOR_PASSWORD:-dev_actor_pass}"
API_PASS="${NATS_API_PASSWORD:-dev_api_pass}"
EMU_PASS="${NATS_EMU_PASSWORD:-dev_emu_pass}"
SETUP_PASS="${NATS_SETUP_PASSWORD:-dev_setup_pass}"

PASSED=0
FAILED=0

pass() { echo "  PASS: $1"; PASSED=$((PASSED + 1)); }
fail() { echo "  FAIL: $1"; FAILED=$((FAILED + 1)); }

test_publish_denied() {
    local user="$1" pass="$2" subject="$3" label="$4"
    if nats pub "$subject" "test" --user "$user" --password "$pass" --server "$NATS_SERVER" 2>&1 | grep -q "Permissions Violation"; then
        pass "$label"
    else
        fail "$label"
    fi
}

test_publish_allowed() {
    local user="$1" pass="$2" subject="$3" label="$4"
    if nats pub "$subject" "test" --user "$user" --password "$pass" --server "$NATS_SERVER" 2>&1 | grep -q "Permissions Violation"; then
        fail "$label"
    else
        pass "$label"
    fi
}

echo "============================================="
echo "  REV-002: NATS Auth & Subject ACL Tests"
echo "============================================="
echo ""

# -- Test 1: Unauthenticated connection rejected --
echo "[1] Unauthenticated access"
if nats pub test.subject "hello" --server "$NATS_SERVER" 2>&1 | grep -qi "authorization\|unauthorized\|error"; then
    pass "Unauthenticated connection is rejected"
else
    fail "Unauthenticated connection should be rejected"
fi
echo ""

# -- Test 2: actor-host permissions --
echo "[2] actor-host user permissions"
test_publish_allowed  "actor-host" "$ACTOR_PASS" "cena.events.test"   "actor-host can publish to cena.events.>"
test_publish_allowed  "actor-host" "$ACTOR_PASS" "cena.system.test"   "actor-host can publish to cena.system.>"
test_publish_denied   "actor-host" "$ACTOR_PASS" "cena.session.start" "actor-host cannot publish to cena.session.>"
echo ""

# -- Test 3: admin-api permissions (subscribe-only) --
echo "[3] admin-api user permissions"
test_publish_denied "admin-api" "$API_PASS" "cena.events.test"   "admin-api cannot publish to cena.events.>"
test_publish_denied "admin-api" "$API_PASS" "cena.session.start" "admin-api cannot publish to cena.session.>"
test_publish_denied "admin-api" "$API_PASS" "cena.system.test"   "admin-api cannot publish to cena.system.>"
echo ""

# -- Test 4: emulator permissions --
echo "[4] emulator user permissions"
test_publish_allowed "emulator" "$EMU_PASS" "cena.session.start"       "emulator can publish to cena.session.>"
test_publish_allowed "emulator" "$EMU_PASS" "cena.mastery.update"      "emulator can publish to cena.mastery.>"
test_publish_allowed "emulator" "$EMU_PASS" "cena.events.focus.test"   "emulator can publish to cena.events.focus.>"
test_publish_denied  "emulator" "$EMU_PASS" "cena.system.health.test"  "emulator cannot publish to cena.system.>"
test_publish_denied  "emulator" "$EMU_PASS" "cena.events.mastery.test" "emulator cannot publish to cena.events.mastery.>"
echo ""

# -- Test 5: Wrong password rejected --
echo "[5] Invalid credentials"
if nats pub test.subject "hello" --user "actor-host" --password "wrong_password" --server "$NATS_SERVER" 2>&1 | grep -qi "authorization\|unauthorized\|error"; then
    pass "Wrong password is rejected"
else
    fail "Wrong password should be rejected"
fi
echo ""

# -- Summary --
echo "============================================="
echo "  Results: $PASSED passed, $FAILED failed"
echo "============================================="

[ "$FAILED" -eq 0 ] || exit 1
