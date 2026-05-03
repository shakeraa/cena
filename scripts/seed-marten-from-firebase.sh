#!/bin/bash
# =============================================================================
# Cena dev-stack — bridge Firebase seed -> Marten state
#
# The Firebase emulator seed (docker/firebase-emulator/seed-dev-users.sh)
# creates auth users with custom claims, but it does NOT bootstrap Marten
# state (AdminUser doc, StudentProfileSnapshot). Without Marten state,
# /api/me/* returns 401/404 even with a valid Firebase idToken.
#
# This script bridges the gap two ways depending on role:
#
#   * Students (5)        -> POST /api/auth/on-first-sign-in
#                            (gated by CENA_E2E_TRUSTED_REGISTRATION=true).
#                            Idempotent on uid; appends StudentOnboardedV1.
#
#   * Non-students (6)    -> direct UPSERT into cena.mt_doc_adminuser keyed
#                            on the current Firebase uid. on-first-sign-in
#                            hard-codes role=student so admin/curriculum/
#                            teacher/parent rows can't go through it.
#                            Includes shaker so live login resolves to a
#                            uid-keyed SUPER_ADMIN AdminUser (the static
#                            sa-001 fixture stays in place beside it).
#
# After upserting, sweeps stale AdminUser rows for canonical dev emails
# whose id is NOT in the allowlist of uids actually bootstrapped this run.
# Those rows reference Firebase uids from previous emulator sessions that
# have been wiped. Sweep is skipped if fewer than 6 live uids were captured
# (defensive: never delete dev users if Firebase is empty/down). The
# sa-001 static fixture is preserved by id.
#
# Run after the dev stack is up:
#   docker compose ... up -d
#   docker exec cena-firebase-emulator /seed/seed-dev-users.sh
#   ./scripts/seed-marten-from-firebase.sh
#
# Re-runs are safe — both bridges are idempotent and the sweep only acts
# on stale rows.
# =============================================================================

set -euo pipefail

EMU="${FIREBASE_EMU_HOST:-localhost:9099}"
API="${STUDENT_API:-http://localhost:5050}"
TENANT="${SEED_TENANT:-cena}"
SCHOOL="${SEED_SCHOOL:-cena-platform}"

PG_CONTAINER="${PG_CONTAINER:-cena-postgres}"

# Allowlist of "live" Firebase uids (the ones we just bootstrapped this run).
# Filled by bootstrap_user / bootstrap_user_direct_marten so the final sweep
# can delete rows for canonical dev emails whose id is NOT in this list,
# regardless of mt_last_modified timing. This avoids the previous bug where
# idempotent on-first-sign-in didn't refresh mt_last_modified, so a re-run
# after the 5-minute window swept the still-current student rows.
LIVE_UIDS=()

# Fetch the current Firebase uid for an email by signing in.
# Echoes the uid to stdout, returns 1 if the user is not in the emulator.
firebase_uid_for() {
  local email="$1"
  local password="$2"
  local resp
  resp=$(curl -fsS -X POST \
    "http://${EMU}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key" \
    -H "Content-Type: application/json" \
    -d "{\"email\":\"${email}\",\"password\":\"${password}\",\"returnSecureToken\":true}" \
    2>/dev/null) || return 1
  echo "${resp}" | python3 -c 'import sys,json; print(json.load(sys.stdin)["localId"])'
}

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
  local id_token uid
  id_token=$(echo "${signin}" | python3 -c 'import sys,json; print(json.load(sys.stdin)["idToken"])')
  uid=$(echo "${signin}" | python3 -c 'import sys,json; print(json.load(sys.stdin)["localId"])')
  LIVE_UIDS+=("${uid}")

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

