import { afterEach, describe, expect, it, vi } from 'vitest'
import { __clearAllForTest, __registerForTest, listShortcuts } from '@/composables/useShortcut'

afterEach(() => {
  __clearAllForTest()
})

function fireKey(keyInit: Partial<KeyboardEventInit> & { key: string }) {
  const event = new KeyboardEvent('keydown', {
    key: keyInit.key,
    metaKey: keyInit.metaKey || false,
    ctrlKey: keyInit.ctrlKey || false,
    altKey: keyInit.altKey || false,
    shiftKey: keyInit.shiftKey || false,
    bubbles: true,
    cancelable: true,
  })

  window.dispatchEvent(event)

  return event
}

describe('useShortcut', () => {
  it('registers a shortcut and listShortcuts returns it', () => {
    const handler = vi.fn()

    __registerForTest({
      id: 'test.one',
      keys: 'a',
      label: 'Test A',
      scope: 'global',
      handler,
      blockInInputs: false,
    })

    expect(listShortcuts().find(s => s.id === 'test.one')).toBeTruthy()
  })

  it('fires a single-key shortcut on matching keydown', () => {
    const handler = vi.fn()

    __registerForTest({
      id: 'test.single',
      keys: '?',
      label: 'Help',
      scope: 'global',
      handler,
      blockInInputs: false,
    })

    fireKey({ key: '?' })

    expect(handler).toHaveBeenCalledTimes(1)
  })

  it('fires cmd+k combo', () => {
    const handler = vi.fn()

    __registerForTest({
      id: 'test.palette',
      keys: 'cmd+k',
      label: 'Palette',
      scope: 'global',
      handler,
      blockInInputs: false,
    })

    fireKey({ key: 'k', metaKey: true })

    expect(handler).toHaveBeenCalledTimes(1)
  })

  it('fires a 2-key sequence shortcut (g h)', async () => {
    const handler = vi.fn()

    __registerForTest({
      id: 'test.go.home',
      keys: 'g h',
      label: 'Go home',
      scope: 'global',
      handler,
      blockInInputs: false,
    })

    fireKey({ key: 'g' })
    fireKey({ key: 'h' })

    expect(handler).toHaveBeenCalledTimes(1)
  })

  it('does not fire blockInInputs shortcut when target is an input', () => {
    const handler = vi.fn()

    __registerForTest({
      id: 'test.blocked',
      keys: 'a',
      label: 'Blocked in inputs',
      scope: 'global',
      handler,
      blockInInputs: true,
    })

    const input = document.createElement('input')

    document.body.appendChild(input)
    input.focus()

    const event = new KeyboardEvent('keydown', { key: 'a', bubbles: true, cancelable: true })

    input.dispatchEvent(event)

    expect(handler).not.toHaveBeenCalled()
    input.remove()
  })

  it('unregisters a shortcut by id', () => {
    const handler = vi.fn()

    __registerForTest({
      id: 'test.removable',
      keys: 'x',
      label: 'Removable',
      scope: 'global',
      handler,
      blockInInputs: false,
    })

    __clearAllForTest()

    fireKey({ key: 'x' })
    expect(handler).not.toHaveBeenCalled()
  })
})
