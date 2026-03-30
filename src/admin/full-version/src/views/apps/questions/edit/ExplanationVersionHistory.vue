<script setup lang="ts">
import { $api } from '@/utils/api'

interface Props {
  questionId: string
  history: any[]
}

const props = defineProps<Props>()
const emit = defineEmits<{ restored: [] }>()

const showRestoreDialog = ref(false)
const showViewDialog = ref(false)
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

const getExplanationText = (evt: any): string => {
  return evt.data?.NewExplanation ?? evt.explanation ?? evt.details ?? ''
}

const formatDate = (timestamp: string | undefined): string => {
  if (!timestamp) return ''
  return new Date(timestamp).toLocaleDateString('en-US', {
    month: 'short', day: 'numeric', year: 'numeric',
    hour: '2-digit', minute: '2-digit',
  })
}

const openView = (version: any) => {
  selectedVersion.value = version
  showViewDialog.value = true
}

const openRestore = (version: any) => {
  selectedVersion.value = version
  showViewDialog.value = false
  showRestoreDialog.value = true
}

const confirmRestore = async () => {
  if (!selectedVersion.value) return
  isRestoring.value = true
  try {
    await $api(`/admin/questions/${props.questionId}/explanation`, {
      method: 'PATCH',
      body: { explanation: getExplanationText(selectedVersion.value) },
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

const resolveChangeType = (evt: any): string => {
  const type = evt.eventType?.toLowerCase() ?? ''
  if (type.includes('edited') || type.includes('updated')) return 'Updated'
  if (type.includes('created') || type.includes('authored') || type.includes('generated') || type.includes('ingested')) return 'Created'
  if (type.includes('restored')) return 'Restored'
  return evt.eventType?.replace(/_V\d+$/, '') ?? 'Change'
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
                {{ resolveChangeType(evt) }}
              </VChip>
              <span class="text-caption text-disabled">
                {{ formatDate(evt.timestamp) }}
              </span>
              <span v-if="evt.data?.Editor || evt.data?.UserId" class="text-caption text-disabled">
                by {{ evt.data.Editor || evt.data.UserId }}
              </span>
            </div>

            <div
              v-if="getExplanationText(evt)"
              class="text-body-2 text-medium-emphasis mt-1"
            >
              {{ truncate(getExplanationText(evt), 200) }}
            </div>

            <div class="d-flex gap-2 mt-2">
              <VBtn
                size="small"
                variant="tonal"
                color="secondary"
                prepend-icon="tabler-eye"
                @click="openView(evt)"
              >
                View
              </VBtn>
              <VBtn
                size="small"
                variant="tonal"
                color="primary"
                prepend-icon="tabler-restore"
                @click="openRestore(evt)"
              >
                Restore
              </VBtn>
            </div>
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

  <!-- View full text dialog -->
  <VDialog
    v-model="showViewDialog"
    max-width="640"
  >
    <VCard>
      <VCardTitle class="d-flex align-center gap-2">
        <VIcon icon="tabler-file-text" size="20" />
        Explanation — {{ formatDate(selectedVersion?.timestamp) }}
      </VCardTitle>
      <VCardText>
        <div class="pa-4 rounded bg-surface-variant text-body-1" style="white-space: pre-wrap; max-block-size: 400px; overflow-y: auto;">
          {{ getExplanationText(selectedVersion) || 'No explanation text available' }}
        </div>
      </VCardText>
      <VCardActions>
        <VBtn
          variant="tonal"
          color="primary"
          prepend-icon="tabler-restore"
          @click="openRestore(selectedVersion)"
        >
          Restore This Version
        </VBtn>
        <VSpacer />
        <VBtn variant="tonal" @click="showViewDialog = false">
          Close
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Restore confirmation dialog -->
  <VDialog
    v-model="showRestoreDialog"
    max-width="480"
  >
    <VCard>
      <VCardTitle>Restore Explanation</VCardTitle>
      <VCardText>
        <p class="text-body-1 mb-3">
          Restore explanation from {{ formatDate(selectedVersion?.timestamp) }}?
        </p>
        <div
          v-if="selectedVersion"
          class="pa-3 rounded bg-surface-variant text-body-2"
        >
          {{ truncate(getExplanationText(selectedVersion), 300) }}
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
