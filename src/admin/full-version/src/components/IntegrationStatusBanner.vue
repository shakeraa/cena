<script lang="ts" setup>
// =============================================================================
// IntegrationStatusBanner — surfaces "is the Anthropic LLM tier reachable?"
//
// 2026-05-03: replaces the previous silent log-and-degrade behaviour where
// concept extraction / variant generation / quality-gate would log a warning
// and the curator never noticed until they saw missing concepts in the
// review panel.
//
// Polls /api/admin/integrations/anthropic/status every 60s. Three visual
// states:
//
//   - DOWN  (no key configured OR every recent call failed) → red banner
//   - DEGRADED (some recent failures but not all) → amber banner
//   - HEALTHY / UNKNOWN → no banner (don't pollute the layout when fine)
//
// The banner is dismissible; the dismissal is per-session (sessionStorage)
// so a curator can suppress it for the current admin session but the
// banner returns next morning when they log in again. Operator decisions
// (configure the key) shouldn't be made invisible by a stale dismissal.
// =============================================================================

import { onBeforeUnmount, onMounted, ref } from 'vue'
import { $api } from '@/utils/api'

interface AnthropicIntegrationStatus {
  apiKeyConfigured: boolean
  keySource: 'None' | 'Marten' | 'Configuration'
  reachability: 'Unknown' | 'Healthy' | 'Degraded' | 'Down'
  recentSuccessCount: number
  recentFailureCount: number
  lastFailureCategory: string | null
  lastFailureMessage: string | null
  lastSuccessAt: string | null
  lastFailureAt: string | null
  checkedAt: string
}

const POLL_INTERVAL_MS = 60_000
const DISMISSAL_KEY = 'cena.integrationBanner.anthropic.dismissed'

const status = ref<AnthropicIntegrationStatus | null>(null)
const loadError = ref(false)
const dismissed = ref<boolean>(typeof window !== 'undefined' && sessionStorage.getItem(DISMISSAL_KEY) === '1')

let pollTimer: number | null = null

async function fetchStatus() {
  try {
    status.value = await $api<AnthropicIntegrationStatus>('/admin/integrations/anthropic/status')
    loadError.value = false
  }
  catch {
    // Don't show our own banner about ourselves failing — the SPA-wide
    // error toast already covers transport problems against the admin API.
    loadError.value = true
  }
}

function dismiss() {
  dismissed.value = true
  if (typeof window !== 'undefined')
    sessionStorage.setItem(DISMISSAL_KEY, '1')
}

onMounted(() => {
  void fetchStatus()
  pollTimer = window.setInterval(() => { void fetchStatus() }, POLL_INTERVAL_MS)
})

onBeforeUnmount(() => {
  if (pollTimer !== null) {
    clearInterval(pollTimer)
    pollTimer = null
  }
})

const showBanner = computed(() => {
  if (dismissed.value || loadError.value || !status.value) return false
  return status.value.reachability === 'Down' || status.value.reachability === 'Degraded'
})

const severity = computed(() => status.value?.reachability === 'Down' ? 'error' : 'warning')

const headline = computed(() => {
  if (!status.value) return ''
  const s = status.value
  if (!s.apiKeyConfigured)
    return 'Anthropic API key is not configured — every LLM-tier feature is silently falling back to rules-only output.'
  if (s.reachability === 'Down')
    return `Anthropic LLM tier is failing — ${s.recentFailureCount} of the last ${s.recentFailureCount + s.recentSuccessCount} calls failed.`
  return `Anthropic LLM tier is degraded — ${s.recentFailureCount} of the last ${s.recentFailureCount + s.recentSuccessCount} calls failed.`
})

const detail = computed(() => {
  if (!status.value) return ''
  const s = status.value
  if (!s.apiKeyConfigured) {
    return [
      'Affected features: concept extraction, variant generation, quality gate, OCR text enhancement, Bagrut question segmentation.',
      'Set the key under Settings → AI Generation, or the Anthropic:ApiKey configuration value.',
    ].join(' ')
  }
  const cat = s.lastFailureCategory ?? 'Other'
  const msg = s.lastFailureMessage ?? ''
  if (cat === 'AuthFailure')
    return `Last failure: authentication rejected. Check the saved key still matches the Anthropic console. ${msg}`
  if (cat === 'CircuitOpen')
    return `Last failure: circuit breaker open. Falls back automatically; will retry once the breaker half-opens. ${msg}`
  if (cat === 'Transport')
    return `Last failure: transport / timeout. Likely a flaky network hop. ${msg}`
  return `Last failure: ${cat}. ${msg}`
})

const ctaTo = computed(() => {
  // Anchor links to the "Anthropic Configuration" card on the AI Settings
  // page so the curator lands directly on the API Key input. The card has
  // id="anthropic-api-key" — keep these in lockstep when the page is
  // restructured.
  if (!status.value?.apiKeyConfigured) return '/apps/system/ai-settings#anthropic-api-key'
  return '/apps/system/architecture'
})

const ctaLabel = computed(() => {
  if (!status.value?.apiKeyConfigured) return 'Configure API key'
  return 'View system health'
})
</script>

<template>
  <VAlert
    v-if="showBanner"
    :type="severity"
    variant="tonal"
    border="start"
    closable
    class="integration-status-banner ma-3"
    role="alert"
    aria-live="polite"
    @click:close="dismiss"
  >
    <template #title>
      <div class="text-body-1 font-weight-medium">
        {{ headline }}
      </div>
    </template>
    <div class="text-body-2 mt-1">
      {{ detail }}
    </div>
    <div class="mt-2 d-flex align-center gap-2">
      <VBtn
        variant="elevated"
        :color="severity"
        size="small"
        :to="ctaTo"
      >
        {{ ctaLabel }}
      </VBtn>
      <VBtn
        variant="text"
        size="small"
        @click="dismiss"
      >
        Dismiss for this session
      </VBtn>
    </div>
  </VAlert>
</template>

<style scoped>
.integration-status-banner {
  border-radius: 8px;
}
</style>
