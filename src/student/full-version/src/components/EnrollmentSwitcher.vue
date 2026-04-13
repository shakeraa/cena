<script setup lang="ts">
/**
 * EnrollmentSwitcher.vue — Top-bar dropdown for multi-enrollment scope (TENANCY-P2f)
 *
 * Scopes all downstream pages (mastery map, sessions, analytics) to the
 * selected enrollment. Stored in route query param for deep-link support.
 */

import { computed, ref, watch } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'

interface Enrollment {
  enrollmentId: string
  instituteName: string
  trackCode: string
  trackTitle: string
  status: 'active' | 'paused' | 'completed'
}

const props = defineProps<{
  enrollments: Enrollment[]
  currentEnrollmentId?: string
}>()

const emit = defineEmits<{
  'update:currentEnrollmentId': [id: string]
}>()

const { t } = useI18n()
const router = useRouter()
const route = useRoute()

const selected = ref(
  props.currentEnrollmentId
  || (route.query.enrollment as string)
  || props.enrollments[0]?.enrollmentId
  || ''
)

const selectedEnrollment = computed(() =>
  props.enrollments.find(e => e.enrollmentId === selected.value)
)

const enrollmentItems = computed(() =>
  props.enrollments
    .filter(e => e.status === 'active')
    .map(e => ({
      title: `${e.trackTitle} — ${e.instituteName}`,
      value: e.enrollmentId,
      subtitle: e.trackCode,
    }))
)

watch(selected, (newId) => {
  emit('update:currentEnrollmentId', newId)
  // Persist in URL for deep-linking
  router.replace({ query: { ...route.query, enrollment: newId } })
})
</script>

<template>
  <VSelect
    v-if="enrollments.length > 1"
    v-model="selected"
    :items="enrollmentItems"
    :label="t('enrollment.switcher.label', 'Learning track')"
    variant="outlined"
    density="compact"
    hide-details
    class="enrollment-switcher"
    :aria-label="t('enrollment.switcher.aria', 'Switch learning track')"
    data-testid="enrollment-switcher"
  />

  <!-- Single enrollment: show as text, no dropdown -->
  <div
    v-else-if="selectedEnrollment"
    class="enrollment-switcher--single text-caption"
  >
    {{ selectedEnrollment.trackTitle }}
  </div>
</template>

<style scoped lang="scss">
.enrollment-switcher {
  max-width: 280px;
  min-width: 180px;

  &--single {
    font-weight: 500;
    color: var(--cena-primary, #7367F0);
  }
}
</style>
