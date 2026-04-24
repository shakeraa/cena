<script setup lang="ts">
import { $api } from '@/utils/api'

definePage({ meta: { action: 'read', subject: 'Outreach' } })

// ── Types ──

interface ChannelStats {
  channel: string
  sentToday: number
  deliveryRate: number
  responseRate: number
}

interface OverviewStats {
  totalSentToday: number
  budgetExhaustionRate: number
  reEngagementRate: number
  channels: ChannelStats[]
}

interface TriggerVolume {
  date: string
  triggers: Record<string, number> // triggerType → count
}

interface SendTimeCell {
  hour: number
  day: string
  responseRate: number
}

// ── State ──

const loading = ref(true)
const error = ref<string | null>(null)

const overview = ref<OverviewStats | null>(null)
const channelData = ref<ChannelStats[]>([])
const triggerVolume = ref<TriggerVolume[]>([])
const sendTimeData = ref<SendTimeCell[]>([])

// ── Fetch ──

const fetchOverview = async () => {
  try {
    const data = await $api<OverviewStats>('/admin/outreach/overview')
    overview.value = data
    channelData.value = data.channels ?? []
  }
  catch (err: any) {
    console.error('Failed to fetch outreach overview:', err)
    error.value = err.message ?? 'Failed to load outreach overview'
  }
}

const fetchByChannel = async () => {
  try {
    const data = await $api<{ channels: ChannelStats[] }>('/admin/outreach/by-channel')
    channelData.value = data.channels ?? []
  }
  catch (err: any) {
    console.error('Failed to fetch channel data:', err)
    error.value = err.message ?? 'Failed to load channel data'
  }
}

const fetchByTrigger = async () => {
  try {
    const data = await $api<{ series: TriggerVolume[] }>('/admin/outreach/by-trigger')
    triggerVolume.value = data.series ?? []
  }
  catch (err: any) {
    console.error('Failed to fetch trigger volume:', err)
    error.value = err.message ?? 'Failed to load trigger volume'
  }
}

const fetchSendTimes = async () => {
  try {
    const data = await $api<{ cells: SendTimeCell[] }>('/admin/outreach/send-times')
    sendTimeData.value = data.cells ?? []
  }
  catch (err: any) {
    console.error('Failed to fetch send time data:', err)
    error.value = err.message ?? 'Failed to load send time heatmap'
  }
}

const fetchAll = async () => {
  loading.value = true
  error.value = null
  await Promise.all([
    fetchOverview(),
    fetchByChannel(),
    fetchByTrigger(),
    fetchSendTimes(),
  ])
  loading.value = false
}

onMounted(fetchAll)

// ── Chart theme tokens ──

const labelColor = 'rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity))'
const borderColor = 'rgba(var(--v-border-color), var(--v-border-opacity))'

// ── Widget cards ──

const channelIcons: Record<string, string> = {
  WhatsApp: 'tabler-brand-whatsapp',
  Telegram: 'tabler-brand-telegram',
  Push: 'tabler-bell-ringing',
  Voice: 'tabler-phone-call',
}

const channelColors: Record<string, string> = {
  WhatsApp: '#25D366',
  Telegram: '#0088CC',
  Push: '#7367F0',
  Voice: '#FF9F43',
}

const widgetCards = computed(() => {
  const cards = (channelData.value ?? []).map(ch => ({
    icon: channelIcons[ch.channel] ?? 'tabler-send',
    color: channelColors[ch.channel] ?? 'primary',
    title: ch.channel,
    value: String(ch.sentToday),
    subtitle: `${ch.deliveryRate}% delivered`,
    isHover: false,
  }))

  return cards
})

const budgetCard = computed(() => ({
  icon: 'tabler-currency-shekel',
  color: (overview.value?.budgetExhaustionRate ?? 0) > 80 ? 'error' : 'warning',
  title: 'Budget Exhaustion',
  value: `${overview.value?.budgetExhaustionRate ?? 0}%`,
  subtitle: 'of daily notification budget used',
  isHover: false,
}))

const reEngagementCard = computed(() => ({
  icon: 'tabler-users-group',
  color: 'success',
  title: 'Re-engagement Rate',
  value: `${overview.value?.reEngagementRate ?? 0}%`,
  subtitle: 'students returned after notification',
  isHover: false,
}))

