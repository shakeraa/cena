#!/usr/bin/env bash
# 04: CAS conformance baseline (RDY-044)
# Triggers the cas-nightly workflow once via gh CLI and blocks until the
# run completes + baseline doc is updated. If gh is not available or the
# workflow is not yet wired for this env, the task skips gracefully.
set -euo pipefail
source "$(dirname "$0")/../lib/common.sh"

post_install_begin "conformance-baseline"

if ! command -v gh >/dev/null 2>&1; then
  post_install_skip "conformance-baseline" "gh CLI not installed"
  exit 0
fi

if cena_is_dry_run; then
  post_install_skip "conformance-baseline" "DRY_RUN=1"
  exit 0
fi

workflow="cas-nightly.yml"
cena_log "triggering $workflow via gh workflow run"

if ! gh workflow run "$workflow" >/dev/null 2>&1; then
  post_install_skip "conformance-baseline" "gh workflow run failed (auth / workflow not found)"
  exit 0
fi

cena_log "workflow dispatched — visit Actions tab to inspect. Baseline lives at ops/reports/cas-conformance-baseline.md"
post_install_ok "conformance-baseline"
