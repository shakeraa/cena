<script setup lang="ts">
import { $api } from '@/utils/api'

interface Props {
  questionId: string
  history: any[]
}

const props = defineProps<Props>()
const emit = defineEmits<{ restored: [] }>()

const showRestoreDialog = ref(false)
const selectedVersion = ref<any>(null)
const isRestoring = ref(false)

// Filter history to explanation-related events
const explanationEvents = computed(() =>
  props.history.filter((e: any) =>
    e.eventType?.toLowerCase().includes('explanation')
    || e.action?.toLowerCase().includes('explanation')
    || e.details?.toLowerCase().includes('explanation'),
  ),
)

const openRestore = (version: any) => {
  selectedVersion.value = version
  showRestoreDialog.value = true
}

const confirmRestore = async () => {
  if (!selectedVersion.value) return
  isRestoring.value = true
  try {
    await $api(`/admin/questions/${props.questionId}/explanation`, {
      method: 'PATCH',
      body: { text: selectedVersion.value.explanation ?? selectedVersion.value.details },
    })
    showRestoreDialog.value = false
    emit('restored')
  }
  catch (err) {
    console.error('Restore failed:', err)
  }
  finally {
    isRestoring.value = false
  }
}

const resolveEventColor = (evt: any): string => {
  const type = evt.eventType?.toLowerCase() ?? ''
  if (type.includes('edited') || type.includes('updated'))
    return 'info'
  if (type.includes('created') || type.includes('authored'))
    return 'primary'
  if (type.includes('restored'))
    return 'success'
  return 'secondary'
}

const truncate = (text: string | undefined, max: number): string => {
  if (!text) return ''
  return text.length > max ? `${text.slice(0, max)}...` : text
}
</script>

<template>
  <VExpansionPanels>
    <VExpansionPanel>
      <VExpansionPanelTitle>
        <div class="d-flex align-center gap-2">
          <VIcon icon="tabler-history" size="20" />
          <span class="text-body-1 font-weight-medium">Explanation Version History</span>
          <VChip
            v-if="explanationEvents.length"
            size="x-small"
            color="primary"
            label
          >
            {{ explanationEvents.length }}
          </VChip>
        </div>
      </VExpansionPanelTitle>

      <VExpansionPanelText>
        <VTimeline
          v-if="explanationEvents.length"
          density="compact"
          side="end"
          truncate-line="both"
        >
          <VTimelineItem
            v-for="(evt, idx) in explanationEvents"
            :key="idx"
            :dot-color="resolveEventColor(evt)"
            size="small"
          >
            <div class="d-flex align-center flex-wrap gap-2 mb-1">
              <VChip
                size="x-small"
                :color="resolveEventColor(evt)"
                label
              >
                {{ evt.eventType ?? evt.action ?? 'Explanation Change' }}
              </VChip>
              <span class="text-caption text-disabled">
                {{ evt.timestamp ? new Date(evt.timestamp).toLocaleDateString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }) : '' }}
              </span>
            </div>

            <div
              v-if="evt.explanation || evt.details"
              class="text-body-2 text-medium-emphasis mt-1"
            >
              {{ truncate(evt.explanation ?? evt.details, 200) }}
            </div>

            <div
              v-if="evt.data?.NewExplanation"
              class="text-body-2 text-medium-emphasis mt-1"
            >
              {{ truncate(evt.data.NewExplanation, 200) }}
            </div>

            <VBtn
              size="small"
              variant="tonal"
              color="primary"
              class="mt-2"
              @click="openRestore(evt)"
            >
              Restore
            </VBtn>
          </VTimelineItem>
        </VTimeline>

        <div
          v-else
          class="text-center py-6 text-disabled"
        >
          No explanation history available
        </div>
      </VExpansionPanelText>
    </VExpansionPanel>
  </VExpansionPanels>

  <!-- Restore confirmation dialog -->
  <VDialog
    v-model="showRestoreDialog"
    max-width="480"
  >
    <VCard>
      <VCardTitle>Restore Explanation</VCardTitle>
      <VCardText>
        <p class="text-body-1 mb-3">
          Are you sure you want to restore this explanation version? This will replace the current explanation text.
        </p>
        <div
          v-if="selectedVersion"
          class="pa-3 rounded bg-surface-variant text-body-2"
        >
          {{ truncate(selectedVersion.explanation ?? selectedVersion.details ?? selectedVersion.data?.NewExplanation, 300) }}
        </div>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn
          variant="tonal"
          @click="showRestoreDialog = false"
        >
          Cancel
        </VBtn>
        <VBtn
          color="primary"
          :loading="isRestoring"
          @click="confirmRestore"
        >
          Restore
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
