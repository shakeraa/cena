<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { Friend } from '@/api/types/common'

interface Props {
  friend: Friend
}

defineProps<Props>()
const { t } = useI18n()
</script>

<template>
  <VCard
    variant="outlined"
    class="friend-row pa-3 mb-2"
    :data-testid="`friend-${friend.studentId}`"
  >
    <div class="d-flex align-center">
      <VBadge
        :color="friend.isOnline ? 'success' : 'grey'"
        dot
        offset-x="4"
        offset-y="4"
      >
        <VAvatar
          color="primary"
          size="44"
        >
          <VIcon
            icon="tabler-user"
            size="22"
            color="white"
            aria-hidden="true"
          />
        </VAvatar>
      </VBadge>
      <div class="flex-grow-1 min-w-0 ms-3">
        <div class="text-subtitle-1 font-weight-medium text-truncate">
          {{ friend.displayName }}
        </div>
        <div class="d-flex align-center ga-2 text-caption text-medium-emphasis">
          <span>{{ t('social.friends.level', { level: friend.level }) }}</span>
          <span>·</span>
          <span>{{ t('social.friends.streak', friend.streakDays) }}</span>
          <span>·</span>
          <span :class="friend.isOnline ? 'text-success' : ''">
            {{ friend.isOnline ? t('social.friends.online') : t('social.friends.offline') }}
          </span>
        </div>
      </div>
    </div>
  </VCard>
</template>
