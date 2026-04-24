#!/usr/bin/env bash
# 06: Flip CAS gate mode to Enforce (RDY-036 rollout)
# Intentionally a manual step — this task exists to record the rollout
# moment with a structured log line and to verify earlier tasks passed.
set -euo pipefail
source "$(dirname "$0")/../lib/common.sh"

post_install_begin "gate-mode-enforce"

if [ "${CENA_CONFIRM_ENFORCE:-}" != "yes" ]; then
  post_install_skip "gate-mode-enforce" \
    "set CENA_CONFIRM_ENFORCE=yes to proceed (guards against accidental rollout)"
  exit 0
fi

if cena_is_dry_run; then
  post_install_skip "gate-mode-enforce" "DRY_RUN=1"
  exit 0
fi

# Config reload — adapter-specific. This stub just prints the instruction;
# replace with the actual config-management call (kubectl patch / consul /
# helm --reuse-values --set …) for your environment.
cat <<EOF
To complete: set CENA_CAS_GATE_MODE=Enforce in the Admin API deployment
  k8s:      kubectl set env deploy/cena-admin-api CENA_CAS_GATE_MODE=Enforce
  compose:  update .env, then: docker compose up -d admin-api
  helm:     helm upgrade cena ./deploy/helm/cena --reuse-values --set admin.casGateMode=Enforce
EOF

post_install_ok "gate-mode-enforce"
