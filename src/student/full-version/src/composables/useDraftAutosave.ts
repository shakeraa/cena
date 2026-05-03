import type { Ref } from 'vue'
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'

export interface UseDraftAutosaveResult {
  savedAt: Ref<Date | null>
  relative: Ref<string>
  flush: () => void
}

/**
 * Debounced localStorage autosave for long-text inputs. Restores from
 * storage on mount, debounces writes on change, and exposes a relative
 * "saved Ns ago" label that updates every second.
 *
 * STU-W-06 (live session) and STU-W-08 (tutor) are the primary
 * consumers — a dropped network or refresh mid-answer must not lose
 * the student's in-progress text.
 */
export function useDraftAutosave(
  key: string,
  target: Ref<string>,
  opts: { debounceMs?: number; storage?: Storage } = {},
): UseDraftAutosaveResult {
  const debounceMs = opts.debounceMs ?? 5000
  const storage: Storage | undefined = opts.storage ?? (typeof localStorage !== 'undefined' ? localStorage : undefined)
  const storageKey = `draft:${key}`

  const savedAt = ref<Date | null>(null)
  const relative = ref<string>('')

  let debounceTimer: ReturnType<typeof setTimeout> | null = null
  let tickInterval: ReturnType<typeof setInterval> | null = null

  function formatRelative(date: Date | null): string {
    if (!date)
      return ''
    const secs = Math.floor((Date.now() - date.getTime()) / 1000)
    if (secs < 5)
      return 'just now'
    if (secs < 60)
      return `${secs}s ago`
    const mins = Math.floor(secs / 60)
    if (mins < 60)
      return `${mins}m ago`
    const hrs = Math.floor(mins / 60)

    return `${hrs}h ago`
  }

  function writeToStorage(value: string) {
    if (!storage)
      return
    try {
      storage.setItem(storageKey, value)
      savedAt.value = new Date()
      relative.value = formatRelative(savedAt.value)
    }
    catch {
      // swallow quota / private-mode errors — autosave is best-effort
    }
  }

  function flush() {
    if (debounceTimer) {
      clearTimeout(debounceTimer)
      debounceTimer = null
    }
    writeToStorage(target.value)
  }

  onMounted(() => {
    if (storage) {
      const stored = storage.getItem(storageKey)
      if (stored !== null)
        target.value = stored
    }

    watch(target, val => {
      if (debounceTimer)
        clearTimeout(debounceTimer)
      debounceTimer = setTimeout(() => writeToStorage(val), debounceMs)
    })

    tickInterval = setInterval(() => {
      if (savedAt.value)
        relative.value = formatRelative(savedAt.value)
    }, 1000)
  })

  onBeforeUnmount(() => {
    if (debounceTimer) {
      clearTimeout(debounceTimer)
      debounceTimer = null
    }
    if (tickInterval) {
      clearInterval(tickInterval)
      tickInterval = null
    }
  })

  return { savedAt, relative, flush }
}
