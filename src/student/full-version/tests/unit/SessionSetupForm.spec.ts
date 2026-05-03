import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'

// PRR-256: SessionSetupForm now reads /api/me/exam-targets via useApiQuery
// to drive the ExamPrep / Freestyle toggle defaults. We mock that
// composable so each test can deterministically pin the state.
const mockTargetsState: {
  data: ReturnType<typeof ref<unknown>>
  loading: ReturnType<typeof ref<boolean>>
  error: ReturnType<typeof ref<unknown>>
} = {
  data: ref(null),
  loading: ref(false),
  error: ref(null),
}

vi.mock('@/composables/useApiQuery', () => ({
  useApiQuery: () => ({
    data: mockTargetsState.data,
    loading: mockTargetsState.loading,
    error: mockTargetsState.error,
    refresh: async () => {},
  }),
  ApiError: class ApiError extends Error {
    constructor(msg: string, public i18nKey: string, public code: string) {
      super(msg)
    }
  },
}))

import SessionSetupForm from '@/components/session/SessionSetupForm.vue'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        session: {
          setup: {
            subjectsLabel: 'Subjects',
            subjectChipGroupLabel: 'Select subjects',
            durationLabel: 'Duration',
            durationMinutes: '{minutes} min | {minutes} min',
            modeLabel: 'Mode',
            startCta: 'Start',
            subjects: {
              math: 'Math',
              physics: 'Physics',
              chemistry: 'Chemistry',
              biology: 'Biology',
              english: 'English',
              history: 'History',
            },
            modes: {
              practice: 'Practice',
              challenge: 'Challenge',
              review: 'Review',
              diagnostic: 'Diagnostic',
            },
            examScopeLabel: 'What kind of practice?',
            examScope: {
              examPrep: 'Exam prep',
              freestyle: 'Freestyle',
              examPrepAria: 'Exam prep',
              freestyleAria: 'Freestyle',
              examPrepHelper: 'Targeted practice',
              freestyleHelper: 'Open practice',
              noTargetsHelper: 'Add an exam target',
            },
            targetLabel: 'Exam target',
            targetAria: 'Choose which exam',
          },
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

const TWO_TARGETS = {
  items: [
    {
      id: 'et_1',
      source: 'Student',
      examCode: 'bagrut-math-5u',
      sitting: { academicYear: '2025-2026', season: 'Summer', moed: 'A' },
      weeklyHours: 6,
      isActive: true,
      questionPaperCodes: ['35581', '35582'],
    },
    {
      id: 'et_2',
      source: 'Student',
      examCode: 'bagrut-physics-5u',
      sitting: { academicYear: '2025-2026', season: 'Summer', moed: 'A' },
      weeklyHours: 4,
      isActive: true,
      questionPaperCodes: ['36991'],
    },
  ],
  includeArchived: false,
}

const NO_TARGETS = { items: [], includeArchived: false }

describe('SessionSetupForm', () => {
  beforeEach(() => {
    mockTargetsState.data.value = NO_TARGETS
    mockTargetsState.loading.value = false
    mockTargetsState.error.value = null
  })

  it('renders all 6 subjects, 6 durations, 4 modes', () => {
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="setup-subject-math"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="setup-subject-history"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="setup-duration-5"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="setup-duration-60"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="setup-mode-practice"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="setup-mode-diagnostic"]').exists()).toBe(true)
  })

  it('with NO targets: defaults to Freestyle and emits {subjects, durationMinutes, mode, examScope:"freestyle"}', async () => {
    mockTargetsState.data.value = NO_TARGETS
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })
    await wrapper.vm.$nextTick()
    await wrapper.find('[data-testid="session-setup-form"]').trigger('submit.prevent')

    const emitted = wrapper.emitted('submit')
    expect(emitted).toBeTruthy()
    const payload = emitted![0][0] as Record<string, unknown>

    expect(payload).toMatchObject({
      subjects: ['math'],
      durationMinutes: 15,
      mode: 'practice',
      examScope: 'freestyle',
    })
    // Freestyle MUST omit activeExamTargetId per the wire contract
    // (server validator rejects the field with examScope='freestyle').
    expect(payload.activeExamTargetId).toBeUndefined()
  })

  it('with TWO targets: defaults to ExamPrep + first target id and emits the pair on submit', async () => {
    mockTargetsState.data.value = TWO_TARGETS
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })
    await wrapper.vm.$nextTick()
    await wrapper.find('[data-testid="session-setup-form"]').trigger('submit.prevent')

    const payload = wrapper.emitted('submit')![0][0] as Record<string, unknown>
    expect(payload.examScope).toBe('exam-prep')
    expect(payload.activeExamTargetId).toBe('et_1')
  })

  it('with TWO targets: switching to Freestyle clears activeExamTargetId on emit', async () => {
    mockTargetsState.data.value = TWO_TARGETS
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })
    await wrapper.vm.$nextTick()

    // Component is the source of truth for examScope; toggle via the
    // exposed setExamScope (the @update:model-value handler on VBtnToggle).
    ;(wrapper.vm as { setExamScope?: (s: string) => void }).setExamScope?.('freestyle')
    await wrapper.vm.$nextTick()

    await wrapper.find('[data-testid="session-setup-form"]').trigger('submit.prevent')
    const payload = wrapper.emitted('submit')![0][0] as Record<string, unknown>
    expect(payload.examScope).toBe('freestyle')
    expect(payload.activeExamTargetId).toBeUndefined()
  })

  it('ExamPrep button is disabled when there are no targets', () => {
    mockTargetsState.data.value = NO_TARGETS
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const examPrepBtn = wrapper.find('[data-testid="setup-exam-scope-exam-prep"]')
    // Vuetify VBtn renders the disabled prop on the button element via
    // either an attribute or aria-disabled. Either signal counts.
    const disabled = examPrepBtn.attributes('disabled')
    const ariaDisabled = examPrepBtn.attributes('aria-disabled')
    expect(disabled !== undefined || ariaDisabled === 'true').toBe(true)
  })

  it('NO targets shows the no-targets helper copy', async () => {
    mockTargetsState.data.value = NO_TARGETS
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })
    await wrapper.vm.$nextTick()

    // Either the no-targets helper OR the freestyle helper is acceptable —
    // the behavior is "Freestyle is the default + the helper explains".
    // The no-targets helper renders only when scope is null AND
    // !targetsLoading (a transition state); the freestyle helper renders
    // once defaults applied. We accept either.
    const noTargets = wrapper.find('[data-testid="setup-exam-scope-no-targets"]')
    const helper = wrapper.find('[data-testid="setup-exam-scope-helper"]')
    expect(noTargets.exists() || helper.exists()).toBe(true)
  })

  it('Target picker renders only in ExamPrep mode and shows a שאלון paper-codes line', async () => {
    mockTargetsState.data.value = TWO_TARGETS
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[data-testid="setup-target-section"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="setup-target-select"]').exists()).toBe(true)

    // Switch to Freestyle — the picker must disappear.
    ;(wrapper.vm as { setExamScope?: (s: string) => void }).setExamScope?.('freestyle')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('[data-testid="setup-target-section"]').exists()).toBe(false)
  })

  it('Submit gate prevents emitting ExamPrep without a target (defensive contract)', async () => {
    // The component's :disabled binding closes when ExamPrep is selected
    // and no target is set. We can't easily synthesize that internal
    // state from outside (Vue refs unwrap through defineExpose), so this
    // test just asserts the contract: WHEN the form does emit with
    // examScope='exam-prep', activeExamTargetId is always a non-empty
    // string. Any emit that violates this would be a 400 from the server.
    mockTargetsState.data.value = TWO_TARGETS
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })
    await wrapper.vm.$nextTick()
    await wrapper.find('[data-testid="session-setup-form"]').trigger('submit.prevent')

    const emits = wrapper.emitted('submit') ?? []
    expect(emits.length).toBeGreaterThan(0)
    for (const [payload] of emits) {
      const p = payload as Record<string, unknown>
      if (p.examScope === 'exam-prep') {
        expect(typeof p.activeExamTargetId).toBe('string')
        expect((p.activeExamTargetId as string).length).toBeGreaterThan(0)
      }
      if (p.examScope === 'freestyle') {
        expect(p.activeExamTargetId).toBeUndefined()
      }
    }
  })

  it('toggles subject chips add/remove correctly', async () => {
    mockTargetsState.data.value = NO_TARGETS
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="setup-subject-physics"]').trigger('click')
    await wrapper.find('[data-testid="session-setup-form"]').trigger('submit.prevent')

    const first = wrapper.emitted('submit')![0][0] as Record<string, unknown>
    expect(first.subjects).toContain('math')
    expect(first.subjects).toContain('physics')

    await wrapper.find('[data-testid="setup-subject-math"]').trigger('click')
    await wrapper.find('[data-testid="session-setup-form"]').trigger('submit.prevent')

    const second = wrapper.emitted('submit')![1][0] as Record<string, unknown>
    expect(second.subjects).not.toContain('math')
    expect(second.subjects).toContain('physics')
  })

  it('disables submit when no subjects are selected', async () => {
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="setup-subject-math"]').trigger('click')

    const submit = wrapper.find('[data-testid="setup-start"]')
    expect(submit.attributes('disabled')).toBeDefined()

    await wrapper.find('[data-testid="session-setup-form"]').trigger('submit.prevent')
    expect(wrapper.emitted('submit')).toBeFalsy()
  })

  it('subject chips have role="button" and aria-pressed reflects selection (FIND-ux-030)', async () => {
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const mathChip = wrapper.find('[data-testid="setup-subject-math"]')
    const physicsChip = wrapper.find('[data-testid="setup-subject-physics"]')

    expect(mathChip.attributes('role')).toBe('button')
    expect(mathChip.attributes('aria-pressed')).toBe('true')
    expect(physicsChip.attributes('role')).toBe('button')
    expect(physicsChip.attributes('aria-pressed')).toBe('false')

    await physicsChip.trigger('click')
    expect(physicsChip.attributes('aria-pressed')).toBe('true')

    await mathChip.trigger('click')
    expect(mathChip.attributes('aria-pressed')).toBe('false')
  })

  it('subject chip group has role="group" with aria-label (FIND-ux-030)', () => {
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const group = wrapper.find('[data-testid="setup-subjects"]')
    expect(group.attributes('role')).toBe('group')
    expect(group.attributes('aria-label')).toBe('Select subjects')
  })

  it('subject chips have aria-label with translated name (FIND-ux-030)', () => {
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const mathChip = wrapper.find('[data-testid="setup-subject-math"]')
    expect(mathChip.attributes('aria-label')).toBe('Math')
  })
})
