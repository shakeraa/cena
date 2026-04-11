import { HttpResponse, http } from 'msw'

/**
 * MSW handlers for the student `/api/sessions/*` endpoint group from
 * STB-01 + STB-01b (Phase 1 stub).
 *
 * These mock responses let the student web dev loop work against a
 * deterministic backend without running `Cena.Api.Host`. Mirrors the
 * same canned question set as `SessionEndpoints.cs` so front-end + back-end
 * stay in sync.
 *
 * STU-W-06 wires these into the /session flow.
 */

interface CannedQuestion {
  questionId: string
  prompt: string
  choices: string[]
  correctAnswer: string
  subject: string
}

const CANNED: CannedQuestion[] = [
  {
    questionId: 'q_001',
    prompt: 'What is 12 × 8?',
    choices: ['92', '96', '104', '108'],
    correctAnswer: '96',
    subject: 'Mathematics',
  },
  {
    questionId: 'q_002',
    prompt: 'Solve for x: 2x + 5 = 15',
    choices: ['5', '10', '15', '20'],
    correctAnswer: '5',
    subject: 'Mathematics',
  },
  {
    questionId: 'q_003',
    prompt: 'What is the derivative of x²?',
    choices: ['x', '2x', 'x²', '2'],
    correctAnswer: '2x',
    subject: 'Mathematics',
  },
  {
    questionId: 'q_004',
    prompt: 'What is the chemical symbol for water?',
    choices: ['H2O', 'CO2', 'O2', 'NaCl'],
    correctAnswer: 'H2O',
    subject: 'Chemistry',
  },
  {
    questionId: 'q_005',
    prompt: 'What is the speed of light approximately?',
    choices: ['300,000 km/s', '150,000 km/s', '1,000,000 km/s', '100,000 km/s'],
    correctAnswer: '300,000 km/s',
    subject: 'Physics',
  },
]

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
}

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
    }
    sessionStore.set(sessionId, state)
  }

  return state
}

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
    }

    sessionStore.set(sessionId, state)
    activeStudentSessionId = sessionId

    return HttpResponse.json({
      sessionId,
      hubGroupName: `session-${sessionId}`,
      firstQuestionId: CANNED[0].questionId,
    })
  }),

  http.get('/api/sessions/active', () => {
    if (!activeStudentSessionId)
      return HttpResponse.json(null, { status: 200 })

    const state = sessionStore.get(activeStudentSessionId)
    if (!state)
      return HttpResponse.json(null, { status: 200 })

    const elapsedMs = Date.now() - state.startedAt
    const progressPercent = Math.min(100, Math.round((state.questionIndex / CANNED.length) * 100))
    const currentQuestionId = state.questionIndex < CANNED.length ? CANNED[state.questionIndex].questionId : null

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

    if (state.questionIndex >= CANNED.length) {
      return HttpResponse.json(
        { error: 'Session already complete' },
        { status: 404 },
      )
    }

    const q = CANNED[state.questionIndex]

    return HttpResponse.json({
      questionId: q.questionId,
      questionIndex: state.questionIndex,
      totalQuestions: CANNED.length,
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
    const currentQ = CANNED[state.questionIndex]

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
    state.questionIndex += 1

    const nextQuestionId = state.questionIndex < CANNED.length
      ? CANNED[state.questionIndex].questionId
      : null

    return HttpResponse.json({
      correct,
      feedback: correct ? 'Correct! Great work.' : `Not quite — the answer was "${currentQ.correctAnswer}".`,
      xpAwarded,
      masteryDelta: correct ? 0.05 : -0.02,
      nextQuestionId,
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
