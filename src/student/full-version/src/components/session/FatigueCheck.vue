<script setup lang="ts">
/**
 * RDY-022: Fatigue Check Dialog
 *
 * After N questions (default 10), ask "How are you feeling?"
 * Maps response to backend CognitiveLoadService fatigue assessment.
 * If "tired": suggest break with estimated cooldown.
 *
 * a11y: keyboard-accessible with focus trap (VDialog handles this).
 */
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'

type EnergyLevel = 'energized' | 'okay' | 'tired'

interface Props {
  modelValue: boolean
  /** Cooldown minutes from CognitiveLoadService (shown when tired). */
  cooldownMinutes?: number
}

const props = withDefaults(defineProps<Props>(), {
  cooldownMinutes: 10,
})

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  /** Emitted with the student's self-reported energy level. */
  'energy-reported': [level: EnergyLevel]
  /** Emitted when student chooses to take a break. */
  'take-break': []
}>()

const { t } = useI18n()

const selectedEnergy = ref<EnergyLevel | null>(null)
const showBreakSuggestion = computed(() => selectedEnergy.value === 'tired')

const energyOptions: { value: EnergyLevel; icon: string; color: string }[] = [
  { value: 'energized', icon: 'tabler-bolt', color: 'success' },
  { value: 'okay', icon: 'tabler-mood-smile', color: 'primary' },
  { value: 'tired', icon: 'tabler-zzz', color: 'warning' },
]

function selectEnergy(level: EnergyLevel) {
  selectedEnergy.value = level
  emit('energy-reported', level)

  if (level !== 'tired') {
    // Auto-close after brief delay for non-tired responses
    setTimeout(() => close(), 800)
  }
}

function close() {
  selectedEnergy.value = null
  emit('update:modelValue', false)
}

function takeBreak() {
  emit('take-break')
  close()
}
</script>

<template>
  <VDialog
    :model-value="modelValue"
    max-width="400"
    persistent
    @update:model-value="emit('update:modelValue', $event)"
  >
    <VCard class="pa-4">
      <VCardTitle class="text-h6 text-center">
        {{ t('session.fatigue.title', 'How are you feeling?') }}
      </VCardTitle>

      <VCardText class="text-center">
        <div class="d-flex justify-center gap-4 my-4">
          <VBtn
            v-for="option in energyOptions"
            :key="option.value"
            :color="selectedEnergy === option.value ? option.color : 'default'"
            :variant="selectedEnergy === option.value ? 'elevated' : 'tonal'"
            size="large"
            rounded
            :data-testid="`fatigue-${option.value}`"
            :aria-label="t(`session.fatigue.${option.value}`, option.value)"
            :aria-pressed="selectedEnergy === option.value"
            @click="selectEnergy(option.value)"
          >
            <VIcon :icon="option.icon" size="24" class="me-2" />
            {{ t(`session.fatigue.${option.value}`, option.value) }}
          </VBtn>
        </div>

        <!-- Break suggestion (shown only when tired) -->
        <VExpandTransition>
          <div v-if="showBreakSuggestion" class="mt-4">
            <VAlert
              type="info"
              variant="tonal"
              class="mb-3"
            >
              {{ t('session.fatigue.breakSuggestion', {
                minutes: cooldownMinutes,
              }) }}
            </VAlert>

            <div class="d-flex justify-center gap-3">
              <VBtn
                color="primary"
                variant="tonal"
                data-testid="fatigue-take-break"
                @click="takeBreak"
              >
                {{ t('session.fatigue.takeBreak', 'Take a break') }}
              </VBtn>
              <VBtn
                variant="text"
                data-testid="fatigue-continue"
                @click="close"
              >
                {{ t('session.fatigue.keepGoing', 'Keep going') }}
              </VBtn>
            </div>
          </div>
        </VExpandTransition>
      </VCardText>
    </VCard>
  </VDialog>
</template>
