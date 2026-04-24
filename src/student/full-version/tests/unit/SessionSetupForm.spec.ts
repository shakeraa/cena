import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
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
            durationMinutes: '{minutes} min',
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
          },
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

describe('SessionSetupForm', () => {
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

  it('defaults to math + 15 min + practice and emits that on submit', async () => {
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="session-setup-form"]').trigger('submit.prevent')

    const emitted = wrapper.emitted('submit')

    expect(emitted).toBeTruthy()
    expect(emitted![0][0]).toEqual({
      subjects: ['math'],
      durationMinutes: 15,
      mode: 'practice',
    })
  })

  it('toggles subject chips add/remove correctly', async () => {
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="setup-subject-physics"]').trigger('click')
    await wrapper.find('[data-testid="session-setup-form"]').trigger('submit.prevent')

    const first = wrapper.emitted('submit')![0][0] as any

    expect(first.subjects).toContain('math')
    expect(first.subjects).toContain('physics')

    // Toggle math off
    await wrapper.find('[data-testid="setup-subject-math"]').trigger('click')
    await wrapper.find('[data-testid="session-setup-form"]').trigger('submit.prevent')

    const second = wrapper.emitted('submit')![1][0] as any

    expect(second.subjects).not.toContain('math')
    expect(second.subjects).toContain('physics')
  })

  it('disables submit when no subjects are selected', async () => {
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    // Remove the default math selection
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

    // Math is selected by default, physics is not
    expect(mathChip.attributes('role')).toBe('button')
    expect(mathChip.attributes('aria-pressed')).toBe('true')
    expect(physicsChip.attributes('role')).toBe('button')
    expect(physicsChip.attributes('aria-pressed')).toBe('false')

    // Click physics to select it
    await physicsChip.trigger('click')
    expect(physicsChip.attributes('aria-pressed')).toBe('true')

    // Click math to deselect it
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

  it('onSubjectKeydown toggles selection on Space and Enter (FIND-ux-030)', async () => {
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const physicsChip = wrapper.find('[data-testid="setup-subject-physics"]')

    expect(physicsChip.attributes('aria-pressed')).toBe('false')

    // Click to select (simulating keyboard activation — Space/Enter on a
    // button triggers click in browsers; the @keydown handler calls
    // toggleSubject which is the same code path as click).
    await physicsChip.trigger('click')
    expect(physicsChip.attributes('aria-pressed')).toBe('true')

    // Click again to deselect
    await physicsChip.trigger('click')
    expect(physicsChip.attributes('aria-pressed')).toBe('false')

    // Verify the component exposes the onSubjectKeydown handler that
    // calls toggleSubject for Enter and Space. The handler is tested
    // via the component instance to avoid jsdom KeyboardEvent limitations.
    const vm = wrapper.vm as any

    // Manually invoke the keydown handler with a Space event
    const spaceEvent = { key: ' ', preventDefault: vi.fn() }

    vm.onSubjectKeydown(spaceEvent, 'physics')
    await wrapper.vm.$nextTick()
    expect(physicsChip.attributes('aria-pressed')).toBe('true')
    expect(spaceEvent.preventDefault).toHaveBeenCalled()

    // Invoke with an Enter event
    const enterEvent = { key: 'Enter', preventDefault: vi.fn() }

    vm.onSubjectKeydown(enterEvent, 'physics')
    await wrapper.vm.$nextTick()
    expect(physicsChip.attributes('aria-pressed')).toBe('false')
    expect(enterEvent.preventDefault).toHaveBeenCalled()

    // A non-matching key should not toggle
    const tabEvent = { key: 'Tab', preventDefault: vi.fn() }

    vm.onSubjectKeydown(tabEvent, 'physics')
    await wrapper.vm.$nextTick()
    expect(physicsChip.attributes('aria-pressed')).toBe('false')
    expect(tabEvent.preventDefault).not.toHaveBeenCalled()
  })
})
