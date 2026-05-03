import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import OnboardingStepper from '@/components/onboarding/OnboardingStepper.vue'

function makeVuetify() {
  return createVuetify({ components, directives })
}

describe('OnboardingStepper', () => {
  it('renders step N of M label', () => {
    const wrapper = mount(OnboardingStepper, {
      props: { currentStep: 1, totalSteps: 4 },
      global: { plugins: [makeVuetify()] },
    })

    expect(wrapper.text()).toContain('Step 2 of 4')
  })

  it('computes the percent correctly for step 0 of 4 → 25%', () => {
    const wrapper = mount(OnboardingStepper, {
      props: { currentStep: 0, totalSteps: 4 },
      global: { plugins: [makeVuetify()] },
    })

    expect(wrapper.text()).toContain('25%')
  })

  it('computes 100% on the last step', () => {
    const wrapper = mount(OnboardingStepper, {
      props: { currentStep: 3, totalSteps: 4 },
      global: { plugins: [makeVuetify()] },
    })

    expect(wrapper.text()).toContain('100%')
  })

  it('exposes an aria-label on the progress bar', () => {
    const wrapper = mount(OnboardingStepper, {
      props: { currentStep: 1, totalSteps: 4 },
      global: { plugins: [makeVuetify()] },
    })

    const progress = wrapper.find('[aria-label*="Onboarding progress"]')

    expect(progress.exists()).toBe(true)
  })
})