// ── Channel Effectiveness Bar Chart ──

const channelBarOptions = computed(() => ({
  chart: { type: 'bar' as const, parentHeightOffset: 0, toolbar: { show: false } },
  plotOptions: {
    bar: { columnWidth: '50%', borderRadius: 4, borderRadiusApplication: 'end' as const },
  },
  colors: channelData.value.map(ch => channelColors[ch.channel] ?? '#82868B'),
  dataLabels: { enabled: true, formatter: (val: number) => `${val}%` },
  legend: { show: false },
  grid: { strokeDashArray: 8, borderColor, padding: { bottom: -10 } },
  xaxis: {
    categories: channelData.value.map(ch => ch.channel),
    labels: { style: { colors: labelColor, fontSize: '13px', fontWeight: 400 } },
    axisBorder: { show: false },
    axisTicks: { show: false },
  },
  yaxis: {
    min: 0,
    max: 100,
    labels: { style: { colors: labelColor, fontSize: '13px', fontWeight: 400 } },
    title: { text: 'Response Rate (%)', style: { color: labelColor } },
  },
  tooltip: { y: { formatter: (val: number) => `${val}%` } },
}))

const channelBarSeries = computed(() => [
  { name: 'Response Rate', data: channelData.value.map(ch => ch.responseRate) },
])

// ── Trigger Volume Stacked Area Chart ──

const triggerTypes = computed(() => {
  const set = new Set<string>()
  triggerVolume.value.forEach(tv => {
    Object.keys(tv.triggers).forEach(k => set.add(k))
  })
  return Array.from(set)
})

const triggerColors = ['#7367F0', '#28C76F', '#FF9F43', '#EA5455', '#00CFE8', '#82868B']

const triggerAreaOptions = computed(() => ({
  chart: {
    type: 'area' as const,
    parentHeightOffset: 0,
    stacked: true,
    toolbar: { show: false },
  },
  colors: triggerColors.slice(0, triggerTypes.value.length),
  dataLabels: { enabled: false },
  stroke: { curve: 'smooth' as const, width: 2 },
  fill: { type: 'gradient', gradient: { opacityFrom: 0.5, opacityTo: 0.1 } },
  legend: {
    show: true,
    position: 'top' as const,
    labels: { colors: labelColor },
  },
  grid: { strokeDashArray: 8, borderColor, padding: { bottom: -10 } },
  xaxis: {
    categories: triggerVolume.value.map(tv => tv.date),
    labels: { style: { colors: labelColor, fontSize: '13px', fontWeight: 400 } },
    axisBorder: { show: false },
    axisTicks: { show: false },
  },
  yaxis: {
    labels: { style: { colors: labelColor, fontSize: '13px', fontWeight: 400 } },
    title: { text: 'Notifications', style: { color: labelColor } },
  },
  tooltip: { shared: true, intersect: false },
}))

const triggerAreaSeries = computed(() =>
  triggerTypes.value.map(t => ({
    name: t,
    data: triggerVolume.value.map(tv => tv.triggers[t] ?? 0),
  })),
)

// ── Optimal Send Time Heatmap ──

const days = computed(() => {
  const set = new Set<string>()
  sendTimeData.value.forEach(c => set.add(c.day))
  return Array.from(set)
})

const hours = computed(() => {
  const set = new Set<number>()
  sendTimeData.value.forEach(c => set.add(c.hour))
  return Array.from(set).sort((a, b) => a - b)
})

const sendTimeMap = computed(() => {
  const map = new Map<string, number>()
  sendTimeData.value.forEach(c => {
    map.set(`${c.day}-${c.hour}`, c.responseRate)
  })
  return map
})

const getResponseRate = (day: string, hour: number): number => {
  return sendTimeMap.value.get(`${day}-${hour}`) ?? 0
}

const heatColor = (rate: number): string => {
  if (rate >= 30) return '#4CAF50'
  if (rate >= 20) return '#8BC34A'
  if (rate >= 10) return '#FFC107'
  if (rate >= 5) return '#FF9800'
  return '#F44336'
}

