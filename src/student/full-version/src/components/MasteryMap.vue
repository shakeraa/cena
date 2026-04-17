<script setup lang="ts">
/**
 * MasteryMap.vue — Mastery map progress visualization (MASTERY-MAP-001)
 *
 * Renders the skill prerequisite DAG as an interactive map.
 * Each node shows: skill name, effective mastery %, color-coded status.
 * Prerequisite edges shown as directional arrows.
 *
 * Uses the curriculum skill graph from BKT-PLUS-001 prerequisite DAG.
 */

import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'

interface SkillNode {
  id: string
  name: string
  effectiveMastery: number
  pLearned: number
  needsRefresh: boolean
  prerequisites: string[]
  status: 'locked' | 'available' | 'in-progress' | 'mastered' | 'needs-refresh'
}

interface MasteryMapProps {
  skills: SkillNode[]
  trackCode: string
}

const props = defineProps<MasteryMapProps>()
const { t } = useI18n()
const selectedSkill = ref<string | null>(null)

const skillMap = computed(() => {
  const map = new Map<string, SkillNode>()
  for (const skill of props.skills)
    map.set(skill.id, skill)

  return map
})

function getStatusColor(status: SkillNode['status']): string {
  switch (status) {
    case 'mastered': return 'var(--cena-success, #28C76F)'
    case 'in-progress': return 'var(--cena-primary, #7367F0)'
    case 'available': return 'var(--cena-info, #00CFE8)'
    case 'needs-refresh': return 'var(--cena-warning, #FF9F43)'
    case 'locked': return 'var(--cena-muted, #B4B7BD)'
    default: return 'var(--cena-muted, #B4B7BD)'
  }
}

function getStatusLabel(status: SkillNode['status']): string {
  switch (status) {
    case 'mastered': return t('mastery.status.mastered', 'Mastered')
    case 'in-progress': return t('mastery.status.inProgress', 'In Progress')
    case 'available': return t('mastery.status.available', 'Available')
    case 'needs-refresh': return t('mastery.status.needsRefresh', 'Needs Review')
    case 'locked': return t('mastery.status.locked', 'Locked')
    default: return ''
  }
}

function getMasteryPercent(skill: SkillNode): string {
  return `${Math.round(skill.effectiveMastery * 100)}%`
}

function selectSkill(skillId: string) {
  selectedSkill.value = selectedSkill.value === skillId ? null : skillId
}

const selectedSkillDetail = computed(() => {
  if (!selectedSkill.value)
    return null

  return skillMap.value.get(selectedSkill.value) ?? null
})
</script>

<template>
  <div
    class="mastery-map"
    role="region"
    :aria-label="t('mastery.map.label', 'Mastery Map')"
  >
    <h2 class="mastery-map__title">
      {{ t('mastery.map.title', 'Your Learning Map') }}
      <span class="mastery-map__track">{{ trackCode }}</span>
    </h2>

    <div
      class="mastery-map__grid"
      role="list"
    >
      <button
        v-for="skill in skills"
        :key="skill.id"
        class="mastery-map__node"
        :class="[
          `mastery-map__node--${skill.status}`,
          { 'mastery-map__node--selected': selectedSkill === skill.id },
        ]"
        :style="{ '--node-color': getStatusColor(skill.status) }"
        :aria-label="`${skill.name}: ${getMasteryPercent(skill)} ${getStatusLabel(skill.status)}`"
        :aria-pressed="selectedSkill === skill.id"
        :disabled="skill.status === 'locked'"
        role="listitem"
        @click="selectSkill(skill.id)"
      >
        <div class="mastery-map__node-ring">
          <svg
            viewBox="0 0 36 36"
            class="mastery-map__progress-ring"
          >
            <path
              class="mastery-map__progress-bg"
              d="M18 2.0845 a 15.9155 15.9155 0 0 1 0 31.831 a 15.9155 15.9155 0 0 1 0 -31.831"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              opacity="0.2"
            />
            <path
              class="mastery-map__progress-fill"
              d="M18 2.0845 a 15.9155 15.9155 0 0 1 0 31.831 a 15.9155 15.9155 0 0 1 0 -31.831"
              fill="none"
              :stroke="getStatusColor(skill.status)"
              stroke-width="2.5"
              :stroke-dasharray="`${skill.effectiveMastery * 100}, 100`"
              stroke-linecap="round"
            />
          </svg>
          <span class="mastery-map__percent">{{ getMasteryPercent(skill) }}</span>
        </div>
        <span class="mastery-map__node-name">{{ skill.name }}</span>
        <span
          class="mastery-map__node-status"
          :style="{ color: getStatusColor(skill.status) }"
        >
          {{ getStatusLabel(skill.status) }}
        </span>
      </button>
    </div>

    <!-- Detail panel for selected skill -->
    <Transition name="slide-fade">
      <div
        v-if="selectedSkillDetail"
        class="mastery-map__detail"
        role="complementary"
        :aria-label="t('mastery.map.detail', 'Skill detail')"
      >
        <h3>{{ selectedSkillDetail.name }}</h3>
        <dl class="mastery-map__detail-list">
          <dt>{{ t('mastery.detail.effective', 'Effective Mastery') }}</dt>
          <dd>{{ getMasteryPercent(selectedSkillDetail) }}</dd>

          <dt>{{ t('mastery.detail.learned', 'Learned (before decay)') }}</dt>
          <dd>{{ Math.round(selectedSkillDetail.pLearned * 100) }}%</dd>

          <dt>{{ t('mastery.detail.status', 'Status') }}</dt>
          <dd :style="{ color: getStatusColor(selectedSkillDetail.status) }">
            {{ getStatusLabel(selectedSkillDetail.status) }}
          </dd>

          <template v-if="selectedSkillDetail.prerequisites.length > 0">
            <dt>{{ t('mastery.detail.prerequisites', 'Prerequisites') }}</dt>
            <dd>
              <ul class="mastery-map__prereq-list">
                <li
                  v-for="prereqId in selectedSkillDetail.prerequisites"
                  :key="prereqId"
                >
                  {{ skillMap.get(prereqId)?.name ?? prereqId }}
                  —
                  {{ getMasteryPercent(skillMap.get(prereqId) ?? { effectiveMastery: 0 } as SkillNode) }}
                </li>
              </ul>
            </dd>
          </template>
        </dl>

        <div
          v-if="selectedSkillDetail.needsRefresh"
          class="mastery-map__refresh-banner"
        >
          {{ t('mastery.refresh', 'This skill has decayed. A quick review will restore it.') }}
        </div>
      </div>
    </Transition>
  </div>
