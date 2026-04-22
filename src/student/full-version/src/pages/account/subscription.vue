<script setup lang="ts">
// =============================================================================
// Cena Platform — Account → Subscription page (EPIC-PRR-I PRR-293/306)
//
// Authenticated parent view. Shows current subscription status + per-student
// list + sibling add/remove + refund-request + cancel. All backend commands
// validated via SubscriptionCommands (30-day refund window, terminal-state
// cancel semantics, etc.).
// =============================================================================

import { computed, ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useApi } from '@/composables/useApi'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.subscription',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()
const router = useRouter()

interface SubscriptionStatus {
  status: string
  currentTier: string | null
  currentBillingCycle: string | null
  activatedAt: string | null
  renewsAt: string | null
  linkedStudentCount: number
}

const status = ref<SubscriptionStatus | null>(null)
const loading = ref(true)
const actionError = ref<string | null>(null)

const refundDialog = ref(false)
const refundReason = ref('')
const cancelDialog = ref(false)
const cancelReason = ref('')
const siblingDialog = ref(false)
const siblingStudentId = ref('')
const siblingTier = ref<'Basic' | 'Plus' | 'Premium'>('Premium')

const activated = computed(() => status.value?.status === 'Active')
const withinRefundWindow = computed(() => {
  if (!status.value?.activatedAt) return false
  const activatedMs = new Date(status.value.activatedAt).getTime()
  return Date.now() - activatedMs <= 30 * 24 * 60 * 60 * 1000
})

async function load() {
  loading.value = true
  actionError.value = null
  const { data, error } = await useApi('/me/subscription').get().json<SubscriptionStatus>()
  if (error.value) actionError.value = t('accountSubscription.loadError')
  else status.value = data.value
  loading.value = false
}

async function requestRefund() {
  actionError.value = null
  const { error } = await useApi('/me/subscription/refund')
    .post({ reason: refundReason.value || 'user-requested' })
    .json()
  if (error.value) {
    actionError.value = t('accountSubscription.actionError')
  } else {
    refundDialog.value = false
    await load()
  }
}

async function cancelSubscription() {
  actionError.value = null
  const { error } = await useApi('/me/subscription/cancel')
    .post({ reason: cancelReason.value || 'user-requested' })
    .json()
  if (error.value) {
    actionError.value = t('accountSubscription.actionError')
  } else {
    cancelDialog.value = false
    await load()
  }
}

async function linkSibling() {
  actionError.value = null
  const { error } = await useApi('/me/subscription/siblings')
    .post({ siblingStudentId: siblingStudentId.value, tier: siblingTier.value })
    .json()
  if (error.value) {
    actionError.value = t('accountSubscription.actionError')
  } else {
    siblingDialog.value = false
    siblingStudentId.value = ''
    await load()
  }
}

const goPricing = () => router.push('/pricing')

onMounted(load)
</script>

