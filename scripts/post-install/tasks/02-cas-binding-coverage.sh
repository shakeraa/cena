#!/usr/bin/env bash
# 02: CAS binding coverage check (RDY-040)
# Refuses to continue if published_math > verified_bindings. Mirrors the
# CasBindingCoverageStartupCheck hosted service at the operator level.
set -euo pipefail
source "$(dirname "$0")/../lib/common.sh"

post_install_begin "cas-binding-coverage"

if cena_is_dry_run; then
  post_install_skip "cas-binding-coverage" "DRY_RUN=1"
  exit 0
fi

# Scrape the gauge published by the Admin API.
ratio="$(cena_admin_curl GET "/metrics" 2>/dev/null \
  | awk '/^cena_cas_binding_coverage_ratio /{print $2; exit}')"

if [ -z "${ratio:-}" ]; then
  post_install_fail "cas-binding-coverage" "cena_cas_binding_coverage_ratio not exposed — is the Admin API running?"
fi

# Ratio is 1.0 when all published math questions have Verified bindings.
# Anything less means we cannot safely flip to Enforce.
awk_cmp="$(awk -v r="$ratio" 'BEGIN { print (r >= 1.0) ? "ok" : "low" }')"
if [ "$awk_cmp" = "ok" ]; then
  post_install_ok "cas-binding-coverage"
else
  post_install_fail "cas-binding-coverage" "ratio=$ratio < 1.0 — run task 03 (cas-backfill) first"
fi
