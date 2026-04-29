// =============================================================================
// Cena Platform — entitlementStore (Phase 2 paywall — SPA side of trial-then-paywall)
//
// Mirrors the backend's GET /api/me/entitlement payload + receives 402
// updates from the global $api 402 interceptor. Surfaces:
//
//   - effectiveStatus / tier / hasPaymentMethodOnFile
//   - active trial counters + caps (when Trialing)
//   - lastBlock: { reason, feature }, set whenever an API call returns
//     402. Cleared on successful re-fetch.
//
// Components consume:
//   - TrialEndCard.vue — renders when lastBlock != null
//   - SessionStartForm.vue / TutorChat.vue — read isBlocked to disable buttons
//   - requiresActiveEntitlement route guard — redirects to a paywall route
//     if effectiveStatus is Unsubscribed/Expired/Cancelled/Refunded
//
// Persistence: the store is volatile (no localStorage). The /entitlement
// endpoint is the single source of truth; we re-fetch on bootstrap.
// =============================================================================

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { $api } from '@/api/$api'

export type EffectiveStatus =
  | 'Active'
  | 'PastDue'
  | 'Trialing'
  | 'Unsubscribed'
  | 'Expired'
  | 'Cancelled'
  | 'Refunded'

export type EffectiveTier = 'Free' | 'TrialPlus' | 'Plus' | 'Premium'

export type CapFeature =
  | 'tutor_turn'
  | 'photo_diagnostic'
  | 'practice_session'
  | 'generic'

export interface TrialState {
  startedAt: string
  endsAt: string
  daysRemaining: number
  caps: {
    tutorTurns: number       // 0 = unbounded
    photoDiagnostics: number
    practiceSessions: number
  }
  used: {
    tutorTurns: number
    photoDiagnostics: number
    practiceSessions: number
  }
}

export interface EntitlementBlock {
  /** "trial_cap_reached" | "unsubscribed" | "expired" | "cancelled" | "refunded" */
  reason: string
  /** Set only when reason === 'trial_cap_reached' */
  feature: CapFeature | null
  /** Effective status the server reported alongside the 402 */
  effectiveStatus: EffectiveStatus
  /** When the block was observed, for stale-block GC */
  observedAt: string
}

export interface EntitlementSnapshot {
  tier: EffectiveTier
  effectiveStatus: EffectiveStatus
  hasPaymentMethodOnFile: boolean
  trial: TrialState | null
}

const ENTITLEMENT_ENDPOINT = '/me/entitlement'

export const useEntitlementStore = defineStore('entitlement', () => {
  // ── State ──
  const tier = ref<EffectiveTier>('Free')
  const effectiveStatus = ref<EffectiveStatus>('Unsubscribed')
  const hasPaymentMethodOnFile = ref(false)
  const trial = ref<TrialState | null>(null)
  const lastBlock = ref<EntitlementBlock | null>(null)
  const isLoaded = ref(false)
  const isLoading = ref(false)

  // ── Derived ──
  const isBlocked = computed(() => lastBlock.value !== null)
  const isTrialing = computed(() => effectiveStatus.value === 'Trialing')
  const isEntitled = computed(() =>
    effectiveStatus.value === 'Active'
    || effectiveStatus.value === 'PastDue'
    || (effectiveStatus.value === 'Trialing' && lastBlock.value?.reason !== 'trial_cap_reached'),
  )

  /**
   * Refresh the snapshot from /api/me/entitlement. Idempotent. Concurrent
   * calls share a single in-flight request via the simple `isLoading` flag.
   */
  async function refresh(): Promise<void> {
    if (isLoading.value)
      return
    isLoading.value = true
    try {
      const dto = await $api<EntitlementSnapshot>(ENTITLEMENT_ENDPOINT)
      tier.value = dto.tier
      effectiveStatus.value = dto.effectiveStatus
      hasPaymentMethodOnFile.value = dto.hasPaymentMethodOnFile
      trial.value = dto.trial
      // Clear lastBlock on a successful refresh — server is authoritative.
      lastBlock.value = null
      isLoaded.value = true
    }
    catch (err) {
      // 402 path is handled by the global interceptor (record402); other
      // errors leave isLoaded false so consumers know to defer rendering.
      // Never throw — boot path must not bounce on entitlement failure.
      // eslint-disable-next-line no-console
      console.warn('[entitlementStore] refresh failed', err)
    }
    finally {
      isLoading.value = false
    }
  }

  /**
   * Called by the $api 402 interceptor with the parsed body. Updates
   * effectiveStatus + lastBlock so the SPA can surface the paywall card
   * immediately without re-fetching.
   */
  function record402(body: {
    error?: string
    reason?: string
    tier?: string
    effectiveStatus?: string
    feature?: string | null
  }): void {
    if (typeof body.effectiveStatus === 'string')
      effectiveStatus.value = body.effectiveStatus as EffectiveStatus
    if (typeof body.tier === 'string')
      tier.value = body.tier as EffectiveTier
    lastBlock.value = {
      reason: body.reason ?? 'entitlement_required',
      feature: (body.feature as CapFeature | null) ?? null,
      effectiveStatus: (body.effectiveStatus as EffectiveStatus) ?? 'Unsubscribed',
      observedAt: new Date().toISOString(),
    }
  }

  /**
   * Manually clear the block — used when the SPA navigates away from a
   * blocked context or after the user dismisses the TrialEndCard.
   */
  function clearBlock(): void {
    lastBlock.value = null
  }

  function $reset(): void {
    tier.value = 'Free'
    effectiveStatus.value = 'Unsubscribed'
    hasPaymentMethodOnFile.value = false
    trial.value = null
    lastBlock.value = null
    isLoaded.value = false
    isLoading.value = false
  }

  return {
    // state
    tier,
    effectiveStatus,
    hasPaymentMethodOnFile,
    trial,
    lastBlock,
    isLoaded,
    isLoading,
    // derived
    isBlocked,
    isTrialing,
    isEntitled,
    // actions
    refresh,
    record402,
    clearBlock,
    $reset,
  }
})
