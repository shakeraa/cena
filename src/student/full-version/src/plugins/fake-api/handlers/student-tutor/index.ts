import { HttpResponse, http } from 'msw'

/**
 * MSW handlers for the student `/api/tutor/*` endpoint group.
 *
 * These mock responses let the student web dev loop work against a
 * deterministic backend without running the student API host. In
 * production the MSW worker is NOT registered, so real requests pass
 * through to the hardened LLM-backed endpoints on Cena.Student.Api.Host.
 *
 * Rules for mock content in this file (enforced by the repo-wide grep
 * guard in scripts/check-fake-api-no-leakage.mjs — run it before commit):
 *
 *   - Reply text must read like a plausible tutor response that a
 *     designer could drop into a demo video without edits.
 *   - No internal ticket identifiers or task-tracker prefixes appear in
 *     any string literal.
 *   - No meta commentary about the mock itself, no references to future
 *     wiring phases, no developer scaffolding vocabulary.
 *   - Any string that ends up in the chat UI is reviewed as user-facing
 *     copy, not as developer scratchwork.
 */

interface StoredThread {
  threadId: string
  title: string
  subject: string | null
  topic: string | null
  createdAt: string
  updatedAt: string
  messageCount: number
  isArchived: boolean
}

interface StoredMessage {
  messageId: string
  threadId: string
  role: 'user' | 'assistant' | 'system'
  content: string
  createdAt: string
  model: string | null
}

// In-memory store so POSTs during a session show up in subsequent GETs.
const threadsById = new Map<string, StoredThread>()
const messagesByThreadId = new Map<string, StoredMessage[]>()

// Model label attached to mock assistant replies. Kept neutral so it does
// not self-identify as a mock in the UI; the UI only shows this value in
// developer-facing debug panels.
const MOCK_ASSISTANT_MODEL = 'cena-tutor-dev'

function seed() {
  const now = new Date()

  const seedThreads: StoredThread[] = [
    {
      threadId: 'th-1',
      title: 'Help with quadratic equations',
      subject: 'math',
      topic: 'algebra',
      createdAt: new Date(now.getTime() - 3 * 86400_000).toISOString(),
      updatedAt: new Date(now.getTime() - 1 * 3600_000).toISOString(),
      messageCount: 4,
      isArchived: false,
    },
    {
      threadId: 'th-2',
      title: 'Photosynthesis questions',
      subject: 'biology',
      topic: 'plants',
      createdAt: new Date(now.getTime() - 5 * 86400_000).toISOString(),
      updatedAt: new Date(now.getTime() - 2 * 86400_000).toISOString(),
      messageCount: 2,
      isArchived: false,
    },
  ]

  for (const t of seedThreads)
    threadsById.set(t.threadId, t)

  messagesByThreadId.set('th-1', [
    {
      messageId: 'msg-1',
      threadId: 'th-1',
      role: 'user',
      content: 'How do I solve x² − 5x + 6 = 0 using the quadratic formula?',
      createdAt: new Date(now.getTime() - 3 * 86400_000).toISOString(),
      model: null,
    },
    {
      messageId: 'msg-2',
      threadId: 'th-1',
      role: 'assistant',
      content: 'Good one. Start by matching the general form ax² + bx + c = 0. Here a = 1, b = −5, c = 6. The quadratic formula is x = (−b ± √(b² − 4ac)) / (2a). Plug in: x = (5 ± √(25 − 24)) / 2 = (5 ± 1) / 2. That gives two solutions: x = 3 and x = 2. You can always double-check by substituting each back into the original equation — both should make the left side equal zero.',
      createdAt: new Date(now.getTime() - 3 * 86400_000 + 30_000).toISOString(),
      model: MOCK_ASSISTANT_MODEL,
    },
    {
      messageId: 'msg-3',
      threadId: 'th-1',
      role: 'user',
      content: 'What if the discriminant is negative?',
      createdAt: new Date(now.getTime() - 1 * 3600_000).toISOString(),
      model: null,
    },
    {
      messageId: 'msg-4',
      threadId: 'th-1',
      role: 'assistant',
      content: 'Great follow-up. The discriminant is the part under the square root: b² − 4ac. If it comes out negative, the equation has no real-number solutions — the parabola never crosses the x-axis. The solutions still exist, but they are complex numbers involving the imaginary unit i (where i² = −1). For most classes before late high school, the answer in that case is simply "no real solutions".',
      createdAt: new Date(now.getTime() - 1 * 3600_000 + 20_000).toISOString(),
      model: MOCK_ASSISTANT_MODEL,
    },
  ])

  messagesByThreadId.set('th-2', [
    {
      messageId: 'msg-5',
      threadId: 'th-2',
      role: 'user',
      content: 'Why do plants need sunlight?',
      createdAt: new Date(now.getTime() - 5 * 86400_000).toISOString(),
      model: null,
    },
    {
      messageId: 'msg-6',
      threadId: 'th-2',
      role: 'assistant',
      content: 'Plants use sunlight as the energy source for photosynthesis. The chlorophyll in their leaves absorbs light — mostly in the red and blue parts of the spectrum — and uses that energy to split water molecules and drive a chemical reaction that turns carbon dioxide from the air and water from the soil into glucose and oxygen. The glucose becomes the plant\'s food and building material; the oxygen is released back into the air. Without sunlight, that chain stops, and the plant eventually runs out of stored sugars.',
      createdAt: new Date(now.getTime() - 5 * 86400_000 + 45_000).toISOString(),
      model: MOCK_ASSISTANT_MODEL,
    },
  ])
}

