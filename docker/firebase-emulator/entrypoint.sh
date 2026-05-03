#!/usr/bin/env bash
# =============================================================================
# Cena Platform — Firebase Auth Emulator Entrypoint
#
# Replaces the previous bare `CMD ["firebase", "emulators:start", ...]` so
# the container survives Docker Desktop restarts without dropping the dev
# user roster. Two-step flow:
#
#   1. If /data has a previous export (auth_export/accounts.json), pass
#      --import=/data to firebase emulators:start. Users come back as-is.
#
#   2. If /data is empty (first boot, or wiped), let the emulator start
#      empty and call /seed/seed-dev-users.sh once the emulator is
#      responsive. The seed is idempotent — re-running it would error on
#      duplicate emails, so we gate it on absence-of-prior-export.
#
# Both paths set --export-on-exit=/data so that any subsequent
# `docker stop`/`docker compose down` writes the current user state to
# the bind-mounted volume. Next start re-imports it via step 1.
#
# /data is expected to be a docker named volume (cena_firebase_users)
# bound in docker-compose.app.yml so state survives container recreation.
#
# Why this matters: the Firebase Auth emulator is in-memory by default,
# and the seed script was wired as a bind-mount + manual `docker exec`
# call — every Docker Desktop restart broke login until someone
# remembered to re-run it. This entrypoint closes that gap.
# =============================================================================
set -euo pipefail

# Volume mountpoint. firebase emulators:export rmdirs its target before
# writing — and you can't rmdir a docker volume mountpoint from inside
# the container, so the *export path* must be a SUBDIRECTORY of /data
# (not /data itself). Otherwise: EBUSY: resource busy or locked, rmdir.
DATA_VOLUME="${EMU_DATA_VOLUME:-/data}"
DATA_DIR="${DATA_VOLUME}/firebase-export"
SEED_SCRIPT="${SEED_SCRIPT:-/seed/seed-dev-users.sh}"
EMU_HOST="${EMU_HOST:-localhost:9099}"

mkdir -p "${DATA_VOLUME}"

IMPORT_FLAG=()
SEED_NEEDED="0"

# auth_export/accounts.json is what firebase emulator writes on
# --export-on-exit. Its presence is the signal that we have prior state
# to import; its absence is the signal that this is a first boot or a
# wiped volume.
if [ -f "${DATA_DIR}/auth_export/accounts.json" ]; then
  echo "[entrypoint] found prior auth export at ${DATA_DIR}/auth_export — importing"
  IMPORT_FLAG=(--import="${DATA_DIR}")
else
  echo "[entrypoint] no prior auth export — will seed dev users after startup"
  SEED_NEEDED="1"
fi

# Start the emulator. & + wait so signals can propagate to the parent
# shell cleanly. We don't use --export-on-exit (flaky under signal-driven
# shutdown in containers) and we don't try to export on SIGTERM (the
# emulator hub starts shutting down before our trap-spawned export call
# can complete the round trip — see 2026-05-01 incident notes).
# Instead we export EAGERLY: right after the seed completes. Dev user
# state is static (defined in seed-dev-users.sh), so the export captured
# immediately after seed is identical to the export you'd get on
# shutdown. Subsequent restarts pick up that export via --import.
firebase emulators:start \
  --project "${PROJECT_ID:-cena-platform}" \
  --only auth \
  "${IMPORT_FLAG[@]}" &
EMU_PID=$!

trap 'kill -TERM "${EMU_PID}" 2>/dev/null; wait "${EMU_PID}"' TERM INT

# If we need to seed, wait for the emulator to come up, run the seed,
# then immediately export so the next container start can re-import
# without re-running the seed. Subshell so the foreground wait below is
# unaffected.
if [ "${SEED_NEEDED}" = "1" ]; then
  (
    for _ in $(seq 1 60); do
      if curl -fsS "http://${EMU_HOST}/" >/dev/null 2>&1; then
        echo "[entrypoint] emulator up — running seed"
        if [ -x "${SEED_SCRIPT}" ]; then
          "${SEED_SCRIPT}" || echo "[entrypoint] seed script exited non-zero (continuing — emulator stays up)"
        else
          echo "[entrypoint] seed script not found at ${SEED_SCRIPT} — skipping"
        fi

        # Eager export: capture the freshly-seeded state to /data so
        # the next container start can --import instead of re-seeding.
        # Done while emulator is fully healthy → reliable, vs. the
        # signal-driven shutdown path which raced the hub teardown.
        echo "[entrypoint] exporting freshly-seeded state to ${DATA_DIR}"
        if firebase emulators:export \
             --project "${PROJECT_ID:-cena-platform}" \
             --force \
             "${DATA_DIR}" 2>&1 | sed 's/^/[entrypoint:export] /'; then
          echo "[entrypoint] export complete — restarts will re-import from ${DATA_DIR}"
        else
          echo "[entrypoint] export FAILED — restarts will re-seed from scratch"
        fi
        exit 0
      fi
      sleep 2
    done
    echo "[entrypoint] emulator did not become responsive within 120s — skipping seed"
  ) &
fi

wait "${EMU_PID}"
