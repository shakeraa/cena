#!/usr/bin/env bash
# =============================================================================
# Cena Platform — Firebase Emulator Dev-User Seed (RDY-056 §2.4)
#
# Creates a fixed roster of local accounts in the Firebase Auth emulator
# with custom claims (role, tenant_id, school_id, grade) matching Cena's
# CenaClaimsTransformer expectations.
#
# Run after the emulator is healthy:
#   docker exec cena-firebase-emulator /seed/seed-dev-users.sh
#
# Emits a user list to stdout that callers can tee into a credentials file.
# =============================================================================
set -euo pipefail

EMU_HOST="${EMU_HOST:-localhost:9099}"
PROJECT_ID="${PROJECT_ID:-cena-platform}"
SCHOOL_ID="${SCHOOL_ID:-dev-school}"

# Emulator accepts any Bearer token as admin; "owner" is convention.
AUTH_HEADER='Authorization: Bearer owner'

CREATE_URL="http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/projects/${PROJECT_ID}/accounts"
CLAIMS_URL="http://${EMU_HOST}/emulator/v1/projects/${PROJECT_ID}/accounts"

# Wait for emulator
echo "waiting for Firebase emulator at ${EMU_HOST}..."
for i in $(seq 1 30); do
  if curl -fsS "http://${EMU_HOST}/" >/dev/null 2>&1; then
    echo "emulator up"
    break
  fi
  sleep 2
done

create_user() {
  local email="$1"
  local password="$2"
  local display_name="$3"
  local claims_json="$4"

  local create_payload
  create_payload=$(cat <<JSON
{"email":"${email}","password":"${password}","displayName":"${display_name}","returnSecureToken":true}
JSON
)

  local resp
  resp=$(curl -fsS -X POST "${CREATE_URL}" \
    -H "${AUTH_HEADER}" \
    -H "Content-Type: application/json" \
    -d "${create_payload}" || echo '{}')

  local uid
  uid=$(echo "${resp}" | sed -n 's/.*"localId":"\([^"]*\)".*/\1/p')

  if [ -z "${uid}" ]; then
    echo "FAIL creating ${email}: ${resp}" >&2
    return 1
  fi

  # Set custom claims — emulator-specific endpoint accepts customAttributes
  # as a JSON string on the update call.
  local claims_payload
  claims_payload=$(cat <<JSON
{"localId":"${uid}","customAttributes":${claims_json}}
JSON
)

  curl -fsS -X POST "http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/projects/${PROJECT_ID}/accounts:update" \
    -H "${AUTH_HEADER}" \
    -H "Content-Type: application/json" \
    -d "${claims_payload}" >/dev/null

  echo "${email} | ${password} | ${uid} | ${display_name}"
}

echo
echo "────────────────────────────────────────────────────────────────"
echo " Seeding Cena dev users (project=${PROJECT_ID}, school=${SCHOOL_ID})"
echo "────────────────────────────────────────────────────────────────"
echo "email | password | uid | displayName"
echo "────────────────────────────────────────────────────────────────"

# ResourceOwnershipGuard.cs expects UPPER_SNAKE_CASE role names:
# SUPER_ADMIN, ADMIN, MODERATOR, TEACHER, STUDENT, PARENT.

# Primary super-admin — matches the Marten seed row (AdminUser sa-001) so
# this account owns the admin backend on login.
create_user "shaker.abuayoub@gmail.com" "ShakerMain2026!" "Shaker Abu Ayoub" \
  "\"{\\\"role\\\":\\\"SUPER_ADMIN\\\",\\\"tenant_id\\\":\\\"cena\\\",\\\"school_id\\\":\\\"${SCHOOL_ID}\\\"}\""

create_user "admin@cena.local"        "DevAdmin123!"   "Dev Admin"       \
  "\"{\\\"role\\\":\\\"SUPER_ADMIN\\\",\\\"tenant_id\\\":\\\"cena\\\",\\\"school_id\\\":\\\"${SCHOOL_ID}\\\"}\""

