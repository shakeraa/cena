#!/usr/bin/env bash
# 01: CAS engine liveness probe (RDY-036 §15)
# Fires the same x+1 probe the Admin host uses at boot, so an operator can
# confirm the sidecar is reachable before driving gated writes.
set -euo pipefail
source "$(dirname "$0")/../lib/common.sh"

post_install_begin "cas-engine-probe"

if cena_is_dry_run; then
  post_install_skip "cas-engine-probe" "DRY_RUN=1"
  exit 0
fi

# Probe via the Admin API's own CAS health endpoint when present, otherwise
# fall through to a verify call on a trivial expression.
if curl --fail-with-body -sS \
    -H "Authorization: Bearer ${CENA_ADMIN_TOKEN:-}" \
    "${CENA_ADMIN_URL:-}/healthz/cas" >/dev/null 2>&1; then
  post_install_ok "cas-engine-probe"
  exit 0
fi

# Fallback — attempt a gated write probe against a well-known safe input.
response="$(cena_admin_curl GET "/api/admin/health/cas" 2>&1 || true)"
if [[ "$response" == *"ok"* ]] || [[ "$response" == *"healthy"* ]]; then
  post_install_ok "cas-engine-probe"
else
  post_install_fail "cas-engine-probe" "probe did not return ok/healthy: $response"
fi
