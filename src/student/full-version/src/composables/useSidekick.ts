/**
 * prr-207 — useSidekick composable.
 *
 * Owns the Sidekick drawer's cross-cutting state:
 *
 *   - Drawer open/close (with focus-trap + restore-focus wired from the
 *     component itself; this composable exposes the refs/methods the
 *     component uses)
 *   - Tutor-context snapshot (prr-204 GET)
 *   - Streaming message turns (ReadableStream over POST; falls back to
 *     the non-streaming JSON helper when streaming isn't available)
 *   - Session-end teardown (ADR-0003 — drawer state destroyed on
 *     route-leave or SessionCompleted event)
 *   - Circuit-breaker — on 5xx or stream error we render a calm
 *     fallback message with a "try the hint ladder instead" CTA
 *   - Productive-failure debounce — `explain-step` intent disabled for
 *     15s after a wrong step submission (timer driven by parent)
 *
 * Per ADR-0003 NO drawer state is persisted across sessions. We explicitly
 * do NOT write to localStorage, sessionStorage, or a pinia store with
 * persistence plugins.
 *
 * Per persona-redteam: the rendered message stream is passed through
 * `containsAnswerLeak()` as defense-in-depth on top of the server guard.
 */

import { computed, ref } from 'vue'
import { getTutorContext } from '@/api/sessions'
import type {
  TutorContextResponseDto,
  TutorTurnRequest,
} from '@/api/types/common'
import { useAuthStore } from '@/stores/authStore'

export interface SidekickMessage {
  id: string
  role: 'user' | 'assistant' | 'system'
  content: string
  createdAt: string
  streaming: boolean

  /** True when defense-in-depth leak detector tripped on this message. */
  leakRedacted: boolean
}

export interface UseSidekickOptions {
  sessionId: string
}

/**
 * Defense-in-depth: returns true when the text contains an MCQ-letter
 * disclosure ("the answer is A", "option B is correct", etc.) or a
 * numeric-answer disclosure ("the answer is 42"). Pattern list is
 * conservative — false positives land on the server-side scrubber.
 */
export function containsAnswerLeak(text: string): boolean {
  if (!text)
    return false

  const patterns: RegExp[] = [
    /\banswer\s+is\s+[A-D]\b/i,
    /\boption\s+[A-D]\s+is\s+correct\b/i,
    /\bchoose\s+[A-D]\b/i,
    /\bthe\s+correct\s+choice\s+is\s+[A-D]\b/i,
    /\bthe\s+answer\s+is\s+\d+(\.\d+)?\b/i,
  ]

  return patterns.some(p => p.test(text))
}

