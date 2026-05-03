<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { BossBattleSummary } from '@/api/types/common'

interface Props {
  boss: BossBattleSummary
  locked?: boolean
  /** Disables the tile while a /start request is in flight. */
  starting?: boolean
}

const props = withDefaults(defineProps<Props>(), { locked: false, starting: false })
const emit = defineEmits<{
  select: [bossBattleId: string]
}>()

const { t } = useI18n()

function onActivate() {
  if (props.locked || props.starting)
    return
  emit('select', props.boss.bossBattleId)
}

function onKey(event: KeyboardEvent) {
  if (event.key === 'Enter' || event.key === ' ') {
    event.preventDefault()
    onActivate()
  }
}
</script>

<template>
  <VCard
    :data-testid="`boss-${boss.bossBattleId}`"
    :variant="locked ? 'outlined' : 'flat'"
    :color="locked ? undefined : 'surface-variant'"
    class="boss-tile pa-4"
    :class="{ 'boss-tile--locked': locked, 'boss-tile--interactive': !locked }"
    :tabindex="locked ? -1 : 0"
    :role="locked ? 'group' : 'button'"
    :aria-disabled="locked || starting"
    :aria-busy="starting"
    @click="onActivate"
    @keydown="onKey"
  >
    <div class="d-flex align-center">
      <VAvatar
        :color="locked ? 'grey-lighten-2' : 'error'"
        size="48"
        class="me-3"
      >
        <VIcon
          :icon="locked ? 'tabler-lock' : 'tabler-skull'"
          size="24"
          color="white"
          aria-hidden="true"
        />
      </VAvatar>
      <div class="flex-grow-1 min-w-0">
        <div class="text-subtitle-1 font-weight-medium text-truncate">
          {{ boss.name }}
        </div>
        <div class="d-flex align-center text-caption text-medium-emphasis mt-1 ga-2">
          <VChip
            size="x-small"
            variant="tonal"
          >
            {{ boss.subject }}
          </VChip>
          <span>·</span>
          <span>{{ t(`challenges.difficulty.${boss.difficulty}`) }}</span>
          <template v-if="locked">
            <span>·</span>
            <span data-testid="boss-lock-reason">
              {{ t('challenges.boss.requiresLevel', { level: boss.requiredMasteryLevel }) }}
            </span>
          </template>
        </div>
      </div>
      <VIcon
        v-if="!locked"
        icon="tabler-chevron-right"
        class="ms-2 text-medium-emphasis"
        aria-hidden="true"
      />
    </div>
  </VCard>
</template>

<style scoped>
.boss-tile--locked {
  opacity: 0.7;
}

.boss-tile--interactive {
  cursor: pointer;
}

.boss-tile--interactive:focus-visible {
  outline: 2px solid rgb(var(--v-theme-primary));
  outline-offset: 2px;
}
</style>
