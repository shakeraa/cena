<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { StudentRole } from '@/stores/onboardingStore'

interface Props {
  modelValue: StudentRole | null
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:modelValue': [value: StudentRole]
}>()

const { t } = useI18n()

interface RoleOption {
  id: StudentRole
  icon: string
  titleKey: string
  descriptionKey: string
  testId: string
}

const ROLES: RoleOption[] = [
  {
    id: 'student',
    icon: 'tabler-school',
    titleKey: 'onboarding.role.student',
    descriptionKey: 'onboarding.role.studentDescription',
    testId: 'role-student',
  },
  {
    id: 'self-learner',
    icon: 'tabler-user',
    titleKey: 'onboarding.role.selfLearner',
    descriptionKey: 'onboarding.role.selfLearnerDescription',
    testId: 'role-self-learner',
  },
  {
    id: 'test-prep',
    icon: 'tabler-target',
    titleKey: 'onboarding.role.testPrep',
    descriptionKey: 'onboarding.role.testPrepDescription',
    testId: 'role-test-prep',
  },
  {
    id: 'homeschool',
    icon: 'tabler-home-heart',
    titleKey: 'onboarding.role.homeschool',
    descriptionKey: 'onboarding.role.homeschoolDescription',
    testId: 'role-homeschool',
  },
]

function select(id: StudentRole) {
  emit('update:modelValue', id)
}
</script>

<template>
  <div
    class="role-selector"
    data-testid="role-selector"
  >
    <VCard
      v-for="role in ROLES"
      :key="role.id"
      :variant="props.modelValue === role.id ? 'flat' : 'outlined'"
      :color="props.modelValue === role.id ? 'primary' : undefined"
      class="role-selector__tile pa-5 cursor-pointer"
      :data-testid="role.testId"
      tabindex="0"
      :aria-pressed="props.modelValue === role.id"
      role="button"
      @click="select(role.id)"
      @keydown.enter="select(role.id)"
      @keydown.space.prevent="select(role.id)"
    >
      <VIcon
        :icon="role.icon"
        size="32"
        class="mb-2"
        aria-hidden="true"
      />
      <div class="text-subtitle-1 font-weight-medium">
        {{ t(role.titleKey) }}
      </div>
      <div class="text-body-2 text-medium-emphasis mt-1">
        {{ t(role.descriptionKey) }}
      </div>
    </VCard>
  </div>
</template>

<style scoped>
.role-selector {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 1rem;
}

.role-selector__tile {
  transition: transform 0.15s ease-out, border-color 0.15s ease-out;
}

.role-selector__tile:hover {
  transform: translateY(-2px);
}

.role-selector__tile:focus-visible {
  outline: 2px solid rgb(var(--v-theme-primary));
  outline-offset: 2px;
}
</style>
