<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useApiQuery } from '@/composables/useApiQuery'
import { useApiMutation } from '@/composables/useApiMutation'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.settingsPrivacy',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()

// ---- Visibility/sharing preferences (existing) ----
// FIND-privacy-010: ICO Children's Code Std 3+7 — all defaults OFF (high-privacy)
const prefs = ref({
  showProgressToClass: false,
  allowPeerComparison: false,
  shareAnalytics: false,
})

if (typeof localStorage !== 'undefined') {
  const stored = localStorage.getItem('cena-privacy-prefs')
  if (stored) {
    try {
      Object.assign(prefs.value, JSON.parse(stored))
    }
    catch {
      // ignore
    }
  }
}

function persist() {
  if (typeof localStorage !== 'undefined')
    localStorage.setItem('cena-privacy-prefs', JSON.stringify(prefs.value))
}

// ---- GDPR Self-Service (FIND-privacy-003) ----
const showExportDialog = ref(false)
const showErasureDialog = ref(false)
const showDsarDialog = ref(false)

const exportLoading = ref(false)
const exportSuccess = ref(false)
const exportError = ref<string | null>(null)

const erasureLoading = ref(false)
const erasureSuccess = ref(false)
const erasureError = ref<string | null>(null)

const dsarMessage = ref('')
const dsarLoading = ref(false)
const dsarSuccess = ref(false)
const dsarError = ref<string | null>(null)
const dsarTrackingId = ref<string | null>(null)

// Load erasure status on mount
const erasureStatus = useApiQuery<{
  studentId: string
  hasActiveRequest: boolean
  status?: string
  requestedAt?: string
  coolingPeriodEnds?: string
}>('/api/me/gdpr/erasure/status')

// Export mutation
const exportMutation = useApiMutation<{ exportedAt: string; studentId: string }>('/api/me/gdpr/export', 'POST')