# Direct-Marten upsert for non-student roles. The on-first-sign-in endpoint
# hard-codes role=student, so admin/curriculum/teacher/parent rows can't go
# through it. We fetch the user's current Firebase uid, then UPSERT a row
# in cena.mt_doc_adminuser keyed on that uid. Idempotent — re-runs of the
# script after an emulator wipe pick up the new uids and either insert
# (new uid) or update-in-place (uid unchanged).
bootstrap_user_direct_marten() {
  local email="$1"
  local password="$2"
  local display="$3"
  local role="$4"     # SUPER_ADMIN | ADMIN | TEACHER | PARENT | MODERATOR
  local locale="${5:-en}"

  local uid
  uid=$(firebase_uid_for "${email}" "${password}") || {
    echo "  SKIP ${email}: not in Firebase emulator"
    return 0
  }
  LIVE_UIDS+=("${uid}")

  # Use psql heredoc on the postgres container. Marten owns mt_last_modified
  # and mt_version via column defaults, so we only set id/data/mt_dotnet_type.
  docker exec -i "${PG_CONTAINER}" psql -U cena -d cena -v ON_ERROR_STOP=1 -q >/dev/null <<SQL
INSERT INTO cena.mt_doc_adminuser (id, data, mt_dotnet_type) VALUES (
  '${uid}',
  jsonb_build_object(
    'id','${uid}', 'role','${role}',
    'email','${email}', 'fullName','${display}',
    'school','${SCHOOL}', 'locale','${locale}', 'status','Active',
    'grade',NULL, 'avatarUrl',NULL, 'softDeleted',false,
    'suspendedAt',NULL, 'suspensionReason',NULL,
    'createdAt', to_char(now() AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI:SS"Z"'),
    'lastLoginAt', to_char(now() AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI:SS"Z"')),
  'Cena.Infrastructure.Documents.AdminUser')
ON CONFLICT (id) DO UPDATE SET
  data = EXCLUDED.data,
  mt_last_modified = transaction_timestamp(),
  mt_version = md5(random()::text || clock_timestamp()::text)::uuid;
SQL

  if [ $? -eq 0 ]; then
    echo "  OK   ${email} (uid=${uid}, role=${role})"
  else
    echo "  FAIL ${email}: direct-Marten upsert failed"
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

# Non-students: direct Marten upsert keyed on current Firebase uid.
# on-first-sign-in is hard-coded to role=student, so these can't go through
# the API. The static sa-001 fixture row for shaker stays in place
# (UserSeedData.cs); we add a uid-keyed row alongside it so live login
# resolves to a SUPER_ADMIN AdminUser.
bootstrap_user_direct_marten "shaker.abuayoub@gmail.com" "ShakerMain2026!" "Shaker Abu Ayoub" "SUPER_ADMIN" "en"
bootstrap_user_direct_marten "admin@cena.local"          "DevAdmin123!"    "Dev Admin"        "SUPER_ADMIN" "en"
bootstrap_user_direct_marten "curriculum@cena.local"     "DevCur123!"      "Curriculum Admin" "ADMIN"       "en"
bootstrap_user_direct_marten "teacher1@cena.local"       "DevTeacher123!"  "Ms. Rivka Cohen"  "TEACHER"     "he"
bootstrap_user_direct_marten "teacher2@cena.local"       "DevTeacher123!"  "Mr. Yusuf Haddad" "TEACHER"     "ar"
bootstrap_user_direct_marten "parent1@cena.local"        "DevParent123!"   "Sara Mizrahi"     "PARENT"      "he"

# Sweep stale rows: any AdminUser doc whose email matches one of our 11
# canonical dev users but whose id is NOT in the live-uid allowlist
# (LIVE_UIDS, populated by both bootstrap functions above). These rows
# reference Firebase uids from previous emulator sessions that have been
# wiped — they accumulate every time the emulator volatile state is reset
# (Docker restart, compose down -v). Without this sweep, every login query
# returns multiple rows and email-indexed admin pages show ghost users.
#
# The static sa-001 fixture row (UserSeedData.cs) is preserved by id.
# Sweep only runs if at least one user was successfully bootstrapped this
# run; refusing to sweep with an empty allowlist prevents the worst case
# of "Firebase down → no live uids → delete all dev users."
echo "─────────────────────────────────────────────────"
if [ "${#LIVE_UIDS[@]}" -lt 6 ]; then
  echo "Skipping sweep: only ${#LIVE_UIDS[@]} users bootstrapped this run (need ≥6)."
  echo "If the Firebase emulator was empty, run docker exec cena-firebase-emulator /seed/seed-dev-users.sh first."
else
  echo "Sweeping stale AdminUser rows (${#LIVE_UIDS[@]} live uids in allowlist)..."
  # Build SQL ARRAY[...] literal from the bash array. uids are alphanumeric
  # so single-quote wrapping without escaping is safe.
  uids_sql=$(printf "'%s'," "${LIVE_UIDS[@]}")
  uids_sql="${uids_sql%,}"  # strip trailing comma
  docker exec -i "${PG_CONTAINER}" psql -U cena -d cena -v ON_ERROR_STOP=1 -q <<SQL
DELETE FROM cena.mt_doc_adminuser
WHERE data->>'email' IN (
  'shaker.abuayoub@gmail.com','admin@cena.local','curriculum@cena.local',
  'teacher1@cena.local','teacher2@cena.local','parent1@cena.local',
  'student1@cena.local','student2@cena.local','student3@cena.local',
  'student4@cena.local','student5@cena.local')
  AND id NOT IN (${uids_sql})
  AND id <> 'sa-001';
SQL
  echo "Sweep complete (sa-001 fixture preserved)."
fi

echo "─────────────────────────────────────────────────"
echo "Done. Re-running this script is safe (idempotent on uid)."
