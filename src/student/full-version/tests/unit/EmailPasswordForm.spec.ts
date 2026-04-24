import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import EmailPasswordForm from '@/components/common/EmailPasswordForm.vue'

async function setField(wrapper: any, testid: string, value: string) {
  const input = wrapper.find(`[data-testid="${testid}"] input`)

  await input.setValue(value)
}

describe('EmailPasswordForm', () => {
  it('renders login mode without the display-name field', () => {
    const wrapper = mount(EmailPasswordForm, { props: { mode: 'login' } })

    expect(wrapper.find('[data-testid="auth-email"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="auth-password"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="auth-display-name"]').exists()).toBe(false)
  })

  it('renders register mode with the display-name field', () => {
    const wrapper = mount(EmailPasswordForm, { props: { mode: 'register' } })

    expect(wrapper.find('[data-testid="auth-display-name"]').exists()).toBe(true)
  })

  it('shows validation errors on empty submit', async () => {
    const wrapper = mount(EmailPasswordForm, { props: { mode: 'login' } })

    await wrapper.find('[data-testid="email-password-form"]').trigger('submit.prevent')

    // No submit event emitted
    expect(wrapper.emitted('submit')).toBeFalsy()

    // Errors surfaced
    const text = wrapper.text()

    expect(text).toContain('Email is required')
    expect(text).toContain('Password is required')
  })

  it('rejects an invalid email format', async () => {
    const wrapper = mount(EmailPasswordForm, { props: { mode: 'login' } })

    await setField(wrapper, 'auth-email', 'not-an-email')
    await setField(wrapper, 'auth-password', 'abcdef')
    await wrapper.find('[data-testid="email-password-form"]').trigger('submit.prevent')
    expect(wrapper.emitted('submit')).toBeFalsy()
    expect(wrapper.text()).toContain('valid email')
  })

  it('rejects a password shorter than 6 characters', async () => {
    const wrapper = mount(EmailPasswordForm, { props: { mode: 'login' } })

    await setField(wrapper, 'auth-email', 'user@example.com')
    await setField(wrapper, 'auth-password', '123')
    await wrapper.find('[data-testid="email-password-form"]').trigger('submit.prevent')
    expect(wrapper.emitted('submit')).toBeFalsy()
    expect(wrapper.text()).toContain('at least 6 characters')
  })

  it('emits submit with {email, password} on a valid login submission', async () => {
    const wrapper = mount(EmailPasswordForm, { props: { mode: 'login' } })

    await setField(wrapper, 'auth-email', 'user@example.com')
    await setField(wrapper, 'auth-password', 'secret123')
    await wrapper.find('[data-testid="email-password-form"]').trigger('submit.prevent')
    expect(wrapper.emitted('submit')).toBeTruthy()

    const payload = wrapper.emitted('submit')![0][0]

    expect(payload).toEqual({ email: 'user@example.com', password: 'secret123' })
  })

  it('emits submit with displayName on a valid register submission', async () => {
    const wrapper = mount(EmailPasswordForm, { props: { mode: 'register' } })

    await setField(wrapper, 'auth-display-name', 'Alice')
    await setField(wrapper, 'auth-email', 'alice@example.com')
    await setField(wrapper, 'auth-password', 'secret123')
    await wrapper.find('[data-testid="email-password-form"]').trigger('submit.prevent')
    expect(wrapper.emitted('submit')).toBeTruthy()

    const payload = wrapper.emitted('submit')![0][0]

    expect(payload).toEqual({
      email: 'alice@example.com',
      password: 'secret123',
      displayName: 'Alice',
    })
  })

  it('blocks submit when submitLocked prop is true', async () => {
    const wrapper = mount(EmailPasswordForm, {
      props: { mode: 'login', submitLocked: true, lockedSecondsRemaining: 3 },
    })

    await setField(wrapper, 'auth-email', 'user@example.com')
    await setField(wrapper, 'auth-password', 'secret123')
    await wrapper.find('[data-testid="email-password-form"]').trigger('submit.prevent')
    expect(wrapper.emitted('submit')).toBeFalsy()
  })
})
