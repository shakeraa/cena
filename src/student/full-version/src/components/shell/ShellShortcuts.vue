<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import CommandPalette from './CommandPalette.vue'
import KeyboardShortcutCheatsheet from './KeyboardShortcutCheatsheet.vue'
import { useShortcut } from '@/composables/useShortcut'

/**
 * Singleton mounted in the default layout. Registers the global shortcut
 * set + hosts the <CommandPalette> and <KeyboardShortcutCheatsheet> dialogs.
 *
 * Wired in STU-W-15 Phase A.
 */

const router = useRouter()

const paletteOpen = ref(false)
const cheatsheetOpen = ref(false)

useShortcut([
  {
    id: 'global.palette',
    keys: 'cmd+k',
    label: 'Open command palette',
    scope: 'global',
    blockInInputs: false,
    handler: () => {
      paletteOpen.value = true
    },
  },
  {
    id: 'global.cheatsheet',
    keys: '?',
    label: 'Show keyboard shortcuts',
    scope: 'global',
    blockInInputs: false,
    handler: () => {
      cheatsheetOpen.value = true
    },
  },
  {
    id: 'global.escape',
    keys: 'escape',
    label: 'Close modals and menus',
    scope: 'global',
    blockInInputs: false,
    handler: () => {
      paletteOpen.value = false
      cheatsheetOpen.value = false
    },
  },
  {
    id: 'global.go.home',
    keys: 'g h',
    label: 'Go to Home',
    scope: 'global',
    handler: () => router.push('/home'),
  },
  {
    id: 'global.go.session',
    keys: 'g s',
    label: 'Go to Session',
    scope: 'global',
    handler: () => router.push('/session'),
  },
  {
    id: 'global.go.progress',
    keys: 'g p',
    label: 'Go to Progress',
    scope: 'global',
    handler: () => router.push('/progress'),
  },
  {
    id: 'global.go.tutor',
    keys: 'g t',
    label: 'Go to Tutor',
    scope: 'global',
    handler: () => router.push('/tutor'),
  },
  {
    id: 'global.go.knowledge',
    keys: 'g k',
    label: 'Go to Knowledge Graph',
    scope: 'global',
    handler: () => router.push('/knowledge-graph'),
  },
  {
    id: 'global.go.leaderboard',
    keys: 'g l',
    label: 'Go to Leaderboard',
    scope: 'global',
    handler: () => router.push('/social/leaderboard'),
  },
  {
    id: 'global.go.notifications',
    keys: 'g n',
    label: 'Go to Notifications',
    scope: 'global',
    handler: () => router.push('/notifications'),
  },
])
</script>

<template>
  <div
    class="shell-shortcuts"
    data-testid="shell-shortcuts"
  >
    <CommandPalette v-model="paletteOpen" />
    <KeyboardShortcutCheatsheet v-model="cheatsheetOpen" />
  </div>
</template>
