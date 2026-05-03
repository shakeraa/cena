#!/usr/bin/env bash
# =============================================================================
# Cena — Stripe webhook event triggering helpers
#
# Wraps `stripe trigger <event>` for the 5 event types Cena's webhook handler
# processes. Useful for end-to-end testing without going through a real
# checkout flow.
#
# Usage:
#   ./scripts/stripe/trigger-events.sh checkout            # checkout.session.completed
#   ./scripts/stripe/trigger-events.sh paid                # invoice.paid
#   ./scripts/stripe/trigger-events.sh failed              # invoice.payment_failed
#   ./scripts/stripe/trigger-events.sh deleted             # customer.subscription.deleted
#   ./scripts/stripe/trigger-events.sh refunded            # charge.refunded
#   ./scripts/stripe/trigger-events.sh all                 # fire each in sequence
#
# Prerequisites:
#   - stripe login
#   - stripe listen --forward-to localhost:5050/api/webhooks/stripe  (in another terminal)
# =============================================================================

set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "usage: $0 <checkout|paid|failed|deleted|refunded|all>"
  exit 1
fi

case "$1" in
  checkout)  stripe trigger checkout.session.completed ;;
  paid)      stripe trigger invoice.paid ;;
  failed)    stripe trigger invoice.payment_failed ;;
  deleted)   stripe trigger customer.subscription.deleted ;;
  refunded)  stripe trigger charge.refunded ;;
  all)
    for kind in checkout paid failed deleted refunded; do
      echo "→ triggering: $kind"
      "$0" "$kind"
      sleep 2
    done
    ;;
  *)
    echo "unknown event kind: $1"
    exit 1
    ;;
esac
