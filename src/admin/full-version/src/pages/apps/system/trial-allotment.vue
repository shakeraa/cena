<script setup lang="ts">
// =============================================================================
// Cena Platform — Super-admin trial-allotment configuration page
// (task t_b89826b8bd60)
//
// Backed by GET / PATCH /api/admin/platform-config/trial-allotment.
// All four knobs default to 0 — when all are zero, the platform offers no
// trial and the SPA hides the "Start free trial" CTA. Setting any knob > 0
// enables trials; whichever-first semantics apply across the four limits.
//
// Per shipgate banned-mechanics: copy uses date-statement / counter language
// only. No "trial ends in N days", no "X days remaining" — we describe the
// allotment, not the urgency.
// =============================================================================

import { $api } from '@/utils/api'
import { useAbility } from '@casl/vue'

definePage({ meta: { action: 'manage', subject: 'Settings' } })

const { can } = useAbility()
const isSuperAdmin = computed(() => can('manage', 'all'))

interface TrialAllotmentDto {
  trialDurationDays: number
  trialTutorTurns: number
  trialPhotoDiagnostics: number
  trialPracticeSessions: number
  trialEnabled: boolean
  lastUpdatedAtUtc: string | null
  lastUpdatedByAdminEncrypted: string | null
}

// Range bounds mirrored from server-side TrialAllotmentValidator.
// Client-side validation is UX only; server is the source of truth.
const RANGES = {
  trialDurationDays: { min: 0, max: 30 },
  trialTutorTurns: { min: 0, max: 200 },
  trialPhotoDiagnostics: { min: 0, max: 50 },
  trialPracticeSessions: { min: 0, max: 20 },
} as const

const loading = ref(true)
const saving = ref(false)
const errorMessage = ref<string | null>(null)
const saveSuccess = ref(false)

const config = ref<TrialAllotmentDto>({
  trialDurationDays: 0,
  trialTutorTurns: 0,
  trialPhotoDiagnostics: 0,
  trialPracticeSessions: 0,
  trialEnabled: false,
  lastUpdatedAtUtc: null,
  lastUpdatedByAdminEncrypted: null,
})

const isEnabled = computed(() =>
  config.value.trialDurationDays > 0
  || config.value.trialTutorTurns > 0
  || config.value.trialPhotoDiagnostics > 0
  || config.value.trialPracticeSessions > 0,
)

const lastUpdatedDisplay = computed(() => {
  if (!config.value.lastUpdatedAtUtc)
    return 'never'
  return new Date(config.value.lastUpdatedAtUtc).toLocaleString()
})

async function loadConfig() {
  loading.value = true
  errorMessage.value = null
  try {
    const res = await $api<TrialAllotmentDto>('/admin/platform-config/trial-allotment')
    config.value = res
  }
  catch (err: any) {
    errorMessage.value = err?.message ?? 'Failed to load trial allotment'
  }
  finally {
    loading.value = false
  }
}

function inRange(value: number, key: keyof typeof RANGES): boolean {
  const { min, max } = RANGES[key]
  return Number.isInteger(value) && value >= min && value <= max
}

const isClientValid = computed(() =>
  inRange(config.value.trialDurationDays, 'trialDurationDays')
  && inRange(config.value.trialTutorTurns, 'trialTutorTurns')
  && inRange(config.value.trialPhotoDiagnostics, 'trialPhotoDiagnostics')
  && inRange(config.value.trialPracticeSessions, 'trialPracticeSessions'),
)

async function saveConfig() {
  if (!isClientValid.value) {
    errorMessage.value = 'One or more values are outside their allowed range.'
    return
  }
  saving.value = true
  errorMessage.value = null
  saveSuccess.value = false
  try {
    const res = await $api<TrialAllotmentDto>('/admin/platform-config/trial-allotment', {
      method: 'PATCH',
      body: {
        trialDurationDays: config.value.trialDurationDays,
        trialTutorTurns: config.value.trialTutorTurns,
        trialPhotoDiagnostics: config.value.trialPhotoDiagnostics,
        trialPracticeSessions: config.value.trialPracticeSessions,
      },
    })
    config.value = res
    saveSuccess.value = true
    setTimeout(() => { saveSuccess.value = false }, 4000)
  }
  catch (err: any) {
    // Server returns structured 400 with field+reason details when validation
    // fails. Surface the field-level message when present so the admin knows
    // exactly which knob is wrong.
    const detail = err?.details
    if (detail?.field && detail?.reason)
      errorMessage.value = `${detail.field}: ${detail.reason}`
    else
      errorMessage.value = err?.message ?? 'Failed to save trial allotment'
  }
  finally {
    saving.value = false
  }
}

