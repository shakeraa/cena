<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useTheme } from 'vuetify'
import { useAuthStore } from '@/stores/authStore'

interface Props {
  modelValue: boolean
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
}>()

const { t } = useI18n()
const router = useRouter()
const vuetifyTheme = useTheme()
const authStore = useAuthStore()

interface Command {
  id: string
  title: string
  subtitle?: string
  icon: string
  category: 'navigate' | 'action'
  keywords?: string[]
  run: () => void
}

const commands = computed<Command[]>(() => {
  const items: Command[] = [
    // Navigate
    { id: 'nav.home', title: t('commandPalette.cmds.home'), icon: 'tabler-home', category: 'navigate', run: () => router.push('/home') },
    { id: 'nav.session', title: t('commandPalette.cmds.startSession'), icon: 'tabler-player-play', category: 'navigate', run: () => router.push('/session') },
    { id: 'nav.progress', title: t('commandPalette.cmds.progress'), icon: 'tabler-chart-line', category: 'navigate', run: () => router.push('/progress') },
    { id: 'nav.tutor', title: t('commandPalette.cmds.tutor'), icon: 'tabler-message-chatbot', category: 'navigate', run: () => router.push('/tutor') },
    { id: 'nav.challenges', title: t('commandPalette.cmds.challenges'), icon: 'tabler-swords', category: 'navigate', run: () => router.push('/challenges') },
    { id: 'nav.knowledge', title: t('commandPalette.cmds.knowledgeGraph'), icon: 'tabler-affiliate', category: 'navigate', run: () => router.push('/knowledge-graph') },
    { id: 'nav.leaderboard', title: t('commandPalette.cmds.leaderboard'), icon: 'tabler-trophy', category: 'navigate', run: () => router.push('/social/leaderboard') },
    { id: 'nav.social', title: t('commandPalette.cmds.social'), icon: 'tabler-users', category: 'navigate', run: () => router.push('/social') },
    { id: 'nav.notifications', title: t('commandPalette.cmds.notifications'), icon: 'tabler-bell', category: 'navigate', run: () => router.push('/notifications') },
    { id: 'nav.profile', title: t('commandPalette.cmds.profile'), icon: 'tabler-user', category: 'navigate', run: () => router.push('/profile') },
    { id: 'nav.settings', title: t('commandPalette.cmds.settings'), icon: 'tabler-settings', category: 'navigate', run: () => router.push('/settings') },

    // Actions
    {
      id: 'act.toggleTheme',
      title: t('commandPalette.cmds.toggleTheme'),
      icon: 'tabler-contrast',
      category: 'action',
      keywords: ['dark', 'light', 'mode'],
      run: () => {
        const next = vuetifyTheme.global.name.value === 'dark' ? 'light' : 'dark'

        vuetifyTheme.global.name.value = next
        if (typeof localStorage !== 'undefined')
          localStorage.setItem('cena-student-theme', next)
      },
    },
    { id: 'act.signOut', title: t('commandPalette.cmds.signOut'), icon: 'tabler-logout', category: 'action', run: () => authStore.__signOut() },
  ]

  return items
})

const query = ref('')
const selectedIndex = ref(0)
const inputRef = ref<HTMLInputElement | null>(null)

const filtered = computed<Command[]>(() => {
  const q = query.value.trim().toLowerCase()
  if (!q)
    return commands.value

  return commands.value.filter(c => {
    const haystack = `${c.title.toLowerCase()} ${c.keywords?.join(' ') ?? ''}`

    return haystack.includes(q)
  })
})

watch(
  () => props.modelValue,
  async next => {
    if (next) {
      query.value = ''
      selectedIndex.value = 0
      await nextTick()
      inputRef.value?.focus()
    }
  },
)

watch(filtered, () => {
  selectedIndex.value = 0
})

function close() {
  emit('update:modelValue', false)
}

function runSelected() {
  const cmd = filtered.value[selectedIndex.value]
  if (cmd) {
    cmd.run()
    close()
  }
}

function handleKeydown(e: KeyboardEvent) {
  if (e.key === 'ArrowDown') {
    e.preventDefault()
    if (filtered.value.length > 0)
      selectedIndex.value = (selectedIndex.value + 1) % filtered.value.length
  }
  else if (e.key === 'ArrowUp') {
    e.preventDefault()
    if (filtered.value.length > 0)
      selectedIndex.value = (selectedIndex.value - 1 + filtered.value.length) % filtered.value.length
  }
  else if (e.key === 'Enter') {
    e.preventDefault()
    runSelected()
  }
  else if (e.key === 'Escape') {
    e.preventDefault()
    close()
  }
}
</script>

<template>
  <VDialog
    :model-value="modelValue"
    max-width="600"
    data-testid="command-palette"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <VCard class="pa-0">
      <div class="command-palette__input-row pa-3 d-flex align-center">
        <VIcon
          icon="tabler-search"
          size="20"
          class="mx-3 text-medium-emphasis"
          aria-hidden="true"
        />
        <input
          ref="inputRef"
          v-model="query"
          type="text"
          :placeholder="t('commandPalette.placeholder')"
          class="command-palette__input flex-grow-1 text-body-1"
          data-testid="command-palette-input"
          @keydown="handleKeydown"
        >
        <VChip
          size="x-small"
          variant="outlined"
          class="mx-2"
        >
          ESC
        </VChip>
      </div>

      <VDivider />

      <VList
        class="pa-0 command-palette__list"
        data-testid="command-palette-list"
      >
        <VListItem
          v-for="(cmd, index) in filtered"
          :key="cmd.id"
          :active="index === selectedIndex"
          :data-testid="`command-${cmd.id}`"
          :data-selected="index === selectedIndex"
          @click="cmd.run(); close()"
        >
          <template #prepend>
            <VIcon
              :icon="cmd.icon"
              size="20"
              class="me-3"
              aria-hidden="true"
            />
          </template>
          <VListItemTitle>{{ cmd.title }}</VListItemTitle>
          <template #append>
            <VChip
              size="x-small"
              variant="tonal"
              :color="cmd.category === 'navigate' ? 'primary' : 'warning'"
            >
              {{ t(`commandPalette.category.${cmd.category}`) }}
            </VChip>
          </template>
        </VListItem>
        <VListItem
          v-if="filtered.length === 0"
          data-testid="command-palette-empty"
        >
          <VListItemTitle class="text-medium-emphasis text-center">
            {{ t('commandPalette.empty') }}
          </VListItemTitle>
        </VListItem>
      </VList>
    </VCard>
  </VDialog>
</template>

<style scoped>
.command-palette__input {
  border: none;
  outline: none;
  background: transparent;
  color: inherit;
  font-size: 1.05rem;
}

.command-palette__list {
  max-block-size: 60vh;
  overflow-y: auto;
}
</style>
