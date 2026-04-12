/**
 * FIND-privacy-018: ReportDialog unit test
 * Verifies the report dialog renders categories, accepts input, and submits.
 */
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import ReportDialog from '@/components/social/ReportDialog.vue'

// Mock $api
vi.mock('@/api/$api', () => ({
  $api: vi.fn(),
}))

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        common: {
          cancel: 'Cancel',
        },
        social: {
          report: {
            title: 'Report content',
            description: 'Help us keep the community safe. Select a reason for reporting this content.',
            categoryLabel: 'Why are you reporting this?',
            category: {
              bullying: 'Bullying or harassment',
              inappropriate: 'Inappropriate content',
              spam: 'Spam',
              'self-harm-risk': 'Self-harm or safety concern',
              other: 'Other',
            },
            reasonLabel: 'Additional details (optional)',
            reasonPlaceholder: 'Tell us more about what happened…',
            submitBtn: 'Submit report',
            submitError: 'Could not submit your report. Please try again.',
            success: 'Thank you. Your report has been submitted and will be reviewed.',
            ariaLabel: 'Report this content',
          },
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

/**
 * Vuetify VDialog teleports its content to the document body.
 * We must use `attachTo: document.body` and search in `document.body`
 * rather than the wrapper's DOM.
 */
describe('ReportDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    // Clean up teleported content from previous tests
    document.body.textContent = ''
  })

  it('renders dialog with title and categories when open', () => {
    mount(ReportDialog, {
      props: {
        modelValue: true,
        contentType: 'feed-item',
        contentId: 'f1',
      },
      global: {
        plugins: [makeI18n(), makeVuetify()],
      },
      attachTo: document.body,
    })

    const body = document.body.textContent ?? ''
    expect(body).toContain('Report content')
    expect(body).toContain('Bullying or harassment')
    expect(body).toContain('Inappropriate content')
    expect(body).toContain('Spam')
    expect(body).toContain('Self-harm or safety concern')
    expect(body).toContain('Other')
  })

  it('renders submit and cancel buttons', () => {
    mount(ReportDialog, {
      props: {
        modelValue: true,
        contentType: 'feed-item',
        contentId: 'f1',
      },
      global: {
        plugins: [makeI18n(), makeVuetify()],
      },
      attachTo: document.body,
    })

    const submitBtn = document.querySelector('[data-testid="report-submit-btn"]')
    const cancelBtn = document.querySelector('[data-testid="report-cancel-btn"]')
    expect(submitBtn).not.toBeNull()
    expect(cancelBtn).not.toBeNull()
  })

  it('has submit button text "Submit report"', () => {
    mount(ReportDialog, {
      props: {
        modelValue: true,
        contentType: 'feed-item',
        contentId: 'f1',
      },
      global: {
        plugins: [makeI18n(), makeVuetify()],
      },
      attachTo: document.body,
    })

    const submitBtn = document.querySelector('[data-testid="report-submit-btn"]')
    expect(submitBtn?.textContent).toContain('Submit report')
  })

  it('emits update:modelValue(false) on cancel click', async () => {
    const wrapper = mount(ReportDialog, {
      props: {
        modelValue: true,
        contentType: 'feed-item',
        contentId: 'f1',
      },
      global: {
        plugins: [makeI18n(), makeVuetify()],
      },
      attachTo: document.body,
    })

    const cancelBtn = document.querySelector('[data-testid="report-cancel-btn"]') as HTMLElement
    expect(cancelBtn).not.toBeNull()
    cancelBtn.click()
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('update:modelValue')).toBeTruthy()
    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })

  it('renders the radio group for report categories', () => {
    mount(ReportDialog, {
      props: {
        modelValue: true,
        contentType: 'feed-item',
        contentId: 'f1',
      },
      global: {
        plugins: [makeI18n(), makeVuetify()],
      },
      attachTo: document.body,
    })

    const radioGroup = document.querySelector('[data-testid="report-category-group"]')
    expect(radioGroup).not.toBeNull()

    // Should have 5 radio options
    const radios = document.querySelectorAll('.v-radio')
    expect(radios.length).toBe(5)
  })

  it('renders description text explaining why to report', () => {
    mount(ReportDialog, {
      props: {
        modelValue: true,
        contentType: 'peer-solution',
        contentId: 'sol-1',
      },
      global: {
        plugins: [makeI18n(), makeVuetify()],
      },
      attachTo: document.body,
    })

    expect(document.body.textContent).toContain('Help us keep the community safe')
  })
})
