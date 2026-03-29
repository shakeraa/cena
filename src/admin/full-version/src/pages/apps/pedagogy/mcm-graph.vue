<script setup lang="ts">
import { $api } from '@/utils/api'

definePage({
  meta: {
    action: 'read',
    subject: 'Pedagogy',
  },
})

// --- Interfaces ---
interface McmEdge {
  errorType: string
  conceptCategory: string
  methodology: string
  confidence: number
}

interface McmGraphResponse {
  edges: McmEdge[]
  errorTypes: string[]
  conceptCategories: string[]
}

interface CellData {
  methodology: string
  confidence: number
  errorType: string
  conceptCategory: string
  editing: boolean
  editValue: number
  saving: boolean
}

// --- State ---
const loading = ref(true)
const error = ref<string | null>(null)
const errorTypes = ref<string[]>([])
const conceptCategories = ref<string[]>([])
const cellMap = ref<Record<string, CellData>>({})

const cellKey = (errorType: string, category: string) =>
  `${errorType}::${category}`

const fetchGraph = async () => {
  loading.value = true
  error.value = null
  try {
    const data = await $api<McmGraphResponse>('/admin/pedagogy/mcm-graph')

    errorTypes.value = data.errorTypes
    conceptCategories.value = data.conceptCategories

    const map: Record<string, CellData> = {}
    for (const edge of data.edges) {
      map[cellKey(edge.errorType, edge.conceptCategory)] = {
        methodology: edge.methodology,
        confidence: edge.confidence,
        errorType: edge.errorType,
        conceptCategory: edge.conceptCategory,
        editing: false,
        editValue: edge.confidence,
        saving: false,
      }
    }
    cellMap.value = map
  }
  catch (err: any) {
    error.value = err.message ?? 'Failed to load MCM graph'
    console.error('Failed to fetch MCM graph:', err)
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchGraph)

// --- Ability check for inline editing ---
const { proxy } = getCurrentInstance()!
const canManage = computed(() => proxy?.$can?.('manage', 'Pedagogy') ?? false)

// --- Cell helpers ---
const getCell = (errorType: string, category: string): CellData | undefined =>
  cellMap.value[cellKey(errorType, category)]

const confidenceColor = (val: number): string => {
  if (val >= 0.8) return 'success'
  if (val >= 0.5) return 'info'
  if (val >= 0.3) return 'warning'
  return 'error'
}

// --- Inline edit ---
const startEdit = (cell: CellData) => {
  if (!canManage.value) return
  cell.editing = true
  cell.editValue = cell.confidence
}

const cancelEdit = (cell: CellData) => {
  cell.editing = false
  cell.editValue = cell.confidence
}

const saveEdit = async (cell: CellData) => {
  const newConfidence = Math.max(0, Math.min(1, cell.editValue))
  if (newConfidence === cell.confidence) {
    cell.editing = false
    return
  }

  cell.saving = true
  try {
    await $api('/admin/pedagogy/mcm-graph/edge', {
      method: 'PUT',
      body: {
        errorType: cell.errorType,
        conceptCategory: cell.conceptCategory,
        methodology: cell.methodology,
        confidence: newConfidence,
      },
    })
    cell.confidence = newConfidence
    cell.editing = false
  }
  catch (err: any) {
    console.error('Failed to save MCM edge:', err)
    error.value = err.message ?? 'Failed to save confidence score'
  }
  finally {
    cell.saving = false
  }
}
</script>

<template>
  <div>
    <div class="d-flex align-center justify-space-between flex-wrap gap-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">
          MCM Graph
        </h4>
        <p class="text-body-1 text-medium-emphasis mb-0">
          Methodology-Concept Mapping: which methodology the MCM graph selects per error type and concept category
        </p>
      </div>
      <VChip
        v-if="canManage"
        color="primary"
        variant="tonal"
        label
        size="small"
      >
        <VIcon
          icon="tabler-edit"
          size="14"
          start
        />
        Click cells to edit confidence
      </VChip>
    </div>

    <VAlert
      v-if="error"
      type="error"
      variant="tonal"
      class="mb-6"
      closable
      @click:close="error = null"
    >
      {{ error }}
    </VAlert>

    <VCard :loading="loading">
      <VCardText>
        <div
          v-if="loading"
          class="d-flex align-center justify-center"
          style="min-height: 300px;"
        >
          <VProgressCircular
            indeterminate
            color="primary"
          />
        </div>

        <div
          v-else-if="errorTypes.length === 0"
          class="d-flex align-center justify-center"
          style="min-height: 300px;"
        >
          <span class="text-disabled">No MCM data available</span>
        </div>

        <div
          v-else
          class="mcm-table-wrapper"
        >
          <VTable
            density="comfortable"
            class="text-no-wrap"
            role="grid"
          >
            <thead>
              <tr>
                <th class="text-body-1 font-weight-bold text-uppercase">
                  Error Type
                </th>
                <th
                  v-for="cat in conceptCategories"
                  :key="cat"
                  class="text-body-2 font-weight-bold text-center"
                >
                  {{ cat }}
                </th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="et in errorTypes"
                :key="et"
              >
                <td class="text-body-2 font-weight-medium">
                  {{ et }}
                </td>
                <td
                  v-for="cat in conceptCategories"
                  :key="cat"
                  class="text-center mcm-cell"
                  :class="{ 'mcm-cell--editable': canManage && getCell(et, cat) }"
                  role="gridcell"
                  :tabindex="getCell(et, cat) ? 0 : undefined"
                  :aria-label="getCell(et, cat) ? `${et} to ${cat}: ${getCell(et, cat)!.methodology}, confidence ${(getCell(et, cat)!.confidence * 100).toFixed(0)}%` : `${et} to ${cat}: no mapping`"
                  @click="canManage && getCell(et, cat) && !getCell(et, cat)!.editing ? startEdit(getCell(et, cat)!) : undefined"
                  @keydown.enter="canManage && getCell(et, cat) && !getCell(et, cat)!.editing ? startEdit(getCell(et, cat)!) : undefined"
                >
                  <template v-if="getCell(et, cat)">
                    <!-- Editing mode -->
                    <div
                      v-if="getCell(et, cat)!.editing"
                      class="d-flex align-center justify-center gap-x-1"
                    >
                      <VTextField
                        v-model.number="getCell(et, cat)!.editValue"
                        type="number"
                        density="compact"
                        variant="outlined"
                        hide-details
                        :min="0"
                        :max="1"
                        step="0.01"
                        style="max-inline-size: 80px;"
                        :loading="getCell(et, cat)!.saving"
                        @keydown.enter="saveEdit(getCell(et, cat)!)"
                        @keydown.escape="cancelEdit(getCell(et, cat)!)"
                      />
                      <VBtn
                        icon
                        variant="text"
                        color="success"
                        size="x-small"
                        :loading="getCell(et, cat)!.saving"
                        @click.stop="saveEdit(getCell(et, cat)!)"
                      >
                        <VIcon
                          icon="tabler-check"
                          size="16"
                        />
                      </VBtn>
                      <VBtn
                        icon
                        variant="text"
                        color="secondary"
                        size="x-small"
                        @click.stop="cancelEdit(getCell(et, cat)!)"
                      >
                        <VIcon
                          icon="tabler-x"
                          size="16"
                        />
                      </VBtn>
                    </div>

                    <!-- Read mode -->
                    <div v-else>
                      <div class="text-body-2 font-weight-medium">
                        {{ getCell(et, cat)!.methodology }}
                      </div>
                      <VChip
                        :color="confidenceColor(getCell(et, cat)!.confidence)"
                        variant="tonal"
                        label
                        size="x-small"
                      >
                        {{ (getCell(et, cat)!.confidence * 100).toFixed(0) }}%
                      </VChip>
                    </div>
                  </template>
                  <span
                    v-else
                    class="text-disabled"
                  >--</span>
                </td>
              </tr>
            </tbody>
          </VTable>
        </div>
      </VCardText>
    </VCard>
  </div>
</template>

<style lang="scss" scoped>
.mcm-table-wrapper {
  overflow-x: auto;
}

.mcm-cell {
  padding: 8px 12px;
  min-inline-size: 120px;
  vertical-align: middle;
}

.mcm-cell--editable {
  cursor: pointer;
  border-radius: 4px;

  &:hover {
    background: rgba(var(--v-theme-primary), 0.04);
  }
}
</style>
