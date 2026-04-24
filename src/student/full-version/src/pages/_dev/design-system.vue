<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useTheme } from 'vuetify'
import type { FlowState } from '@/plugins/vuetify/theme'

definePage({
  meta: {
    layout: 'blank',
  },
})

const { t } = useI18n()
const theme = useTheme()

const toggleTheme = () => {
  theme.global.name.value = theme.global.current.value.dark ? 'light' : 'dark'
}

const currentFlowState = ref<FlowState>('inFlow')
const flowStates: FlowState[] = ['warming', 'approaching', 'inFlow', 'disrupted', 'fatigued']

const masteryLevels = [
  { code: 'novice', color: 'mastery-novice' },
  { code: 'learning', color: 'mastery-learning' },
  { code: 'proficient', color: 'mastery-proficient' },
  { code: 'mastered', color: 'mastery-mastered' },
  { code: 'expert', color: 'mastery-expert' },
] as const

const triggerError = ref(false)

const Bomb = {
  setup() {
    if (triggerError.value)
      throw new Error('Intentional boundary test error')

    return () => null
  },
}
</script>

<template>
  <FlowAmbientBackground :flow-state="currentFlowState" />

  <div class="design-system-page pa-8">
    <header class="d-flex align-center justify-space-between mb-6 flex-wrap gap-3">
      <div>
        <h1 class="text-h4 mb-1">
          STU-W-01 Design System
        </h1>
        <p class="text-body-2 text-medium-emphasis">
          Tokens, layouts, locales, shared components — verified chassis.
        </p>
      </div>
      <div class="d-flex gap-2 align-center flex-wrap">
        <VBtn
          data-testid="ds-toggle-theme"
          variant="tonal"
          @click="toggleTheme"
        >
          Toggle theme ({{ theme.global.name.value }})
        </VBtn>
        <LanguageSwitcher />
      </div>
    </header>

    <VDivider class="mb-6" />

    <section
      class="mb-8"
      aria-labelledby="section-tokens"
    >
      <h2
        id="section-tokens"
        class="text-h5 mb-3"
      >
        Flow &amp; mastery tokens
      </h2>
      <div class="d-flex flex-wrap gap-3 mb-4">
        <!--
          Active state uses `variant="flat" color="primary"` (solid
          primary with white text) so contrast stays above 4.5:1; tonal
          would drop primary text on a near-white tint below AA.
        -->
        <VBtn
          v-for="state in flowStates"
          :key="state"
          :color="currentFlowState === state ? 'primary' : undefined"
          :variant="currentFlowState === state ? 'flat' : 'outlined'"
          :data-testid="`flow-${state}`"
          @click="currentFlowState = state"
        >
          {{ t(`flow.${state}`) }}
        </VBtn>
      </div>
      <!--
        Mastery swatches: colored dot carries the semantic color; the
        label uses the default high-emphasis text color so the whole
        row passes WCAG AA contrast in both light and dark modes.
      -->
      <div class="d-flex flex-wrap gap-3">
        <div
          v-for="level in masteryLevels"
          :key="level.code"
          class="mastery-swatch d-flex align-center gap-2 px-3 py-2"
        >
          <span
            class="mastery-dot"
            :class="`bg-${level.color}`"
            aria-hidden="true"
          />
          <span class="text-body-2 text-high-emphasis">
            {{ t(`mastery.${level.code}`) }}
          </span>
        </div>
      </div>
    </section>

    <section
      class="mb-8"
      aria-labelledby="section-kpis"
    >
      <h2
        id="section-kpis"
        class="text-h5 mb-3"
      >
        KPI cards
      </h2>
      <div class="d-flex flex-wrap gap-4">
        <KpiCard
          label="Minutes today"
          :value="42"
          :trend="12"
          icon="tabler-clock"
        />
        <KpiCard
          label="Questions"
          :value="128"
          :trend="-4"
          icon="tabler-question-mark"
        />
        <KpiCard
          label="Streak"
          :value="7"
          :trend="0"
          icon="tabler-flame"
        />
        <KpiCard
          label="XP"
          :value="2340"
          icon="tabler-bolt"
        />
      </div>
    </section>

    <section
      class="mb-8"
      aria-labelledby="section-skeleton"
    >
      <h2
        id="section-skeleton"
        class="text-h5 mb-3"
      >
        Skeleton loader
      </h2>
      <div class="d-flex flex-wrap gap-4">
        <StudentSkeletonCard :lines="3" />
        <StudentSkeletonCard
          :lines="5"
          show-avatar
        />
      </div>
    </section>

    <section
      class="mb-8"
      aria-labelledby="section-empty"
    >
      <h2
        id="section-empty"
        class="text-h5 mb-3"
      >
        Empty state
      </h2>
      <StudentEmptyState
        icon="tabler-books"
        :title="t('empty.noSessions')"
        :subtitle="t('empty.noSessionsSubtitle')"
      >
        <template #actions>
          <VBtn color="primary">
            {{ t('common.save') }}
          </VBtn>
          <VBtn variant="text">
            {{ t('common.cancel') }}
          </VBtn>
        </template>
      </StudentEmptyState>
    </section>

    <section
      class="mb-8"
      aria-labelledby="section-error"
    >
      <h2
        id="section-error"
        class="text-h5 mb-3"
      >
        Error boundary
      </h2>
      <VBtn
        class="mb-3"
        color="error"
        variant="tonal"
        data-testid="trigger-error"
        @click="triggerError = true"
      >
        Trigger error
      </VBtn>
      <StudentErrorBoundary>
        <Bomb />
      </StudentErrorBoundary>
    </section>
  </div>
</template>

<style scoped>
.design-system-page {
  max-inline-size: 1280px;
  margin-inline: auto;
}

.mastery-swatch {
  border: 1px solid rgb(var(--v-theme-on-surface) / 0.12);
  border-radius: 8px;
}

.mastery-dot {
  display: inline-block;
  inline-size: 12px;
  block-size: 12px;
  border-radius: 50%;
}
</style>