export function useSidekick(opts: UseSidekickOptions) {
  const isOpen = ref(false)
  const context = ref<TutorContextResponseDto | null>(null)
  const contextLoading = ref(false)
  const contextError = ref<string | null>(null)
  const messages = ref<SidekickMessage[]>([])
  const streaming = ref(false)
  const circuitBroken = ref(false)

  /**
   * Monotonic counter bumped each time we start a new stream; used to
   * abort any in-flight write back into `messages` if the drawer closes
   * mid-stream.
   */
  const streamGeneration = ref(0)

  /**
   * Productive-failure debounce — set to Date.now()+15000 after a wrong
   * step submission is reported. `explainStepEnabled` reads from here.
   */
  const debounceUntilMs = ref<number>(0)

  /** Re-evaluated on every tick in the parent — pure computed of wall-clock. */
  const explainStepEnabled = computed(() => Date.now() >= debounceUntilMs.value)

  /** Open the drawer + fetch context. Idempotent — cheap to call again. */
  async function open(): Promise<void> {
    isOpen.value = true
    if (context.value)
      return
    await refreshContext()
  }

  /** Close the drawer + abort any in-flight stream. */
  function close(): void {
    isOpen.value = false

    // Bump generation so any in-flight stream-reader drops its writes.
    streamGeneration.value += 1
    streaming.value = false
  }

  /** Toggle the drawer. Returns the new isOpen value. */
  async function toggle(): Promise<boolean> {
    if (isOpen.value)
      close()
    else await open()

    return isOpen.value
  }

  /** Refresh the tutor context. Called on open + when the runner signals a state change. */
  async function refreshContext(): Promise<void> {
    contextLoading.value = true
    contextError.value = null
    try {
      context.value = await getTutorContext(opts.sessionId)
    }
    catch (err) {
      const anyErr = err as { statusCode?: number; status?: number; message?: string }
      const status = anyErr.statusCode ?? anyErr.status ?? 0

      contextError.value = anyErr.message || 'tutor_context_failed'
      if (status >= 500)
        circuitBroken.value = true
    }
    finally {
      contextLoading.value = false
    }
  }

  /** Record a wrong-step event from the parent; starts the 15s debounce. */
  function noteWrongStep(): void {
    debounceUntilMs.value = Date.now() + 15000
  }

  /** Fully tear down drawer state (ADR-0003 session-end). */
  function teardown(): void {
    close()
    context.value = null
    messages.value = []
    circuitBroken.value = false
    streamGeneration.value += 1
    debounceUntilMs.value = 0
  }

  /**
   * Send a user turn — streams assistant tokens into `messages` as they
   * arrive. Uses raw fetch so we can consume a ReadableStream body.
   * ofetch does not expose a cancellation-friendly stream primitive.
   *
   * `turn.stepIndex` is only relevant when `turn.intent==='explain_step'`.
   */
  async function sendTurn(turn: TutorTurnRequest): Promise<void> {
    if (streaming.value)
      return

    // Honor productive-failure debounce for explain-step.
    if (turn.intent === 'explain_step' && !explainStepEnabled.value)
      return

    const gen = ++streamGeneration.value

    streaming.value = true

    // Append the user message immediately.
    if (turn.userMessage) {
      messages.value.push({
        id: `user-${gen}-${Date.now()}`,
        role: 'user',
        content: turn.userMessage,
        createdAt: new Date().toISOString(),
        streaming: false,
        leakRedacted: false,
      })
    }

    // Placeholder assistant bubble that token-chunks append into.
    const assistantIdx = messages.value.length

    messages.value.push({
      id: `assistant-${gen}-${Date.now()}`,
      role: 'assistant',
      content: '',
      createdAt: new Date().toISOString(),
      streaming: true,
      leakRedacted: false,
    })

    try {
      const auth = useAuthStore()

      const headers = new Headers({
        'Accept': 'text/event-stream',
        'Content-Type': 'application/json',
      })

      if (auth.idToken)
        headers.set('Authorization', `Bearer ${auth.idToken}`)

      const response = await fetch(
        `/api/v1/sessions/${encodeURIComponent(opts.sessionId)}/tutor-turn`,
        {
          method: 'POST',
          headers,
          body: JSON.stringify(turn),
        },
      )

      if (!response.ok) {
        if (response.status >= 500)
          circuitBroken.value = true
        throw new Error(`tutor_turn_http_${response.status}`)
      }

      const reader = response.body?.getReader()
      if (!reader) {
        // Non-streaming path — read the whole body as text.
        const text = await response.text()

        applyChunk(assistantIdx, gen, text)
        finishAssistantBubble(assistantIdx, gen)

        return
      }

      const decoder = new TextDecoder()
      let buffer = ''

      while (true) {
        const { value, done } = await reader.read()
        if (done)
          break
        if (streamGeneration.value !== gen) {
          // Drawer closed or a newer turn started — bail out.
          try {
            await reader.cancel()
          }
          catch { /* ignore */ }

          return
        }
        buffer += decoder.decode(value, { stream: true })

        // SSE-style: split on double-newline boundaries.
        const parts = buffer.split('\n\n')

        buffer = parts.pop() ?? ''
        for (const part of parts) {
          const dataLine = part.split('\n').find(l => l.startsWith('data:'))
          if (!dataLine)
            continue
          const payload = dataLine.slice(5).trim()
          if (payload === '[DONE]')
            break
          applyChunk(assistantIdx, gen, payload)
        }
      }

      if (buffer.trim())
        applyChunk(assistantIdx, gen, buffer.trim())
      finishAssistantBubble(assistantIdx, gen)
    }
    catch {
      if (streamGeneration.value === gen) {
        circuitBroken.value = true

        const bubble = messages.value[assistantIdx]
        if (bubble) {
          bubble.streaming = false
          bubble.content = ''
        }
      }
    }
    finally {
      if (streamGeneration.value === gen)
        streaming.value = false
    }
  }

  function applyChunk(idx: number, gen: number, chunk: string): void {
    if (streamGeneration.value !== gen)
      return
    const bubble = messages.value[idx]
    if (!bubble)
      return
    bubble.content += chunk
    if (containsAnswerLeak(bubble.content)) {
      // Defense-in-depth: blank the content and mark as redacted.
      bubble.content = ''
      bubble.leakRedacted = true
    }
  }

  function finishAssistantBubble(idx: number, gen: number): void {
    if (streamGeneration.value !== gen)
      return
    const bubble = messages.value[idx]
    if (bubble)
      bubble.streaming = false
  }

  return {
    isOpen,
    context,
    contextLoading,
    contextError,
    messages,
    streaming,
    circuitBroken,
    explainStepEnabled,
    open,
    close,
    toggle,
    refreshContext,
    noteWrongStep,
    teardown,
    sendTurn,
  }
}
