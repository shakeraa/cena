<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Shortcut, ShortcutScope } from '@/composables/useShortcut'
import { listShortcuts } from '@/composables/useShortcut'

interface Props {
  modelValue: boolean
}

defineProps<Props>()
const emit = defineEmits<{
  'update:modelValue': [value: boolean]
}>()

const { t } = useI18n()

const grouped = computed(() => {
  const shortcuts = listShortcuts()
  const groups: Record<ShortcutScope, Shortcut[]> = {
    global: [],
    session: [],
    tutor: [],
    graph: [],
    palette: [],
  }

  for (const s of shortcuts) {
    if (!groups[s.scope])
      groups[s.scope] = []
    groups[s.scope].push(s)
  }

  return groups
})

const SCOPE_ORDER: ShortcutScope[] = ['global', 'session', 'tutor', 'graph', 'palette']

function formatKey(keys: string): string[] {
  // Split sequences like "g h" into individual key chips
  if (keys.includes(' '))
    return keys.split(' ')

  return keys.split('+').map(k => (k === 'cmd' ? '⌘' : k))
}

function close() {
  emit('update:modelValue', false)
}
</script>

<template>
  <VDialog
    :model-value="modelValue"
    max-width="700"
    data-testid="shortcut-cheatsheet"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <VCard class="pa-0">
      <div class="d-flex align-center pa-5">
        <VIcon
          icon="tabler-keyboard"
          size="24"
          class="me-3"
          aria-hidden="true"
        />
        <div class="flex-grow-1">
          <div class="text-h5">
            {{ t('shortcuts.title') }}
          </div>
          <div class="text-caption text-medium-emphasis">
            {{ t('shortcuts.subtitle') }}
          </div>
        </div>
        <VBtn
          icon="tabler-x"
          variant="text"
          size="small"
          data-testid="cheatsheet-close"
          :aria-label="t('common.close')"
          @click="close"
        />
      </div>

      <VDivider />

      <div class="pa-5 shortcut-cheatsheet__body">
        <section
          v-for="scope in SCOPE_ORDER"
          :key="scope"
          class="mb-5"
        >
          <div
            v-if="grouped[scope].length > 0"
            :data-testid="`cheatsheet-group-${scope}`"
          >
            <div class="text-subtitle-2 text-medium-emphasis mb-3 text-uppercase">
              {{ t(`shortcuts.scopes.${scope}`) }}
            </div>
            <div
              v-for="s in grouped[scope]"
              :key="s.id"
              class="d-flex align-center justify-space-between mb-2"
              :data-testid="`cheatsheet-item-${s.id}`"
            >
              <div class="text-body-2">
                {{ s.label }}
              </div>
              <div class="d-flex align-center ga-1">
                <VChip
                  v-for="(k, idx) in formatKey(s.keys)"
                  :key="`${s.id}-${idx}`"
                  size="small"
                  variant="outlined"
                  label
                  class="font-mono"
                >
                  {{ k }}
                </VChip>
              </div>
            </div>
          </div>
        </section>

        <div
          v-if="Object.values(grouped).every(g => g.length === 0)"
          class="text-center py-6 text-medium-emphasis"
          data-testid="cheatsheet-empty"
        >
          {{ t('shortcuts.empty') }}
        </div>
      </div>
    </VCard>
  </VDialog>
</template>

<style scoped>
.shortcut-cheatsheet__body {
  max-block-size: 70vh;
  overflow-y: auto;
}

.font-mono {
  font-family: monospace;
}
</style>
