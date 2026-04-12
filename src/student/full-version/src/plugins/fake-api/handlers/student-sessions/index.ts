import { HttpResponse, http } from 'msw'

/**
 * MSW handlers for the student `/api/sessions/*` endpoint group.
 *
 * FIND-pedagogy-011: These mock responses produce realistic pedagogical
 * data matching the real `SessionAnswerResponseDto` contract from
 * `Cena.Api.Contracts/Sessions/SessionDtos.cs`. The response includes:
 *   - `explanation`: authored worked explanation (not just "Correct!")
 *   - `distractorRationale`: per-option rationale for wrong answers
 *   - `masteryDelta`: computed via a deterministic BKT shim matching
 *     `BktService.Update` from `Cena.Actors.Services`
 *
 * DEV-only (MSW is stripped in production per FIND-arch-017).
 */

// ─────────────────────────────────────────────────────────────────────
// BKT shim — deterministic Bayesian Knowledge Tracing matching
// Cena.Actors.Services.BktService.Update (Corbett & Anderson 1994)
// ─────────────────────────────────────────────────────────────────────

interface BktParams {
  pLearning: number
  pSlip: number
  pGuess: number
  pForget: number
}

/** Default BKT parameters matching BktService.BktParameters.Default */
const BKT_DEFAULTS: BktParams = {
  pLearning: 0.10,
  pSlip: 0.05,
  pGuess: 0.20,
  pForget: 0.02,
}

const BKT_MIN_P = 0.01
const BKT_MAX_P = 0.99

function clampBkt(value: number): number {
  if (value < BKT_MIN_P) return BKT_MIN_P
  if (value > BKT_MAX_P) return BKT_MAX_P
  return value
}

function clampDenominator(value: number): number {
  if (value < BKT_MIN_P) return BKT_MIN_P
  return value
}

/**
 * Performs a single BKT update step. Mirrors `BktService.Update` exactly:
 *   1. P(correct) = P(L) * (1 - P(S)) + (1 - P(L)) * P(G)
 *   2. Bayes posterior given observation
 *   3. Learning transition: P(L_n) = P(L_n|obs) + (1 - P(L_n|obs)) * P(T)
 *   4. Micro-forgetting: P(L_n) * (1 - P(F))
 *   5. Clamp to [0.01, 0.99]
 */
export function bktUpdate(
  priorMastery: number,
  isCorrect: boolean,
  params: BktParams = BKT_DEFAULTS,
): number {
  const prior = clampBkt(priorMastery)
  const { pSlip, pGuess, pLearning, pForget } = params

  // Step 1: P(correct) and P(incorrect) from current mastery
  let pCorrect = prior * (1.0 - pSlip) + (1.0 - prior) * pGuess
  let pIncorrect = 1.0 - pCorrect

  pCorrect = clampDenominator(pCorrect)
  pIncorrect = clampDenominator(pIncorrect)

  // Step 2: Posterior given observation (Bayes update)
  let pLearned: number
  if (isCorrect) {
    pLearned = prior * (1.0 - pSlip) / pCorrect
  }
  else {
    pLearned = prior * pSlip / pIncorrect
  }

  // Step 3: Account for learning transition
  let posterior = pLearned + (1.0 - pLearned) * pLearning

  // Step 4: Apply micro-forgetting factor
  posterior = posterior * (1.0 - pForget)

  // Step 5: Clamp to valid range
  return clampBkt(posterior)
}

// ─────────────────────────────────────────────────────────────────────
// Dev question bank — rich question data with authored explanations
// and per-distractor rationales, matching QuestionDocument fields.
// ─────────────────────────────────────────────────────────────────────

interface DevQuestion {
  questionId: string
  prompt: string
  choices: string[]
  correctAnswer: string
  subject: string
  /** Authored worked explanation (QuestionDocument.Explanation). */
  explanation: string
  /**
   * Per-distractor rationale map. Keys are option values; values are
   * the rationale string shown when the student picks that wrong option.
   * Matches QuestionDocument.DistractorRationales.
   */
  distractorRationales: Record<string, string>
}