create_user "curriculum@cena.local"   "DevCur123!"     "Curriculum Admin" \
  "\"{\\\"role\\\":\\\"ADMIN\\\",\\\"tenant_id\\\":\\\"cena\\\",\\\"school_id\\\":\\\"${SCHOOL_ID}\\\"}\""

create_user "teacher1@cena.local"     "DevTeacher123!" "Ms. Rivka Cohen"  \
  "\"{\\\"role\\\":\\\"TEACHER\\\",\\\"tenant_id\\\":\\\"cena\\\",\\\"school_id\\\":\\\"${SCHOOL_ID}\\\",\\\"classroom_ids\\\":[\\\"class-5u-A\\\"]}\""

create_user "teacher2@cena.local"     "DevTeacher123!" "Mr. Yusuf Haddad" \
  "\"{\\\"role\\\":\\\"TEACHER\\\",\\\"tenant_id\\\":\\\"cena\\\",\\\"school_id\\\":\\\"${SCHOOL_ID}\\\",\\\"classroom_ids\\\":[\\\"class-4u-B\\\"]}\""

create_user "student1@cena.local"     "DevStudent123!" "Lior Mizrahi"     \
  "\"{\\\"role\\\":\\\"STUDENT\\\",\\\"tenant_id\\\":\\\"cena\\\",\\\"school_id\\\":\\\"${SCHOOL_ID}\\\",\\\"grade\\\":\\\"11\\\",\\\"track\\\":\\\"5U\\\",\\\"lang\\\":\\\"he\\\"}\""

create_user "student2@cena.local"     "DevStudent123!" "Amina Khoury"     \
  "\"{\\\"role\\\":\\\"STUDENT\\\",\\\"tenant_id\\\":\\\"cena\\\",\\\"school_id\\\":\\\"${SCHOOL_ID}\\\",\\\"grade\\\":\\\"11\\\",\\\"track\\\":\\\"5U\\\",\\\"lang\\\":\\\"ar\\\"}\""

create_user "student3@cena.local"     "DevStudent123!" "Noa Peretz"       \
  "\"{\\\"role\\\":\\\"STUDENT\\\",\\\"tenant_id\\\":\\\"cena\\\",\\\"school_id\\\":\\\"${SCHOOL_ID}\\\",\\\"grade\\\":\\\"10\\\",\\\"track\\\":\\\"4U\\\",\\\"lang\\\":\\\"he\\\"}\""

create_user "student4@cena.local"     "DevStudent123!" "Karim Abu-Assaf"  \
  "\"{\\\"role\\\":\\\"STUDENT\\\",\\\"tenant_id\\\":\\\"cena\\\",\\\"school_id\\\":\\\"${SCHOOL_ID}\\\",\\\"grade\\\":\\\"10\\\",\\\"track\\\":\\\"4U\\\",\\\"lang\\\":\\\"ar\\\"}\""

create_user "student5@cena.local"     "DevStudent123!" "Maya Ben-David"   \
  "\"{\\\"role\\\":\\\"STUDENT\\\",\\\"tenant_id\\\":\\\"cena\\\",\\\"school_id\\\":\\\"${SCHOOL_ID}\\\",\\\"grade\\\":\\\"12\\\",\\\"track\\\":\\\"5U\\\",\\\"lang\\\":\\\"he\\\"}\""

create_user "parent1@cena.local"      "DevParent123!"  "Sara Mizrahi"     \
  "\"{\\\"role\\\":\\\"PARENT\\\",\\\"tenant_id\\\":\\\"cena\\\",\\\"school_id\\\":\\\"${SCHOOL_ID}\\\",\\\"children\\\":[\\\"student1@cena.local\\\"]}\""

echo "────────────────────────────────────────────────────────────────"
echo "done. Emulator UI: http://localhost:4000 (project ${PROJECT_ID})"