seed()

/**
 * Lightweight local reply generator for the dev loop. It does not attempt
 * to actually answer the user's question — instead it produces a short,
 * on-topic acknowledgement that reads like the opening sentences of a real
 * tutor response. The real assistant content comes from the hardened
 * `/api/tutor/messages` endpoint in production, where MSW is not active.
 */
function generateMockReply(userContent: string): string {
  const trimmed = userContent.trim().replace(/\s+/g, ' ')
  const preview = trimmed.length > 140 ? `${trimmed.slice(0, 140)}…` : trimmed

  if (!preview)
    return 'Could you share a little more about what you\'re trying to work out? A single sentence describing the problem is usually enough for me to start helping.'

  return [
    `Let\'s take that step by step. You asked: "${preview}"`,
    'First, identify what the question is actually asking you to find, then list the pieces of information you already have. From there we can pick a method — in most cases that\'s either a direct formula, a worked example, or a short back-and-forth where I check your reasoning. Walk me through your first attempt and tell me where you get stuck, and I\'ll nudge you from there.',
  ].join('\n\n')
}

export const handlerStudentTutor = [
  http.get('/api/tutor/threads', () => {
    const items = Array.from(threadsById.values())
      .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt))

    return HttpResponse.json({
      items,
      totalCount: items.length,
    })
  }),

  http.post('/api/tutor/threads', async ({ request }) => {
    const body = await request.json() as {
      title?: string
      subject?: string
      topic?: string
      initialMessage?: string
    } | null

    const threadId = `th-${Math.random().toString(36).slice(2, 10)}`
    const now = new Date().toISOString()

    const thread: StoredThread = {
      threadId,
      title: body?.title || body?.initialMessage?.slice(0, 60) || 'New conversation',
      subject: body?.subject || null,
      topic: body?.topic || null,
      createdAt: now,
      updatedAt: now,
      messageCount: 0,
      isArchived: false,
    }

    threadsById.set(threadId, thread)
    messagesByThreadId.set(threadId, [])

    return HttpResponse.json({
      threadId,
      title: thread.title,
      createdAt: now,
    })
  }),

  http.get('/api/tutor/threads/:threadId/messages', ({ params }) => {
    const threadId = params.threadId as string
    const messages = messagesByThreadId.get(threadId) || []

    return HttpResponse.json({
      threadId,
      messages,
      hasMore: false,
    })
  }),

  http.post('/api/tutor/threads/:threadId/messages', async ({ params, request }) => {
    const threadId = params.threadId as string
    const body = await request.json() as { content: string }

    const existing = messagesByThreadId.get(threadId) || []
    const now = new Date()

    const userMsg: StoredMessage = {
      messageId: `msg-${Math.random().toString(36).slice(2, 10)}`,
      threadId,
      role: 'user',
      content: body.content,
      createdAt: now.toISOString(),
      model: null,
    }

    const assistantMsg: StoredMessage = {
      messageId: `msg-${Math.random().toString(36).slice(2, 10)}`,
      threadId,
      role: 'assistant',
      content: generateMockReply(body.content ?? ''),
      createdAt: new Date(now.getTime() + 400).toISOString(),
      model: MOCK_ASSISTANT_MODEL,
    }

    existing.push(userMsg, assistantMsg)
    messagesByThreadId.set(threadId, existing)

    const thread = threadsById.get(threadId)
    if (thread) {
      thread.messageCount = existing.length
      thread.updatedAt = now.toISOString()
    }

    // The POST response returns only the assistant reply — the client
    // already has the user message it just sent.
    return HttpResponse.json({
      messageId: assistantMsg.messageId,
      role: 'assistant',
      content: assistantMsg.content,
      createdAt: assistantMsg.createdAt,
      status: 'complete',
    })
  }),
]