const devQuestions: DevQuestion[] = [
  {
    questionId: 'q_001',
    prompt: 'What is 12 \u00d7 8?',
    choices: ['92', '96', '104', '108'],
    correctAnswer: '96',
    subject: 'Mathematics',
    explanation: 'To multiply 12 by 8, break it into (10 \u00d7 8) + (2 \u00d7 8) = 80 + 16 = 96. This uses the distributive property of multiplication over addition.',
    distractorRationales: {
      '92': 'You may have computed 12 \u00d7 8 as 12 \u00d7 7 + 4 = 88, then added 4 by mistake. Double-check each partial product.',
      '104': 'This is 13 \u00d7 8. Make sure you are multiplying 12, not 13.',
      '108': 'This is 12 \u00d7 9. Check that you used 8 as the multiplier, not 9.',
    },
  },
  {
    questionId: 'q_002',
    prompt: 'Solve for x: 2x + 5 = 15',
    choices: ['5', '10', '15', '20'],
    correctAnswer: '5',
    subject: 'Mathematics',
    explanation: 'Subtract 5 from both sides: 2x = 10. Then divide both sides by 2: x = 5. Always isolate the variable by performing inverse operations.',
    distractorRationales: {
      '10': 'You found 2x = 10 correctly but forgot to divide by 2. The equation is 2x = 10, so x = 10 / 2 = 5.',
      '15': 'You may have subtracted 5 from the wrong side or not subtracted at all. Start by subtracting 5 from both sides.',
      '20': 'You added 5 to 15 instead of subtracting. To isolate 2x, subtract 5 from both sides, giving 2x = 10.',
    },
  },
  {
    questionId: 'q_003',
    prompt: 'What is the derivative of x\u00b2?',
    choices: ['x', '2x', 'x\u00b2', '2'],
    correctAnswer: '2x',
    subject: 'Mathematics',
    explanation: 'Using the power rule d/dx[x^n] = n\u00b7x^(n-1), with n = 2: d/dx[x\u00b2] = 2\u00b7x^(2-1) = 2x. The power rule is the fundamental differentiation rule for polynomial terms.',
    distractorRationales: {
      'x': 'You reduced the exponent but forgot to bring it down as a coefficient. The power rule requires multiplying by the original exponent.',
      'x\u00b2': 'The derivative of x\u00b2 is not x\u00b2 itself \u2014 that would mean the function equals its own rate of change (which is the property of e^x, not x\u00b2).',
      '2': 'You applied the power rule coefficient correctly (2) but dropped the x term. The derivative is 2\u00b7x^1 = 2x, not just 2.',
    },
  },
  {
    questionId: 'q_004',
    prompt: 'What is the chemical symbol for water?',
    choices: ['H2O', 'CO2', 'O2', 'NaCl'],
    correctAnswer: 'H2O',
    subject: 'Chemistry',
    explanation: 'Water is composed of two hydrogen atoms and one oxygen atom bonded covalently, giving the molecular formula H\u2082O. The H\u2013O\u2013H bond angle is approximately 104.5\u00b0.',
    distractorRationales: {
      'CO2': 'CO\u2082 is carbon dioxide, a gas exhaled during respiration. Water contains hydrogen and oxygen, not carbon.',
      'O2': 'O\u2082 is molecular oxygen (the gas we breathe). Water requires hydrogen atoms bonded to oxygen.',
      'NaCl': 'NaCl is sodium chloride (table salt), an ionic compound. Water is a covalent molecule of hydrogen and oxygen.',
    },
  },
  {
    questionId: 'q_005',
    prompt: 'What is the speed of light approximately?',
    choices: ['300,000 km/s', '150,000 km/s', '1,000,000 km/s', '100,000 km/s'],
    correctAnswer: '300,000 km/s',
    subject: 'Physics',
    explanation: 'The speed of light in a vacuum is approximately 299,792 km/s, commonly rounded to 300,000 km/s (or 3 \u00d7 10\u2078 m/s). This is a fundamental constant denoted c in physics equations like E = mc\u00b2.',
    distractorRationales: {
      '150,000 km/s': 'This is half the actual value. The speed of light is approximately 300,000 km/s, not 150,000 km/s.',
      '1,000,000 km/s': 'This is more than three times the actual value. Nothing with mass can even reach 300,000 km/s, let alone exceed it.',
      '100,000 km/s': 'This is one-third of the actual value. The speed of light is approximately 3 \u00d7 10\u2075 km/s = 300,000 km/s.',
    },
  },
]

// ─────────────────────────────────────────────────────────────────────
// Session state — tracks mastery per-session for BKT computation
// ─────────────────────────────────────────────────────────────────────

interface SessionState {
  sessionId: string
  questionIndex: number
  correctCount: number
  wrongCount: number
  totalXp: number
  startedAt: number
  subjects: string[]
  mode: string
  durationMinutes: number
  /** Current BKT mastery estimate for this session (starts at P_Initial). */
  currentMastery: number
}

const BKT_P_INITIAL = 0.10

// In-memory store so a session created via POST /start shows up on GETs
const sessionStore = new Map<string, SessionState>()
let activeStudentSessionId: string | null = null

function getOrCreateState(sessionId: string): SessionState {
  let state = sessionStore.get(sessionId)

  if (!state) {
    state = {
      sessionId,
      questionIndex: 0,
      correctCount: 0,
      wrongCount: 0,
      totalXp: 0,
      startedAt: Date.now(),
      subjects: ['math'],
      mode: 'practice',
      durationMinutes: 15,
      currentMastery: BKT_P_INITIAL,
    }
    sessionStore.set(sessionId, state)
  }

  return state
}

// ─────────────────────────────────────────────────────────────────────
// Exported MSW handlers
// ─────────────────────────────────────────────────────────────────────

