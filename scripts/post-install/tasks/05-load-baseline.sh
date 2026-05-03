#!/usr/bin/env bash
# 05: k6 load baseline (RDY-051)
# Triggers the cas-load-nightly workflow. Requires CENA_ADMIN_URL +
# CENA_ADMIN_TOKEN secrets to be present on the repo.
set -euo pipefail
source "$(dirname "$0")/../lib/common.sh"

post_install_begin "load-baseline"

if ! command -v gh >/dev/null 2>&1; then
  post_install_skip "load-baseline" "gh CLI not installed"
  exit 0
fi

if cena_is_dry_run; then
  post_install_skip "load-baseline" "DRY_RUN=1"
  exit 0
fi

workflow="cas-load-nightly.yml"
cena_log "triggering $workflow via gh workflow run"

if ! gh workflow run "$workflow" >/dev/null 2>&1; then
  post_install_skip "load-baseline" "gh workflow run failed (auth / workflow not found)"
  exit 0
fi

cena_log "workflow dispatched — populate ops/reports/cas-load-baseline.md from the uploaded artifact"
post_install_ok "load-baseline"
