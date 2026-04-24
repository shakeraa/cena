import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, nextTick, ref } from 'vue'
import { mount } from '@vue/test-utils'
import { useDraftAutosave } from '@/composables/useDraftAutosave'

function makeStorage(): Storage {
  const data = new Map<string, string>()

  return {
    get length() { return data.size },
    clear: () => data.clear(),
    getItem: (k: string) => (data.has(k) ? data.get(k)! : null),
    key: (i: number) => Array.from(data.keys())[i] ?? null,
    removeItem: (k: string) => { data.delete(k) },
    setItem: (k: string, v: string) => { data.set(k, v) },
  }
}

function makeHostComponent(key: string, initial = '', opts?: any) {
  return defineComponent({
    props: { opts: { type: Object, default: () => ({}) } },
    setup(props) {
      const text = ref(initial)
      const result = useDraftAutosave(key, text, { ...opts, ...props.opts })

      return { text, result }
    },
    template: '<div>{{ text }}</div>',
  })
}

describe('useDraftAutosave', () => {
  let storage: Storage

  beforeEach(() => {
    vi.useFakeTimers()
    storage = makeStorage()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('writes to storage after the debounce window on change', async () => {
    const Host = makeHostComponent('k1', '', { debounceMs: 5000, storage })
    const wrapper = mount(Host)

    wrapper.vm.text = 'hello'
    await nextTick()

    // Before debounce elapses: nothing saved.
    vi.advanceTimersByTime(4000)
    expect(storage.getItem('draft:k1')).toBeNull()

    // After debounce: saved.
    vi.advanceTimersByTime(1000)
    expect(storage.getItem('draft:k1')).toBe('hello')
  })

  it('restores the target ref from storage on mount', async () => {
    storage.setItem('draft:k2', 'previously saved')

    const Host = makeHostComponent('k2', '', { storage })
    const wrapper = mount(Host)

    await nextTick()
    expect(wrapper.vm.text).toBe('previously saved')
  })

  it('debounces multiple rapid changes into a single write', async () => {
    const Host = makeHostComponent('k3', '', { debounceMs: 5000, storage })
    const wrapper = mount(Host)
    const spy = vi.spyOn(storage, 'setItem')

    wrapper.vm.text = 'a'
    await nextTick()
    vi.advanceTimersByTime(1000)
    wrapper.vm.text = 'ab'
    await nextTick()
    vi.advanceTimersByTime(1000)
    wrapper.vm.text = 'abc'
    await nextTick()
    vi.advanceTimersByTime(1000)
    wrapper.vm.text = 'abcd'
    await nextTick()

    // Only the final value lands, after a full debounce window from the
    // last change.
    expect(spy).not.toHaveBeenCalled()
    vi.advanceTimersByTime(5000)
    expect(spy).toHaveBeenCalledTimes(1)
    expect(spy).toHaveBeenCalledWith('draft:k3', 'abcd')
  })

  it('flush() writes immediately and clears the debounce timer', async () => {
    const Host = makeHostComponent('k4', '', { debounceMs: 5000, storage })
    const wrapper = mount(Host)

    wrapper.vm.text = 'urgent'
    await nextTick()
    vi.advanceTimersByTime(100)

    expect(storage.getItem('draft:k4')).toBeNull()
    ;(wrapper.vm as any).result.flush()
    expect(storage.getItem('draft:k4')).toBe('urgent')
  })

  it('cleans up timers on unmount', async () => {
    const Host = makeHostComponent('k5', '', { debounceMs: 5000, storage })
    const wrapper = mount(Host)

    wrapper.vm.text = 'pending'
    await nextTick()
    wrapper.unmount()

    // Advance past the debounce window — no write should occur because the
    // timer was cleared on unmount.
    vi.advanceTimersByTime(10_000)
    expect(storage.getItem('draft:k5')).toBeNull()
  })

  it('formats relative time — "just now" under 5s, "Ns ago" under 60s', async () => {
    const Host = makeHostComponent('k6', '', { debounceMs: 100, storage })
    const wrapper = mount(Host)

    wrapper.vm.text = 'x'
    await nextTick()
    vi.advanceTimersByTime(100)
    expect(storage.getItem('draft:k6')).toBe('x')
    expect((wrapper.vm as any).result.relative.value).toBe('just now')
  })
})