export const handlerStudentSessions = [
  http.post('/api/sessions/start', async ({ request }) => {
    const body = await request.json() as {
      subjects: string[]
      durationMinutes: number
      mode: string
    }

    const sessionId = `s-${Math.random().toString(36).slice(2, 10)}`

    const state: SessionState = {
      sessionId,
      questionIndex: 0,
      correctCount: 0,
      wrongCount: 0,
      totalXp: 0,
      startedAt: Date.now(),
      subjects: body.subjects,
      mode: body.mode,
      durationMinutes: body.durationMinutes,
      currentMastery: BKT_P_INITIAL,
    }

    sessionStore.set(sessionId, state)
    activeStudentSessionId = sessionId

    return HttpResponse.json({
      sessionId,
      hubGroupName: `session-${sessionId}`,
      firstQuestionId: devQuestions[0].questionId,
    })
  }),

  http.get('/api/sessions/active', () => {
    if (!activeStudentSessionId)
      return HttpResponse.json(null, { status: 200 })

    const state = sessionStore.get(activeStudentSessionId)
    if (!state)
      return HttpResponse.json(null, { status: 200 })

    const progressPercent = Math.min(100, Math.round((state.questionIndex / devQuestions.length) * 100))
    const currentQuestionId = state.questionIndex < devQuestions.length ? devQuestions[state.questionIndex].questionId : null

    return HttpResponse.json({
      sessionId: state.sessionId,
      subjects: state.subjects,
      mode: state.mode,
      startedAt: new Date(state.startedAt).toISOString(),
      durationMinutes: state.durationMinutes,
      progressPercent,
      currentQuestionId,
    })
  }),

  http.get('/api/sessions/:sessionId/current-question', ({ params }) => {
    const sessionId = params.sessionId as string
    const state = getOrCreateState(sessionId)

    if (state.questionIndex >= devQuestions.length) {
      return HttpResponse.json(
        { error: 'Session already complete' },
        { status: 404 },
      )
    }

    const q = devQuestions[state.questionIndex]

    return HttpResponse.json({
      questionId: q.questionId,
      questionIndex: state.questionIndex,
      totalQuestions: devQuestions.length,
      prompt: q.prompt,
      questionType: 'multiple-choice',
      choices: q.choices,
      subject: q.subject,
      expectedTimeSeconds: 30,
    })
  }),

  http.post('/api/sessions/:sessionId/answer', async ({ params, request }) => {
    const sessionId = params.sessionId as string

    const body = await request.json() as {
      questionId: string
      answer: string
      timeSpentMs: number
    }

    const state = getOrCreateState(sessionId)
    const currentQ = devQuestions[state.questionIndex]

    if (!currentQ || currentQ.questionId !== body.questionId) {
      return HttpResponse.json(
        { error: 'Question index mismatch' },
        { status: 409 },
      )
    }

    const correct = body.answer === currentQ.correctAnswer
    const xpAwarded = correct ? 10 : 0

    if (correct)
      state.correctCount += 1
    else
      state.wrongCount += 1

    state.totalXp += xpAwarded

    // BKT mastery update — deterministic, matches BktService.Update
    const priorMastery = state.currentMastery
    const posteriorMastery = bktUpdate(priorMastery, correct)
    state.currentMastery = posteriorMastery
    const masteryDelta = posteriorMastery - priorMastery

    // Feedback label — short pill matching SessionEndpoints.BuildAnswerFeedback
    const feedback = correct ? 'Correct' : 'Not quite'

    // Distractor rationale — only for wrong answers, matching the real
    // backend logic in SessionEndpoints.BuildAnswerFeedback
    let distractorRationale: string | null = null
    if (!correct && body.answer) {
      const trimmed = body.answer.trim()
      if (currentQ.distractorRationales[trimmed]) {
        distractorRationale = currentQ.distractorRationales[trimmed]
      }
      else {
        // Case-insensitive fallback matching the .NET backend
        const lowerTrimmed = trimmed.toLowerCase()
        for (const [key, value] of Object.entries(currentQ.distractorRationales)) {
          if (key.toLowerCase() === lowerTrimmed) {
            distractorRationale = value
            break
          }
        }
      }
    }

    state.questionIndex += 1

    const nextQuestionId = state.questionIndex < devQuestions.length
      ? devQuestions[state.questionIndex].questionId
      : null

    // Response shape matches SessionAnswerResponseDto exactly:
    //   Correct, Feedback, XpAwarded, MasteryDelta, NextQuestionId,
    //   Explanation, DistractorRationale
    return HttpResponse.json({
      correct,
      feedback,
      xpAwarded,
      masteryDelta,
      nextQuestionId,
      explanation: currentQ.explanation,
      distractorRationale,
    })
  }),

  http.post('/api/sessions/:sessionId/complete', ({ params }) => {
    const sessionId = params.sessionId as string
    const state = getOrCreateState(sessionId)

    const durationSeconds = Math.round((Date.now() - state.startedAt) / 1000)
    const total = state.correctCount + state.wrongCount
    const accuracyPercent = total === 0 ? 0 : Math.round((state.correctCount / total) * 100)

    if (activeStudentSessionId === sessionId)
      activeStudentSessionId = null

    return HttpResponse.json({
      sessionId,
      totalCorrect: state.correctCount,
      totalWrong: state.wrongCount,
      totalXpAwarded: state.totalXp,
      accuracyPercent,
      durationSeconds,
    })
  }),
]
