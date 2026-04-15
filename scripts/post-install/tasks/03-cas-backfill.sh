#!/usr/bin/env bash
# 03: CAS binding backfill (ADR-0032 §14)
# Upgrades Unverifiable / missing bindings for math/physics questions that
# were ingested under Shadow or before the gate landed.
set -euo pipefail
source "$(dirname "$0")/../lib/common.sh"

post_install_begin "cas-backfill"

if cena_is_dry_run; then
  post_install_skip "cas-backfill" "DRY_RUN=1"
  exit 0
fi

# Default batch=50; loop until the endpoint reports no more candidates.
total_verified=0
total_failed=0
total_unverifiable=0
while : ; do
  response="$(cena_admin_curl POST "/api/admin/questions/cas-backfill" --data '{"batch":50}')"
  # Expected response shape: { verified: N, failed: M, unverifiable: K, remaining: R }
  verified=$(printf '%s' "$response" | python3 -c 'import json,sys;d=json.load(sys.stdin);print(d.get("verified",0))')
  failed=$(printf '%s' "$response" | python3 -c 'import json,sys;d=json.load(sys.stdin);print(d.get("failed",0))')
  unverifiable=$(printf '%s' "$response" | python3 -c 'import json,sys;d=json.load(sys.stdin);print(d.get("unverifiable",0))')
  remaining=$(printf '%s' "$response" | python3 -c 'import json,sys;d=json.load(sys.stdin);print(d.get("remaining",0))')

  total_verified=$((total_verified + verified))
  total_failed=$((total_failed + failed))
  total_unverifiable=$((total_unverifiable + unverifiable))

  cena_log "backfill pass: +${verified} verified, +${failed} failed, +${unverifiable} unverifiable, remaining=${remaining}"

  if [ "$remaining" -le 0 ]; then break; fi
done

cena_log "totals: verified=${total_verified} failed=${total_failed} unverifiable=${total_unverifiable}"

if [ "$total_failed" -gt 0 ]; then
  cena_warn "${total_failed} question(s) still Failed — inspect the admin queue before flipping Enforce"
fi

post_install_ok "cas-backfill"