<template>
  <div class="account-subscription pa-4 pa-md-8" data-testid="account-subscription-page">
    <h1 class="text-h4 font-weight-bold mb-4">
      {{ t('accountSubscription.title') }}
    </h1>

    <VAlert
      v-if="actionError"
      type="error"
      variant="tonal"
      closable
      class="mb-4"
      data-testid="account-subscription-error"
    >
      {{ actionError }}
    </VAlert>

    <div v-if="loading" class="d-flex justify-center pa-8" data-testid="account-subscription-loading">
      <VProgressCircular indeterminate color="primary" />
    </div>

    <template v-else-if="status">
      <!-- Status card -->
      <VCard class="pa-6 mb-4" variant="outlined">
        <div class="d-flex justify-space-between align-center flex-wrap ga-2">
          <div>
            <h2 class="text-h6">
              {{ t(`accountSubscription.status.${status.status.toLowerCase()}`) }}
              <span v-if="status.currentTier" class="text-body-1 text-medium-emphasis ms-2">
                — {{ status.currentTier }}
              </span>
            </h2>
            <p v-if="status.renewsAt" class="text-body-2 text-medium-emphasis mt-1">
              {{ t('accountSubscription.renewsAt', { date: new Date(status.renewsAt).toLocaleDateString() }) }}
            </p>
          </div>
          <VBtn
            v-if="status.status === 'Unsubscribed' || status.status === 'Cancelled'"
            color="primary"
            @click="goPricing"
          >
            {{ t('accountSubscription.seePricing') }}
          </VBtn>
        </div>
      </VCard>

      <!-- Linked students -->
      <VCard v-if="activated" class="pa-6 mb-4" variant="outlined">
        <div class="d-flex justify-space-between align-center mb-3">
          <h2 class="text-h6">
            {{ t('accountSubscription.siblings.title') }}
          </h2>
          <VBtn size="small" variant="tonal" color="primary" @click="siblingDialog = true">
            <VIcon icon="tabler-user-plus" class="me-1" />
            {{ t('accountSubscription.siblings.addCta') }}
          </VBtn>
        </div>
        <p class="text-body-2 text-medium-emphasis mb-0">
          {{ t('accountSubscription.siblings.count', { n: status.linkedStudentCount }) }}
        </p>
      </VCard>

      <!-- Refund + cancel -->
      <VCard v-if="activated" class="pa-6" variant="outlined">
        <h2 class="text-h6 mb-3">
          {{ t('accountSubscription.manage.title') }}
        </h2>
        <div class="d-flex ga-2 flex-wrap">
          <VBtn
            v-if="withinRefundWindow"
            variant="outlined"
            color="warning"
            data-testid="account-request-refund"
            @click="refundDialog = true"
          >
            {{ t('accountSubscription.manage.refund') }}
          </VBtn>
          <VBtn
            variant="outlined"
            color="error"
            data-testid="account-cancel"
            @click="cancelDialog = true"
          >
            {{ t('accountSubscription.manage.cancel') }}
          </VBtn>
        </div>
      </VCard>
    </template>

    <!-- Refund dialog -->
    <VDialog v-model="refundDialog" max-width="480" data-testid="refund-dialog">
      <VCard class="pa-6">
        <h2 class="text-h6 mb-2">{{ t('accountSubscription.refundDialog.title') }}</h2>
        <p class="text-body-2 text-medium-emphasis mb-3">
          {{ t('accountSubscription.refundDialog.body') }}
        </p>
        <VTextarea
          v-model="refundReason"
          :label="t('accountSubscription.refundDialog.reasonLabel')"
          rows="3"
          variant="outlined"
        />
        <div class="d-flex justify-end ga-2 mt-3">
          <VBtn variant="text" @click="refundDialog = false">
            {{ t('accountSubscription.common.cancel') }}
          </VBtn>
          <VBtn color="warning" variant="flat" data-testid="refund-confirm" @click="requestRefund">
            {{ t('accountSubscription.refundDialog.confirm') }}
          </VBtn>
        </div>
      </VCard>
    </VDialog>

    <!-- Cancel dialog with churn-reason (PRR-331) -->
    <VDialog v-model="cancelDialog" max-width="480" data-testid="cancel-dialog">
      <VCard class="pa-6">
        <h2 class="text-h6 mb-2">{{ t('accountSubscription.cancelDialog.title') }}</h2>
        <p class="text-body-2 text-medium-emphasis mb-3">
          {{ t('accountSubscription.cancelDialog.body') }}
        </p>
        <VTextarea
          v-model="cancelReason"
          :label="t('accountSubscription.cancelDialog.reasonLabel')"
          rows="3"
          variant="outlined"
        />
        <div class="d-flex justify-end ga-2 mt-3">
          <VBtn variant="text" @click="cancelDialog = false">
            {{ t('accountSubscription.common.back') }}
          </VBtn>
          <VBtn color="error" variant="flat" data-testid="cancel-confirm" @click="cancelSubscription">
            {{ t('accountSubscription.cancelDialog.confirm') }}
          </VBtn>
        </div>
      </VCard>
    </VDialog>

    <!-- Sibling-add dialog -->
    <VDialog v-model="siblingDialog" max-width="480" data-testid="sibling-dialog">
      <VCard class="pa-6">
        <h2 class="text-h6 mb-2">{{ t('accountSubscription.siblingDialog.title') }}</h2>
        <p class="text-body-2 text-medium-emphasis mb-3">
          {{ t('accountSubscription.siblingDialog.body') }}
        </p>
        <VTextField
          v-model="siblingStudentId"
          :label="t('accountSubscription.siblingDialog.idLabel')"
          variant="outlined"
          class="mb-3"
        />
        <VSelect
          v-model="siblingTier"
          :items="['Basic', 'Plus', 'Premium']"
          :label="t('accountSubscription.siblingDialog.tierLabel')"
          variant="outlined"
        />
        <div class="d-flex justify-end ga-2 mt-3">
          <VBtn variant="text" @click="siblingDialog = false">
            {{ t('accountSubscription.common.cancel') }}
          </VBtn>
          <VBtn
            color="primary"
            variant="flat"
            :disabled="!siblingStudentId"
            data-testid="sibling-confirm"
            @click="linkSibling"
          >
            {{ t('accountSubscription.siblingDialog.confirm') }}
          </VBtn>
        </div>
      </VCard>
    </VDialog>
  </div>
</template>