const heatTextColor = (rate: number): string => {
  if (rate >= 20) return '#1a1a1a'
  return '#ffffff'
}

const formatHour = (h: number): string => {
  if (h === 0) return '12am'
  if (h < 12) return `${h}am`
  if (h === 12) return '12pm'
  return `${h - 12}pm`
}
</script>

<template>
  <div>
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">
          Outreach & Engagement
        </h4>
        <div class="text-body-1">
          Notification delivery, channel performance, and engagement analytics
        </div>
      </div>
    </div>

    <VAlert
      v-if="error"
      type="error"
      variant="tonal"
      class="mb-6"
    >
      {{ error }}
    </VAlert>

    <!-- Channel stat cards + budget + re-engagement -->
    <VRow class="mb-2">
      <VCol
        v-for="(card, index) in widgetCards"
        :key="index"
        cols="12"
        sm="6"
        md="3"
      >
        <VCard
          class="outreach-stat-card"
          :loading="loading"
          :style="card.isHover
            ? `border-block-end-color: ${card.color}`
            : `border-block-end-color: ${card.color}66`"
          @mouseenter="card.isHover = true"
          @mouseleave="card.isHover = false"
        >
          <VCardText>
            <div class="d-flex align-center gap-x-4 mb-1">
              <VAvatar
                variant="tonal"
                color="primary"
                rounded
                :style="{ backgroundColor: `${card.color}22`, color: card.color }"
              >
                <VIcon
                  :icon="card.icon"
                  size="28"
                />
              </VAvatar>
              <h4 class="text-h4">
                {{ card.value }}
              </h4>
            </div>
            <div class="text-body-1 mb-1">
              {{ card.title }}
            </div>
            <span class="text-sm text-disabled">{{ card.subtitle }}</span>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Budget Exhaustion -->
      <VCol
        cols="12"
        sm="6"
        md="3"
      >
        <VCard
          class="outreach-stat-card"
          :loading="loading"
          :style="budgetCard.isHover
            ? `border-block-end-color: rgb(var(--v-theme-${budgetCard.color}))`
            : `border-block-end-color: rgba(var(--v-theme-${budgetCard.color}),0.38)`"
          @mouseenter="budgetCard.isHover = true"
          @mouseleave="budgetCard.isHover = false"
        >
          <VCardText>
            <div class="d-flex align-center gap-x-4 mb-1">
              <VAvatar
                variant="tonal"
                :color="budgetCard.color"
                rounded
              >
                <VIcon
                  :icon="budgetCard.icon"
                  size="28"
                />
              </VAvatar>
              <h4 class="text-h4">
                {{ budgetCard.value }}
              </h4>
            </div>
            <div class="text-body-1 mb-1">
              {{ budgetCard.title }}
            </div>
            <span class="text-sm text-disabled">{{ budgetCard.subtitle }}</span>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Re-engagement Rate -->
      <VCol
        cols="12"
        sm="6"
        md="3"
      >
        <VCard
          class="outreach-stat-card"
          :loading="loading"
          :style="reEngagementCard.isHover
            ? `border-block-end-color: rgb(var(--v-theme-${reEngagementCard.color}))`
            : `border-block-end-color: rgba(var(--v-theme-${reEngagementCard.color}),0.38)`"
          @mouseenter="reEngagementCard.isHover = true"
          @mouseleave="reEngagementCard.isHover = false"
        >
          <VCardText>
            <div class="d-flex align-center gap-x-4 mb-1">
              <VAvatar
                variant="tonal"
                :color="reEngagementCard.color"
                rounded
              >
                <VIcon
                  :icon="reEngagementCard.icon"
                  size="28"
                />
              </VAvatar>
              <h4 class="text-h4">
                {{ reEngagementCard.value }}
              </h4>
            </div>
            <div class="text-body-1 mb-1">
              {{ reEngagementCard.title }}
            </div>
            <span class="text-sm text-disabled">{{ reEngagementCard.subtitle }}</span>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Charts Row -->
    <VRow class="match-height mb-2">
      <!-- Channel Effectiveness Bar -->
      <VCol
        cols="12"
        md="5"
      >
        <VCard :loading="loading">
          <VCardItem title="Channel Effectiveness">
            <template #subtitle>
              Response rate by notification channel
            </template>
          </VCardItem>

          <VCardText>
            <VueApexCharts
              v-if="channelBarSeries[0].data.length"
              type="bar"
              height="350"
              :options="channelBarOptions"
              :series="channelBarSeries"
            />

            <div
              v-else-if="!loading"
              class="d-flex align-center justify-center"
              style="min-height: 350px;"
            >
              <span class="text-disabled">No channel data available</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Trigger Volume Stacked Area -->
      <VCol
        cols="12"
        md="7"
      >
        <VCard :loading="loading">
          <VCardItem title="Notification Volume by Trigger">
            <template #subtitle>
              Daily notifications by trigger type (absence, streak-at-risk, re-engagement, scheduled)
            </template>
          </VCardItem>

          <VCardText>
            <VueApexCharts
              v-if="triggerAreaSeries.length && triggerAreaSeries[0].data.length"
              type="area"
              height="350"
              :options="triggerAreaOptions"
              :series="triggerAreaSeries"
            />

            <div
              v-else-if="!loading"
              class="d-flex align-center justify-center"
              style="min-height: 350px;"
            >
              <span class="text-disabled">No trigger volume data available</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Optimal Send Time Heatmap -->
    <VRow class="mb-2">
      <VCol cols="12">
        <VCard :loading="loading">
          <VCardItem title="Optimal Send Time">
            <template #subtitle>
              Response rate (%) by hour and day of week — darker green = higher engagement
            </template>
          </VCardItem>

          <VCardText>
            <div
              v-if="sendTimeData.length > 0"
              class="heatmap-container"
            >
              <table class="heatmap-table">
                <thead>
                  <tr>
                    <th class="day-header">
                      Day / Hour
                    </th>
                    <th
                      v-for="h in hours"
                      :key="h"
                      class="hour-header"
                    >
                      {{ formatHour(h) }}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="day in days"
                    :key="day"
                  >
                    <td class="day-name">
                      {{ day }}
                    </td>
                    <td
                      v-for="h in hours"
                      :key="h"
                      class="heatmap-cell"
                      :style="{
                        backgroundColor: heatColor(getResponseRate(day, h)),
                        color: heatTextColor(getResponseRate(day, h)),
                      }"
                      :title="`${day} ${formatHour(h)}: ${getResponseRate(day, h)}% response rate`"
                    >
                      {{ getResponseRate(day, h) }}%
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>

            <div
              v-else-if="!loading"
              class="d-flex align-center justify-center py-8"
            >
              <span class="text-disabled">No send time data available</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>
  </div>
