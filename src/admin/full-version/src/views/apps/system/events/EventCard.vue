<script setup lang="ts">
interface EventData {
  id: string
  timestamp: string
  type: string
  summary: string
  payload?: Record<string, unknown>
}

interface Props {
  event: EventData
  isExpanded: boolean
}

const props = defineProps<Props>()
const emit = defineEmits<{ (e: 'toggle'): void }>()

const eventTypeConfig: Record<string, { color: string; icon: string }> = {
  ConceptAttempted: { color: 'info', icon: 'tabler-pencil' },
  ConceptMastered: { color: 'success', icon: 'tabler-trophy' },
  StagnationDetected: { color: 'warning', icon: 'tabler-alert-triangle' },
  MethodologySwitched: { color: 'primary', icon: 'tabler-switch-horizontal' },
  SessionStarted: { color: 'secondary', icon: 'tabler-player-play' },
  SessionEnded: { color: 'secondary', icon: 'tabler-player-stop' },
  FocusAlert: { color: 'error', icon: 'tabler-eye-off' },
  MicrobreakTaken: { color: 'success', icon: 'tabler-coffee' },
}

const config = computed(() => eventTypeConfig[props.event.type] ?? { color: 'default', icon: 'tabler-point' })

const formatTime = (ts: string) => {
  const d = new Date(ts)
  return d.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}
</script>

<template>
  <VCard
    variant="outlined"
    class="mb-2 cursor-pointer"
    @click="emit('toggle')"
  >
    <VCardText class="pa-3">
      <div class="d-flex align-center gap-3">
        <VAvatar
          :color="config.color"
          variant="tonal"
          size="32"
        >
          <VIcon
            :icon="config.icon"
            size="18"
          />
        </VAvatar>
        <div class="flex-grow-1">
          <div class="d-flex align-center gap-2">
            <VChip
              :color="config.color"
              size="x-small"
              label
            >
              {{ event.type }}
            </VChip>
            <span class="text-caption text-disabled">{{ formatTime(event.timestamp) }}</span>
          </div>
          <div class="text-body-2 mt-1">
            {{ event.summary }}
          </div>
        </div>
        <VIcon
          :icon="isExpanded ? 'tabler-chevron-up' : 'tabler-chevron-down'"
          size="18"
        />
      </div>
      <VExpandTransition>
        <div v-if="isExpanded && event.payload">
          <VDivider class="my-2" />
          <pre class="text-caption pa-2 bg-grey-lighten-4 rounded" style="overflow-x: auto; max-height: 300px;">{{ JSON.stringify(event.payload, null, 2) }}</pre>
        </div>
      </VExpandTransition>
    </VCardText>
  </VCard>
</template>
