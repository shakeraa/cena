/**
 * prr-205 — useHintLadder composable.
 *
 * Client-side state machine for the server-authoritative hint ladder
 * (ADR-0045, prr-203 endpoint). The server owns rung progression — this
 * composable only orchestrates the request + keeps a local view of
 * rungs received so the ladder component can render them in order.
 *
 * Responsibilities:
 *   - Call `postHintNext` for the active (session, question) pair
 *   - Append the resulting rung to an in-memory array
 *   - Track loading / error / end-of-ladder state for the UI
 *   - Reset when the active question changes (hints are per-question,
 *     not per-session)
 *   - Expose BKT-bucket-aware `shouldAutoShow`: true for low/mid mastery,
 *     false for high (expertise-reversal gate — component defaults to
 *     collapsed and surfaces only via "I'm stuck" affordance).
 *
 * Citations:
 *   - Kalyuga et al. (2003) "The Expertise Reversal Effect"
 *     DOI: 10.1207/S15326985EP3801_4
 *   - ADR-0045 §3 server-authoritative ladder
 */

import { computed, ref, watch } from 'vue'
import { postHintNext } from '@/api/sessions'
import type { HintLadderResponseDto } from '@/api/types/common'

export interface HintLadderRung {
  hintLevel: number
  hintText: string
  rungSource: string
}

/**
 * Translate a HintLadderResponseDto into the lightweight rung shape the
 * HintLadder.vue component expects. We intentionally discard the
 * `maxRungReached` field — the component renders only the rungs it has
 * actually received, which matches the student's lived experience.
 */
function toRung(dto: HintLadderResponseDto): HintLadderRung {
  return {
    hintLevel: dto.rung,
    hintText: dto.body,
    rungSource: dto.rungSource,
  }
}

export interface UseHintLadderOptions {
  sessionId: string

  /** Reactive current question id; ladder resets on change. */
  questionId: () => string | null | undefined

  /** Reactive BKT mastery bucket. Drives auto-show / expertise-reversal. */
  masteryBucket?: () => string | null | undefined
}

export function useHintLadder(opts: UseHintLadderOptions) {
  const rungs = ref<HintLadderRung[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  /**
   * Tracks whether the server has told us the ladder is exhausted.
   * Once false, UI hides the "More help" button so no further requests
   * are sent for this question.
   */
  const nextRungAvailable = ref(true)

  /**
   * Student-controlled collapsed state. Defaults to true for high-mastery
   * students (expertise-reversal). Any student can click "I'm stuck" to
   * surface the ladder, which also sets expanded=true.
   */
  const expanded = ref(false)

  const shouldAutoShow = computed(() => {
    const bucket = opts.masteryBucket?.() ?? 'unknown'

    // Auto-show for low/mid — scaffolds help novices. Collapse for high —
    // experts shouldn't be fed scaffolds unless they explicitly ask.
    return bucket === 'low' || bucket === 'mid' || bucket === 'unknown'
  })

  /** True when ladder is surfaced (either auto-show or explicit "I'm stuck"). */
  const visible = computed(() => expanded.value || shouldAutoShow.value)

  /** Request the next rung from the server. No-op if already loading or exhausted. */
  async function requestNext(): Promise<void> {
    const qid = opts.questionId()
    if (!qid)
      return
    if (loading.value)
      return
    if (!nextRungAvailable.value && rungs.value.length > 0)
      return

    loading.value = true
    error.value = null
    try {
      const dto = await postHintNext(opts.sessionId, qid)

      rungs.value.push(toRung(dto))
      nextRungAvailable.value = dto.nextRungAvailable

      // Explicit student-driven advance always expands the ladder.
      expanded.value = true
    }
    catch (err) {
      const anyErr = err as { statusCode?: number; status?: number; message?: string }
      const status = anyErr.statusCode ?? anyErr.status
      if (status === 404) {
        // 404 is "ladder exhausted" — close the button cleanly, no banner.
        nextRungAvailable.value = false
      }
      else {
        error.value = anyErr.message || 'hint_request_failed'
      }
    }
    finally {
      loading.value = false
    }
  }

  /** Surface the ladder via explicit "I'm stuck". */
  function surface(): void {
    expanded.value = true
  }

  /** Reset state for a fresh question. Called automatically on questionId change. */
  function reset(): void {
    rungs.value = []
    loading.value = false
    error.value = null
    nextRungAvailable.value = true
    expanded.value = false
  }

  watch(() => opts.questionId(), (newId, oldId) => {
    if (newId !== oldId)
      reset()
  })

  return {
    rungs,
    loading,
    error,
    nextRungAvailable,
    expanded,
    visible,
    shouldAutoShow,
    requestNext,
    surface,
    reset,
  }
}