</template>

<style scoped lang="scss">
.mastery-map {
  padding: 1rem;

  &__title {
    font-size: 1.25rem;
    font-weight: 600;
    margin-bottom: 1rem;
  }

  &__track {
    font-size: 0.875rem;
    font-weight: 400;
    opacity: 0.6;
    margin-inline-start: 0.5rem;
  }

  &__grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
    gap: 1rem;
  }

  &__node {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 0.375rem;
    padding: 0.75rem 0.5rem;
    border: 2px solid transparent;
    border-radius: 12px;
    background: var(--cena-card-bg, #fff);
    cursor: pointer;
    transition: border-color 0.2s, transform 0.15s;
    min-height: 44px; // touch target

    &:hover:not(:disabled) { transform: translateY(-2px); }
    &--selected { border-color: var(--node-color); }
    &--locked { opacity: 0.5; cursor: not-allowed; }
  }

  &__node-ring {
    position: relative;
    width: 56px;
    height: 56px;
  }

  &__progress-ring {
    width: 100%;
    height: 100%;
    transform: rotate(-90deg);
  }

  &__percent {
    position: absolute;
    inset: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 0.75rem;
    font-weight: 700;
  }

  &__node-name {
    font-size: 0.75rem;
    font-weight: 500;
    text-align: center;
    line-height: 1.2;
  }

  &__node-status {
    font-size: 0.625rem;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.05em;
  }

  &__detail {
    margin-top: 1.5rem;
    padding: 1rem;
    border-radius: 8px;
    background: var(--cena-card-bg, #f8f8f8);
  }

  &__detail-list {
    display: grid;
    grid-template-columns: auto 1fr;
    gap: 0.5rem 1rem;

    dt { font-weight: 600; font-size: 0.875rem; }
    dd { font-size: 0.875rem; }
  }

  &__prereq-list {
    list-style: none;
    padding: 0;
    margin: 0;
    li { font-size: 0.8125rem; }
  }

  &__refresh-banner {
    margin-top: 0.75rem;
    padding: 0.5rem 0.75rem;
    border-radius: 6px;
    background: color-mix(in srgb, var(--cena-warning, #FF9F43) 15%, transparent);
    color: var(--cena-warning, #FF9F43);
    font-size: 0.8125rem;
    font-weight: 500;
  }
}

.slide-fade-enter-active { transition: all 0.2s ease; }
.slide-fade-leave-active { transition: all 0.15s ease; }
.slide-fade-enter-from,
.slide-fade-leave-to {
  opacity: 0;
  transform: translateY(-8px);
}

/* RDY-030b: prefers-reduced-motion guard (WCAG 2.3.3).
   Component-local animations/transitions reduced to an imperceptible
   0.01ms so vestibular-sensitive users don't trigger motion-related
   symptoms. Complements the global reset in styles.scss. */
@media (prefers-reduced-motion: reduce) {
  * {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
    scroll-behavior: auto !important;
  }
}
</style>
