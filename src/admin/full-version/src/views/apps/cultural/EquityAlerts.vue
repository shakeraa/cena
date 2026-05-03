<script setup lang="ts">
import { $api } from '@/utils/api'

interface EquityAlert {
  id: string
  severity: 'error' | 'warning' | 'info'
  title: string
  message: string
  affectedContexts: string[]
  masteryGap: number
  recommendation: string
}

const loading = ref(true)
const error = ref<string | null>(null)
const alerts = ref<EquityAlert[]>([])

const fetchAlerts = async () => {
  loading.value = true
  error.value = null
  try {
    const data = await $api<{ alerts: EquityAlert[] }>('/admin/cultural/equity-alerts')
    alerts.value = data.alerts ?? []
  }
  catch (err: any) {
    console.error('Failed to fetch equity alerts:', err)
    error.value = err.message ?? 'Failed to load equity alerts'
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchAlerts)

const severityIcon = (severity: string): string => {
  if (severity === 'error') return 'tabler-alert-octagon'
  if (severity === 'warning') return 'tabler-alert-triangle'
  return 'tabler-info-circle'
}

const severityColor = (severity: string): string => {
  if (severity === 'error') return 'error'
  if (severity === 'warning') return 'warning'
  return 'info'
}

defineExpose({ refresh: fetchAlerts })
</script>

<template>
  <VCard :loading="loading">
    <VCardItem title="Equity Alerts">
      <template #subtitle>
        Mastery divergence and content balance warnings
      </template>

      <template #append>
        <VBtn
          icon="tabler-refresh"
          variant="text"
          size="small"
          :loading="loading"
          @click="fetchAlerts"
        />
      </template>
    </VCardItem>

    <VCardText>
      <VAlert
        v-if="error"
        type="error"
        variant="tonal"
        class="mb-4"
      >
        {{ error }}
      </VAlert>

      <div
        v-if="alerts.length > 0"
        class="d-flex flex-column gap-y-3"
      >
        <VAlert
          v-for="alert in alerts"
          :key="alert.id"
          :type="alert.severity"
          variant="tonal"
          prominent
          border="start"
        >
          <template #prepend>
            <VIcon
              :icon="severityIcon(alert.severity)"
              :color="severityColor(alert.severity)"
              size="24"
            />
          </template>

          <VAlertTitle class="text-body-1 font-weight-semibold mb-1">
            {{ alert.title }}
          </VAlertTitle>

          <div class="text-body-2 mb-2">
            {{ alert.message }}
          </div>

          <div
            v-if="alert.masteryGap > 0"
            class="d-flex align-center gap-x-2 mb-2"
          >
            <VChip
              :color="alert.masteryGap > 15 ? 'error' : 'warning'"
              label
              size="small"
            >
              {{ alert.masteryGap }}% gap
            </VChip>
            <span class="text-caption text-medium-emphasis">
              between {{ alert.affectedContexts.join(' and ') }}
            </span>
          </div>

          <div
            v-if="alert.recommendation"
            class="d-flex align-start gap-x-2 mt-1"
          >
            <VIcon
              icon="tabler-bulb"
              size="16"
              color="primary"
              class="mt-1"
            />
            <span class="text-body-2 text-medium-emphasis">
              {{ alert.recommendation }}
            </span>
          </div>
        </VAlert>
      </div>

      <div
        v-else-if="!loading && !error"
        class="d-flex flex-column align-center justify-center py-8"
      >
        <VIcon
          icon="tabler-check"
          color="success"
          size="40"
          class="mb-2"
        />
        <span class="text-body-1 text-disabled">No equity alerts — all groups within 10% mastery threshold</span>
      </div>
    </VCardText>
  </VCard>
</template>
