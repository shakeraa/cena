import { onBeforeUnmount, onMounted } from 'vue'

/**
 * Global keyboard shortcut registry + useShortcut composable (STU-W-15).
 *
 * One keydown listener on window, a Map of registered shortcuts, and a
 * `useShortcut()` composable that components call to register + cleanup.
 *
 * The registry is exported so <KeyboardShortcutCheatsheet> can iterate
 * it, and so the <CommandPalette> can list shortcut-annotated commands.
 */

export type ShortcutScope = 'global' | 'session' | 'tutor' | 'graph' | 'palette'

export interface Shortcut {
  /** Unique id, e.g. 'global.palette' or 'session.submit' */
  id: string
  /** Keyboard combo: a string like 'cmd+k', '?', 'g h', 'shift+/' */
  keys: string
  /** Human-readable label shown in cheatsheet */
  label: string
  /** Group the shortcut belongs to */
  scope: ShortcutScope
  /** The callback when the combo fires */
  handler: (event: KeyboardEvent) => void
  /**
   * When true, the shortcut will NOT fire while an input/textarea/contenteditable
   * is focused. Default true. Global shortcuts like `?` and Cmd+K should set
   * this false so they work everywhere.
   */
  blockInInputs?: boolean
}

const registry = new Map<string, Shortcut>()

/**
 * Sequence buffer for 2-key combos like `g h`. Holds the first key for
 * up to 1 second waiting for the second.
 */
let pendingPrefix: { key: string, until: number } | null = null

function isEditable(target: EventTarget | null): boolean {
  if (!target || !(target instanceof HTMLElement))
    return false
  const tag = target.tagName
  if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT')
    return true

  return target.isContentEditable
}

/**
 * Normalize a browser KeyboardEvent into a single string we can look up.
 * "Cmd+K" → 'cmd+k', "?" → '?', "Escape" → 'escape'.
 */
function normalizeEvent(e: KeyboardEvent): string {
  const parts: string[] = []
  if (e.metaKey || e.ctrlKey)
    parts.push('cmd')
  if (e.altKey)
    parts.push('alt')
  if (e.shiftKey && e.key.length > 1)
    parts.push('shift')

  let key = e.key
  if (key === ' ')
    key = 'space'
  else if (key === 'Escape')
    key = 'escape'
  else if (key === 'Enter')
    key = 'enter'
  else if (key === 'ArrowUp')
    key = 'up'
  else if (key === 'ArrowDown')
    key = 'down'
  else if (key === 'ArrowLeft')
    key = 'left'
  else if (key === 'ArrowRight')
    key = 'right'
  else
    key = key.toLowerCase()

  parts.push(key)

  return parts.join('+')
}

function handleKeydown(e: KeyboardEvent) {
  const combo = normalizeEvent(e)

  // Check 2-key sequences first: if there's a pending prefix and this is
  // another printable key, try `g+h`-style match.
  if (pendingPrefix && Date.now() < pendingPrefix.until) {
    const sequenceCombo = `${pendingPrefix.key} ${e.key.toLowerCase()}`

    pendingPrefix = null
    for (const s of registry.values()) {
      if (s.keys === sequenceCombo) {
        if (s.blockInInputs !== false && isEditable(e.target))
          return
        e.preventDefault()
        s.handler(e)

        return
      }
    }
  }

  // First, look for an exact single-combo match.
  for (const s of registry.values()) {
    if (s.keys === combo || (s.keys === e.key && e.key.length === 1)) {
      if (s.blockInInputs !== false && isEditable(e.target))
        return
      e.preventDefault()
      s.handler(e)

      return
    }
  }

  // If the pressed key could be the start of a 2-key sequence, buffer it.
  if (e.key.length === 1 && !e.metaKey && !e.ctrlKey && !e.altKey) {
    const hasSequenceStartingWith = Array.from(registry.values()).some(
      s => s.keys.startsWith(`${e.key.toLowerCase()} `),
    )
    if (hasSequenceStartingWith)
      pendingPrefix = { key: e.key.toLowerCase(), until: Date.now() + 1000 }
  }
}

let listenerInstalled = false

function ensureListener() {
  if (listenerInstalled || typeof window === 'undefined')
    return
  window.addEventListener('keydown', handleKeydown)
  listenerInstalled = true
}

/**
 * Compose-time API: register one or more shortcuts for the component's
 * lifetime. Automatically unregisters on unmount.
 */
export function useShortcut(shortcuts: Shortcut | Shortcut[]) {
  const list = Array.isArray(shortcuts) ? shortcuts : [shortcuts]

  onMounted(() => {
    ensureListener()
    for (const s of list)
      registry.set(s.id, s)
  })

  onBeforeUnmount(() => {
    for (const s of list)
      registry.delete(s.id)
  })
}

/** All currently-registered shortcuts (read-only). Used by the cheatsheet. */
export function listShortcuts(): Shortcut[] {
  return Array.from(registry.values())
}

/** Testing + palette helpers. */
export function __registerForTest(s: Shortcut) {
  ensureListener()
  registry.set(s.id, s)
}

export function __unregisterForTest(id: string) {
  registry.delete(id)
}

export function __clearAllForTest() {
  registry.clear()
  pendingPrefix = null
}