onMounted(loadConfig)
</script>

<template>
  <VContainer>
    <VRow>
      <VCol cols="12">
        <VCard>
          <VCardTitle>Trial allotment configuration</VCardTitle>
          <VCardSubtitle>
            Platform-wide trial knobs. All zeros = no trial offered.
            Set any value &gt; 0 to enable trials. Whichever limit is hit first
            ends the trial.
          </VCardSubtitle>
          <VCardText>
            <VAlert
              v-if="errorMessage"
              type="error"
              variant="tonal"
              class="mb-4"
              data-testid="trial-allotment-error"
            >
              {{ errorMessage }}
            </VAlert>
            <VAlert
              v-if="saveSuccess"
              type="success"
              variant="tonal"
              class="mb-4"
              data-testid="trial-allotment-success"
            >
              Saved. New values apply to subsequent trial-starts; in-flight
              trials are unaffected.
            </VAlert>

            <VAlert
              :type="isEnabled ? 'info' : 'warning'"
              variant="tonal"
              class="mb-4"
              data-testid="trial-allotment-status"
            >
              <strong>Trial status:</strong>
              {{ isEnabled ? 'enabled (at least one knob > 0)' : 'disabled (all zero — no trial offered)' }}
              <br>
              <small>Last updated: {{ lastUpdatedDisplay }}</small>
            </VAlert>

            <VProgressLinear
              v-if="loading"
              indeterminate
              class="mb-4"
            />

            <VForm
              v-if="!loading"
              @submit.prevent="saveConfig"
            >
              <VRow>
                <VCol
                  cols="12"
                  md="6"
                >
                  <VTextField
                    v-model.number="config.trialDurationDays"
                    label="Trial duration (days)"
                    type="number"
                    :min="RANGES.trialDurationDays.min"
                    :max="RANGES.trialDurationDays.max"
                    hint="0 = no calendar bound. Range 0..30."
                    persistent-hint
                    :disabled="!isSuperAdmin || saving"
                    data-testid="input-trial-duration-days"
                  />
                </VCol>
                <VCol
                  cols="12"
                  md="6"
                >
                  <VTextField
                    v-model.number="config.trialTutorTurns"
                    label="Trial tutor turns"
                    type="number"
                    :min="RANGES.trialTutorTurns.min"
                    :max="RANGES.trialTutorTurns.max"
                    hint="0 = no per-trial cap on tutor turns. Range 0..200."
                    persistent-hint
                    :disabled="!isSuperAdmin || saving"
                    data-testid="input-trial-tutor-turns"
                  />
                </VCol>
                <VCol
                  cols="12"
                  md="6"
                >
                  <VTextField
                    v-model.number="config.trialPhotoDiagnostics"
                    label="Trial photo diagnostics"
                    type="number"
                    :min="RANGES.trialPhotoDiagnostics.min"
                    :max="RANGES.trialPhotoDiagnostics.max"
                    hint="0 = no per-trial cap on photo uploads. Range 0..50."
                    persistent-hint
                    :disabled="!isSuperAdmin || saving"
                    data-testid="input-trial-photo-diagnostics"
                  />
                </VCol>
                <VCol
                  cols="12"
                  md="6"
                >
                  <VTextField
                    v-model.number="config.trialPracticeSessions"
                    label="Trial practice sessions"
                    type="number"
                    :min="RANGES.trialPracticeSessions.min"
                    :max="RANGES.trialPracticeSessions.max"
                    hint="0 = no per-trial cap on practice sessions. Range 0..20."
                    persistent-hint
                    :disabled="!isSuperAdmin || saving"
                    data-testid="input-trial-practice-sessions"
                  />
                </VCol>
              </VRow>

              <VBtn
                color="primary"
                type="submit"
                :loading="saving"
                :disabled="!isSuperAdmin || !isClientValid || saving"
                class="mt-4"
                data-testid="trial-allotment-save"
              >
                Save trial allotment
              </VBtn>
              <VBtn
                variant="text"
                class="mt-4 ms-2"
                :disabled="loading || saving"
                data-testid="trial-allotment-reload"
                @click="loadConfig"
              >
                Reload
              </VBtn>
              <p
                v-if="!isSuperAdmin"
                class="text-caption text-medium-emphasis mt-3"
              >
                Read-only: super-admin role required to change trial allotment.
              </p>
            </VForm>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>
  </VContainer>
</template>
