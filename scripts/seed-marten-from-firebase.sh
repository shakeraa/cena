#!/bin/bash
# =============================================================================
# Cena dev-stack — bridge Firebase seed -> Marten state
#
# The Firebase emulator seed (docker/firebase-emulator/seed-dev-users.sh)
# creates auth users with custom claims, but it does NOT bootstrap Marten
# state (AdminUser doc, StudentProfileSnapshot). Without Marten state,
# /api/me/* returns 401/404 even with a valid Firebase idToken.
#
# This script bridges the gap by signing in each seeded student/parent/teacher
# user via the emulator's signInWithPassword, then POSTing to
# /api/auth/on-first-sign-in (gated by CENA_E2E_TRUSTED_REGISTRATION=true on
# dev student-api). The endpoint creates the AdminUser doc + appends
# StudentOnboardedV1 to the per-uid stream — same idempotent path the
# register flow uses.
#
# Run after the dev stack is up:
#   docker compose ... up -d
#   docker exec cena-firebase-emulator /seed/seed-dev-users.sh
#   ./scripts/seed-marten-from-firebase.sh
#
# Re-runs are no-ops (the on-first-sign-in service is idempotent on uid).
# =============================================================================

set -euo pipefail

EMU="${FIREBASE_EMU_HOST:-localhost:9099}"
API="${STUDENT_API:-http://localhost:5050}"
TENANT="${SEED_TENANT:-cena}"
SCHOOL="${SEED_SCHOOL:-cena-platform}"

bootstrap_user() {
  local email="$1"
  local password="$2"
  local display="$3"
  local role="${4:-student}"

  local signin
  signin=$(curl -fsS -X POST \
    "http://${EMU}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key" \
    -H "Content-Type: application/json" \
    -d "{\"email\":\"${email}\",\"password\":\"${password}\",\"returnSecureToken\":true}" \
    2>&1) || {
    echo "  SKIP ${email}: signInWithPassword failed (user not seeded? expected for parents/teachers if seeder skipped them)"
    return 0
  }
  local id_token
  id_token=$(echo "${signin}" | python3 -c 'import sys,json; print(json.load(sys.stdin)["idToken"])')

  # On-first-sign-in is the canonical path: sets claims, creates AdminUser,
  # appends StudentOnboardedV1. Idempotent on uid — re-runs are no-ops.
  local resp_status
  resp_status=$(curl -sS -o /tmp/seed-marten-resp.json -w "%{http_code}" \
    -X POST "${API}/api/auth/on-first-sign-in" \
    -H "Content-Type: application/json" \
    -H "Authorization: Bearer ${id_token}" \
    -d "{\"tenantId\":\"${TENANT}\",\"schoolId\":\"${SCHOOL}\",\"displayName\":\"${display}\"}" \
    || echo "ERROR")

  if [ "${resp_status}" = "200" ]; then
    local newly
    newly=$(python3 -c 'import sys,json; print(json.load(open("/tmp/seed-marten-resp.json"))["wasNewlyOnboarded"])' 2>/dev/null || echo "?")
    echo "  OK   ${email} (newly=${newly})"
  else
    echo "  FAIL ${email}: HTTP ${resp_status} body=$(cat /tmp/seed-marten-resp.json)"
    return 1
  fi
}

echo "Bridging Firebase seed -> Marten state at ${API}"
echo "tenant=${TENANT} school=${SCHOOL}"
echo "─────────────────────────────────────────────────"

# Students: real on-first-sign-in path. Match the seed script.
bootstrap_user "student1@cena.local" "DevStudent123!" "Lior Mizrahi"     "student"
bootstrap_user "student2@cena.local" "DevStudent123!" "Amina Khoury"     "student"
bootstrap_user "student3@cena.local" "DevStudent123!" "Noa Peretz"       "student"
bootstrap_user "student4@cena.local" "DevStudent123!" "Karim Abu-Assaf"  "student"
bootstrap_user "student5@cena.local" "DevStudent123!" "Maya Ben-David"   "student"
# Parent: useful for A-04 + parent-dashboard specs. on-first-sign-in's
# trusted-registration body declares role=student internally; that's a
# limitation of the trusted path. Parents won't bootstrap cleanly until
# the endpoint accepts a role parameter, but their Firebase claims are
# already set so most A-04 paths still work.

echo "─────────────────────────────────────────────────"
echo "Done. Re-running this script is safe (idempotent on uid)."