</template>

<style lang="scss">
@use "@core/scss/template/libs/apex-chart.scss";
</style>

<style lang="scss" scoped>
@use "@core/scss/base/mixins" as mixins;

.outreach-stat-card {
  border-block-end-style: solid;
  border-block-end-width: 2px;

  &:hover {
    border-block-end-width: 3px;
    margin-block-end: -1px;

    @include mixins.elevation(8);

    transition: all 0.1s ease-out;
  }
}

.skin--bordered {
  .outreach-stat-card {
    border-block-end-width: 2px;

    &:hover {
      border-block-end-width: 3px;
      margin-block-end: -2px;
      transition: all 0.1s ease-out;
    }
  }
}

.heatmap-container {
  overflow-x: auto;
}

.heatmap-table {
  border-collapse: collapse;
  font-size: 0.8125rem;
  inline-size: 100%;

  th,
  td {
    border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
    padding-block: 6px;
    padding-inline: 8px;
    text-align: center;
    white-space: nowrap;
  }

  .day-header,
  .day-name {
    position: sticky;
    background: rgb(var(--v-theme-surface));
    inset-inline-start: 0;
    text-align: start;
    z-index: 1;
  }

  .day-header {
    z-index: 2;
  }

  .hour-header {
    background: rgb(var(--v-theme-surface));
    color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
    font-size: 0.75rem;
    font-weight: 500;
  }

  .heatmap-cell {
    font-size: 0.75rem;
    font-weight: 600;
    min-inline-size: 48px;
    transition: opacity 0.15s;

    &:hover {
      opacity: 0.85;
    }
  }
}
</style>
