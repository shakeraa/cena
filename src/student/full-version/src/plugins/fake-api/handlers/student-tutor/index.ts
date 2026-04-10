import { HttpResponse, http } from 'msw'

/**
 * MSW handlers for the student `/api/tutor/*` endpoint group from STB-04.
 *
 * These mock responses let the student web dev loop work against a
 * deterministic backend without running `Cena.Api.Host`. In production
 * the MSW worker is NOT registered, so real requests pass through.
 *
 * STU-W-08 wires these into the /tutor pages.
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
      content: 'Great question! For x² − 5x + 6 = 0, we have a=1, b=−5, c=6. Using the quadratic formula: x = (5 ± √(25 − 24)) / 2 = (5 ± 1) / 2, which gives x = 3 or x = 2. (STB-04b will wire real LLM streaming.)',
      createdAt: new Date(now.getTime() - 3 * 86400_000 + 30_000).toISOString(),
      model: 'stub-llm-v1',
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
      content: 'Great question! If b² − 4ac is negative, the equation has no real solutions — only complex ones involving the imaginary unit i. (STB-04b will wire real LLM streaming.)',
      createdAt: new Date(now.getTime() - 1 * 3600_000 + 20_000).toISOString(),
      model: 'stub-llm-v1',
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
      content: 'Great question! Plants use sunlight in a process called photosynthesis, where chlorophyll in their leaves captures light energy and converts CO₂ and water into glucose and oxygen. (STB-04b will wire real LLM streaming.)',
      createdAt: new Date(now.getTime() - 5 * 86400_000 + 45_000).toISOString(),
      model: 'stub-llm-v1',
    },
  ])
}

seed()

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
      content: 'Great question! (STB-04b will wire real LLM streaming.) For now, here\'s a stub reply that echoes the spirit of your question so you can test the chat UI end-to-end.',
      createdAt: new Date(now.getTime() + 400).toISOString(),
      model: 'stub-llm-v1',
    }

    existing.push(userMsg, assistantMsg)
    messagesByThreadId.set(threadId, existing)

    const thread = threadsById.get(threadId)
    if (thread) {
      thread.messageCount = existing.length
      thread.updatedAt = now.toISOString()
    }

    // Phase 1 returns only the assistant reply — the client already has
    // the user message it just sent.
    return HttpResponse.json({
      messageId: assistantMsg.messageId,
      role: 'assistant',
      content: assistantMsg.content,
      createdAt: assistantMsg.createdAt,
      status: 'complete',
    })
  }),
]