async function handleExport() {
  exportLoading.value = true
  exportError.value = null
  exportSuccess.value = false
  try {
    const result = await exportMutation.execute({} as never)
    // Trigger download
    const blob = new Blob([JSON.stringify(result, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `cena-data-export-${new Date().toISOString().slice(0, 10)}.json`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
    exportSuccess.value = true
    showExportDialog.value = false
  }
  catch {
    exportError.value = t('settingsPage.privacy.exportError')
  }
  finally {
    exportLoading.value = false
  }
}

// Erasure mutation
const erasureMutation = useApiMutation<{
  studentId: string
  status: string
  coolingPeriodEnds: string
  message: string
}>('/api/me/gdpr/erasure', 'POST')

async function handleErasure() {
  erasureLoading.value = true
  erasureError.value = null
  erasureSuccess.value = false
  try {
    await erasureMutation.execute({} as never)
    erasureSuccess.value = true
    showErasureDialog.value = false
    // Refresh erasure status
    await erasureStatus.refresh()
  }
  catch {
    erasureError.value = t('settingsPage.privacy.erasureError')
  }
  finally {
    erasureLoading.value = false
  }
}

// DSAR mutation
const dsarMutation = useApiMutation<{
  trackingId: string
  status: string
  slaDeadline: string
}>('/api/me/dsar', 'POST')

async function handleDsar() {
  if (!dsarMessage.value.trim()) return
  dsarLoading.value = true
  dsarError.value = null
  dsarSuccess.value = false
  try {
    const result = await dsarMutation.execute({ message: dsarMessage.value } as never)
    dsarTrackingId.value = result.trackingId
    dsarSuccess.value = true
    dsarMessage.value = ''
    showDsarDialog.value = false
  }
  catch {
    dsarError.value = t('settingsPage.privacy.dsarError')
  }
  finally {
    dsarLoading.value = false
  }
}
</script>

<template>
  <div
    class="settings-privacy-page pa-4"
    data-testid="settings-privacy-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('settingsPage.privacy.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('settingsPage.privacy.subtitle') }}
    </p>

    <!-- Visibility preferences -->
    <VCard
      variant="outlined"
      class="pa-5 mb-6"
    >
      <VSwitch
        v-model="prefs.showProgressToClass"
        :label="t('settingsPage.privacy.showProgressToClass')"
        color="primary"
        data-testid="privacy-show-progress"
        @update:model-value="persist"
      />
      <VSwitch
        v-model="prefs.allowPeerComparison"
        :label="t('settingsPage.privacy.allowPeerComparison')"
        color="primary"
        data-testid="privacy-peer-comparison"
        @update:model-value="persist"
      />
      <VSwitch
        v-model="prefs.shareAnalytics"
        :label="t('settingsPage.privacy.shareAnalytics')"
        color="primary"
        data-testid="privacy-analytics"
        @update:model-value="persist"
      />
    </VCard>

    <!-- FIND-privacy-002: Links to full policy pages from settings -->
    <div
      class="mt-6 text-body-2 text-medium-emphasis"
      data-testid="settings-legal-links"
    >
      <RouterLink
        to="/privacy"
        class="text-medium-emphasis text-decoration-underline"
      >
        {{ t('legal.footer.privacyLink') }}
      </RouterLink>
      <span class="mx-2">&middot;</span>
      <RouterLink
        to="/terms"
        class="text-medium-emphasis text-decoration-underline"
      >
        {{ t('legal.footer.termsLink') }}
      </RouterLink>
      <span class="mx-2">&middot;</span>
      <RouterLink
        to="/privacy/children"
        class="text-medium-emphasis text-decoration-underline"
      >
        {{ t('legal.footer.childrenLink') }}
      </RouterLink>
    </div>

    <!-- GDPR Data Rights Section (FIND-privacy-003) -->
    <h2 class="text-h5 mb-3">
      {{ t('settingsPage.privacy.dataRightsTitle') }}
    </h2>
    <p class="text-body-2 text-medium-emphasis mb-4">
      {{ t('settingsPage.privacy.dataRightsSubtitle') }}
    </p>

    <!-- Active erasure request banner -->
    <VAlert
      v-if="erasureStatus.data.value?.hasActiveRequest"
      type="warning"
      variant="tonal"
      class="mb-4"
      data-testid="erasure-active-banner"
    >
      {{ t('settingsPage.privacy.erasureActiveBanner', {
        status: erasureStatus.data.value?.status ?? '',
        date: erasureStatus.data.value?.coolingPeriodEnds
          ? new Date(erasureStatus.data.value.coolingPeriodEnds).toLocaleDateString()
          : '',
      }) }}
    </VAlert>

    <!-- Success alerts -->
    <VAlert
      v-if="exportSuccess"
      type="success"
      variant="tonal"
      class="mb-4"
      closable
      data-testid="export-success-alert"
      @click:close="exportSuccess = false"
    >
      {{ t('settingsPage.privacy.exportSuccess') }}
    </VAlert>

    <VAlert
      v-if="erasureSuccess"
      type="success"
      variant="tonal"
      class="mb-4"
      closable
      data-testid="erasure-success-alert"
      @click:close="erasureSuccess = false"
    >
      {{ t('settingsPage.privacy.erasureSuccess') }}
    </VAlert>

    <VAlert
      v-if="dsarSuccess && dsarTrackingId"
      type="success"
      variant="tonal"
      class="mb-4"
      closable
      data-testid="dsar-success-alert"
      @click:close="dsarSuccess = false"
    >
      {{ t('settingsPage.privacy.dsarSuccess', { trackingId: dsarTrackingId }) }}
    </VAlert>

    <!-- Error alerts -->
    <VAlert
      v-if="exportError"
      type="error"
      variant="tonal"
      class="mb-4"
      closable
      @click:close="exportError = null"
    >
      {{ exportError }}
    </VAlert>

    <VAlert
      v-if="erasureError"
      type="error"
      variant="tonal"
      class="mb-4"
      closable
      @click:close="erasureError = null"
    >
      {{ erasureError }}
    </VAlert>

    <VAlert
      v-if="dsarError"
      type="error"
      variant="tonal"
      class="mb-4"
      closable
      @click:close="dsarError = null"
    >
      {{ dsarError }}
    </VAlert>

    <VCard
      variant="outlined"
      class="pa-5"
    >
      <div class="d-flex flex-column ga-3">
        <!-- Download my data -->
        <VBtn
          color="primary"
          variant="outlined"
          prepend-icon="ri-download-2-line"
          data-testid="btn-download-data"
          :loading="exportLoading"
          @click="showExportDialog = true"
        >
          {{ t('settingsPage.privacy.downloadData') }}
        </VBtn>

        <!-- Request data deletion -->
        <VBtn
          color="error"
          variant="outlined"
          prepend-icon="ri-delete-bin-line"
          data-testid="btn-delete-data"
          :loading="erasureLoading"
          :disabled="erasureStatus.data.value?.hasActiveRequest === true"
          @click="showErasureDialog = true"
        >
          {{ t('settingsPage.privacy.deleteData') }}
        </VBtn>

        <!-- What data do you have? (DSAR) -->
        <VBtn
          color="secondary"
          variant="outlined"
          prepend-icon="ri-question-line"
          data-testid="btn-dsar"
          :loading="dsarLoading"
          @click="showDsarDialog = true"
        >
          {{ t('settingsPage.privacy.whatData') }}
        </VBtn>
      </div>
    </VCard>

    <!-- Export Confirmation Dialog -->
    <VDialog
      v-model="showExportDialog"
      max-width="500"
    >
      <VCard data-testid="export-dialog">
        <VCardTitle>{{ t('settingsPage.privacy.exportDialogTitle') }}</VCardTitle>
        <VCardText>{{ t('settingsPage.privacy.exportDialogBody') }}</VCardText>
        <VCardActions>
          <VSpacer />
          <VBtn
            variant="text"
            @click="showExportDialog = false"
          >
            {{ t('common.cancel') }}
          </VBtn>
          <VBtn
            color="primary"
            variant="flat"
            :loading="exportLoading"
            data-testid="export-confirm-btn"
            @click="handleExport"
          >
            {{ t('settingsPage.privacy.exportConfirm') }}
          </VBtn>
        </VCardActions>
      </VCard>
    </VDialog>

    <!-- Erasure Confirmation Dialog -->
    <VDialog
      v-model="showErasureDialog"
      max-width="500"
    >
      <VCard data-testid="erasure-dialog">
        <VCardTitle class="text-error">
          {{ t('settingsPage.privacy.erasureDialogTitle') }}
        </VCardTitle>
        <VCardText>{{ t('settingsPage.privacy.erasureDialogBody') }}</VCardText>
        <VCardActions>
          <VSpacer />
          <VBtn
            variant="text"
            @click="showErasureDialog = false"
          >
            {{ t('common.cancel') }}
          </VBtn>
          <VBtn
            color="error"
            variant="flat"
            :loading="erasureLoading"
            data-testid="erasure-confirm-btn"
            @click="handleErasure"
          >
            {{ t('settingsPage.privacy.erasureConfirm') }}
          </VBtn>
        </VCardActions>
      </VCard>
    </VDialog>

    <!-- DSAR Dialog -->
    <VDialog
      v-model="showDsarDialog"
      max-width="500"
    >
      <VCard data-testid="dsar-dialog">
        <VCardTitle>{{ t('settingsPage.privacy.dsarDialogTitle') }}</VCardTitle>
        <VCardText>
          <p class="mb-4">
            {{ t('settingsPage.privacy.dsarDialogBody') }}
          </p>
          <VTextarea
            v-model="dsarMessage"
            :label="t('settingsPage.privacy.dsarMessageLabel')"
            :placeholder="t('settingsPage.privacy.dsarMessagePlaceholder')"
            rows="3"
            data-testid="dsar-message-input"
          />
        </VCardText>
        <VCardActions>
          <VSpacer />
          <VBtn
            variant="text"
            @click="showDsarDialog = false"
          >
            {{ t('common.cancel') }}
          </VBtn>
          <VBtn
            color="primary"
            variant="flat"
            :loading="dsarLoading"
            :disabled="!dsarMessage.trim()"
            data-testid="dsar-confirm-btn"
            @click="handleDsar"
          >
            {{ t('settingsPage.privacy.dsarConfirm') }}
          </VBtn>
        </VCardActions>
      </VCard>
    </VDialog>
  </div>
</template>

<style scoped>
.settings-privacy-page {
  max-inline-size: 700px;
  margin-inline: auto;
}
</style>
