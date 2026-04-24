<script setup lang="ts">
import { $api } from '@/utils/api'

interface ModerationStatItem {
  icon: string
  color: string
  title: string
  value: number
  change: number
  isHover: boolean
}

const statsData = ref<ModerationStatItem[]>([
  { icon: 'tabler-clock', color: 'warning', title: 'Pending', value: 0, change: 0, isHover: false },
  { icon: 'tabler-eye-check', color: 'info', title: 'In Review', value: 0, change: 0, isHover: false },
  { icon: 'tabler-circle-check', color: 'success', title: 'Approved Today', value: 0, change: 0, isHover: false },
  { icon: 'tabler-circle-x', color: 'error', title: 'Rejected Today', value: 0, change: 0, isHover: false },
])

const loading = ref(true)

const fetchStats = async () => {
  loading.value = true
  try {
    const data = await $api('/admin/moderation/stats')

    statsData.value[0].value = data.pending ?? 0
    statsData.value[0].change = data.pendingChange ?? 0
    statsData.value[1].value = data.inReview ?? 0
    statsData.value[1].change = data.inReviewChange ?? 0
    statsData.value[2].value = data.approvedToday ?? 0
    statsData.value[2].change = data.approvedTodayChange ?? 0
    statsData.value[3].value = data.rejectedToday ?? 0
    statsData.value[3].change = data.rejectedTodayChange ?? 0
  }
  catch (error) {
    console.error('Failed to fetch moderation stats:', error)
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchStats)

defineExpose({ refresh: fetchStats })
</script>

<template>
  <VRow>
    <VCol
      v-for="(data, index) in statsData"
      :key="index"
      cols="12"
      md="3"
      sm="6"
    >
      <VCard
        class="moderation-stat-card cursor-pointer"
        :style="data.isHover ? `border-block-end-color: rgb(var(--v-theme-${data.color}))` : `border-block-end-color: rgba(var(--v-theme-${data.color}),0.38)`"
        :loading="loading"
        @mouseenter="data.isHover = true"
        @mouseleave="data.isHover = false"
      >
        <VCardText>
          <div class="d-flex align-center gap-x-4 mb-1">
            <VAvatar
              variant="tonal"
              :color="data.color"
              rounded
            >
              <VIcon
                :icon="data.icon"
                size="28"
              />
            </VAvatar>
            <h4 class="text-h4">
              {{ data.value }}
            </h4>
          </div>
          <div class="text-body-1 mb-1">
            {{ data.title }}
          </div>
          <div class="d-flex gap-x-2 align-center">
            <h6 class="text-h6">
              {{ (data.change > 0) ? '+' : '' }}{{ data.change }}%
            </h6>
            <div class="text-sm text-disabled">
              than yesterday
            </div>
          </div>
        </VCardText>
      </VCard>
    </VCol>
  </VRow>
</template>

<style lang="scss" scoped>
@use "@core/scss/base/mixins" as mixins;

.moderation-stat-card {
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
  .moderation-stat-card {
    border-block-end-width: 2px;

    &:hover {
      border-block-end-width: 3px;
      margin-block-end: -2px;
      transition: all 0.1s ease-out;
    }
  }
}
</style>
