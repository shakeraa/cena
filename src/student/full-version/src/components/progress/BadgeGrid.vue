<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Badge, BadgeListResponse, BadgeTier } from '@/api/types/common'

interface Props {
  badges: BadgeListResponse
}

const props = defineProps<Props>()
const { t } = useI18n()

const earnedCount = computed(() => props.badges.earned.length)
const totalCount = computed(() => props.badges.earned.length + props.badges.locked.length)

const tierColor: Record<BadgeTier, string> = {
  bronze: 'amber-darken-2',
  silver: 'grey-lighten-1',
  gold: 'yellow-darken-2',
  platinum: 'cyan-lighten-2',
}

function colorFor(badge: Badge, locked: boolean): string {
  return locked ? 'grey-darken-2' : tierColor[badge.tier]
}
</script>

<template>
  <VCard
    class="badge-grid pa-5"
    variant="outlined"
    data-testid="badge-grid"
  >
    <div class="d-flex align-center justify-space-between mb-4">
      <div class="text-h6">
        {{ t('gamification.badges.title') }}
      </div>
      <VChip
        size="small"
        variant="tonal"
        data-testid="badge-counter"
      >
        {{ t('gamification.badges.counter', { earned: earnedCount, total: totalCount }) }}
      </VChip>
    </div>

    <div
      v-if="badges.earned.length > 0"
      class="mb-5"
    >
      <div class="text-subtitle-2 text-medium-emphasis mb-3">
        {{ t('gamification.badges.earnedHeading') }}
      </div>
      <div class="badge-grid__row">
        <div
          v-for="badge in badges.earned"
          :key="badge.badgeId"
          class="badge-grid__tile"
          :data-testid="`badge-earned-${badge.badgeId}`"
        >
          <VAvatar
            :color="colorFor(badge, false)"
            size="56"
            class="mb-2"
          >
            <VIcon
              :icon="badge.iconName"
              size="28"
              color="white"
              aria-hidden="true"
            />
          </VAvatar>
          <div class="text-caption font-weight-medium text-high-emphasis">
            {{ badge.name }}
          </div>
          <div class="text-caption text-medium-emphasis">
            {{ t(`gamification.badges.tier.${badge.tier}`) }}
          </div>
        </div>
      </div>
    </div>

    <div v-if="badges.locked.length > 0">
      <div class="text-subtitle-2 text-medium-emphasis mb-3">
        {{ t('gamification.badges.lockedHeading') }}
      </div>
      <div class="badge-grid__row">
        <div
          v-for="badge in badges.locked"
          :key="badge.badgeId"
          class="badge-grid__tile badge-grid__tile--locked"
          :data-testid="`badge-locked-${badge.badgeId}`"
        >
          <VAvatar
            :color="colorFor(badge, true)"
            size="56"
            class="mb-2"
          >
            <VIcon
              icon="tabler-lock"
              size="28"
              color="white"
              aria-hidden="true"
            />
          </VAvatar>
          <div class="text-caption font-weight-medium text-medium-emphasis">
            {{ badge.name }}
          </div>
          <div class="text-caption text-medium-emphasis">
            {{ badge.description }}
          </div>
        </div>
      </div>
    </div>
  </VCard>
</template>

<style scoped>
.badge-grid__row {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(130px, 1fr));
  gap: 1rem;
}

.badge-grid__tile {
  text-align: center;
  padding: 0.5rem;
}

.badge-grid__tile--locked {
  opacity: 0.65;
}
</style>
