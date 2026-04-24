import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import PeerSolutionCard from '@/components/social/PeerSolutionCard.vue'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        social: { peers: { forQuestion: 'Q#{questionId}' } },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

const sample = {
  solutionId: 'sol-1',
  questionId: 'q_001',
  authorStudentId: 'u-priya',
  authorDisplayName: 'Priya Rao',
  content: 'Use the distributive property.',
  upvoteCount: 15,
  downvoteCount: 2,
  postedAt: '2026-04-10T00:00:00Z',
}

describe('PeerSolutionCard', () => {
  it('renders author, content, vote counts, and question reference', () => {
    const wrapper = mount(PeerSolutionCard, {
      props: { solution: sample },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.text()).toContain('Priya Rao')
    expect(wrapper.find('[data-testid="peer-solution-content"]').text()).toContain('distributive')
    expect(wrapper.text()).toContain('15')
    expect(wrapper.text()).toContain('2')
    expect(wrapper.text()).toContain('Q#q_001')
  })

  it('emits vote up event with solutionId', async () => {
    const wrapper = mount(PeerSolutionCard, {
      props: { solution: sample },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="upvote-sol-1"]').trigger('click')

    expect(wrapper.emitted('vote')![0]).toEqual(['sol-1', 'up'])
  })

  it('emits vote down event with solutionId', async () => {
    const wrapper = mount(PeerSolutionCard, {
      props: { solution: sample },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="downvote-sol-1"]').trigger('click')

    expect(wrapper.emitted('vote')![0]).toEqual(['sol-1', 'down'])
  })
})
