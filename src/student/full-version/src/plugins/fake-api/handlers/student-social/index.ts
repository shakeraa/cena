import { HttpResponse, http } from 'msw'

/**
 * MSW handlers for the student `/api/social/*` endpoint group.
 *
 * These mock responses let the student web dev loop work without the
 * student API host. In production the MSW worker is NOT registered,
 * so real requests pass through to the deployed social endpoints.
 */

interface FeedItem {
  itemId: string
  kind: 'achievement' | 'milestone' | 'question' | 'announcement'
  authorStudentId: string
  authorDisplayName: string
  authorAvatarUrl: string | null
  title: string
  body: string | null
  postedAt: string
  reactionCount: number
  commentCount: number
}

interface PeerSolution {
  solutionId: string
  questionId: string
  authorStudentId: string
  authorDisplayName: string
  content: string
  upvoteCount: number
  downvoteCount: number
  postedAt: string
}

interface Friend {
  studentId: string
  displayName: string
  avatarUrl: string | null
  level: number
  streakDays: number
  isOnline: boolean
}

interface FriendRequest {
  requestId: string
  fromStudentId: string
  fromDisplayName: string
  fromAvatarUrl: string | null
  requestedAt: string
}

function makeFeed(): FeedItem[] {
  const now = Date.now()

  const items: FeedItem[] = [
    { itemId: 'f1', kind: 'achievement', authorStudentId: 'u-alex', authorDisplayName: 'Alex Chen', authorAvatarUrl: null, title: 'Earned the Quiz Master badge', body: 'Finally got 90% accuracy on 10 quizzes in a row!', postedAt: new Date(now - 30 * 60_000).toISOString(), reactionCount: 12, commentCount: 3 },
    { itemId: 'f2', kind: 'milestone', authorStudentId: 'u-priya', authorDisplayName: 'Priya Rao', authorAvatarUrl: null, title: 'Reached level 10 in Math', body: 'That calculus chain was brutal.', postedAt: new Date(now - 2 * 3600_000).toISOString(), reactionCount: 8, commentCount: 1 },
    { itemId: 'f3', kind: 'question', authorStudentId: 'u-jordan', authorDisplayName: 'Jordan Smith', authorAvatarUrl: null, title: 'Anyone else struggling with trigonometric identities?', body: 'Specifically the double-angle formulas — my brain is mush.', postedAt: new Date(now - 4 * 3600_000).toISOString(), reactionCount: 5, commentCount: 9 },
    { itemId: 'f4', kind: 'announcement', authorStudentId: 'u-teacher', authorDisplayName: 'Ms. Rivera (Teacher)', authorAvatarUrl: null, title: 'Quiz on Photosynthesis next Friday', body: 'Chapters 4–5 in the textbook. Study groups encouraged!', postedAt: new Date(now - 6 * 3600_000).toISOString(), reactionCount: 2, commentCount: 0 },
    { itemId: 'f5', kind: 'achievement', authorStudentId: 'u-sam', authorDisplayName: 'Sam Park', authorAvatarUrl: null, title: '14-day streak!', body: null, postedAt: new Date(now - 8 * 3600_000).toISOString(), reactionCount: 18, commentCount: 2 },
    { itemId: 'f6', kind: 'milestone', authorStudentId: 'u-riley', authorDisplayName: 'Riley Evans', authorAvatarUrl: null, title: 'Unlocked the Physics Core card chain', body: 'Halfway through now.', postedAt: new Date(now - 12 * 3600_000).toISOString(), reactionCount: 4, commentCount: 0 },
    { itemId: 'f7', kind: 'question', authorStudentId: 'u-casey', authorDisplayName: 'Casey Kim', authorAvatarUrl: null, title: 'Best way to memorize the periodic table?', body: 'I know the first row but blanking after that.', postedAt: new Date(now - 18 * 3600_000).toISOString(), reactionCount: 7, commentCount: 5 },
    { itemId: 'f8', kind: 'achievement', authorStudentId: 'u-maya', authorDisplayName: 'Maya Patel', authorAvatarUrl: null, title: 'Defeated the Algebra Overlord boss!', body: '5 attempts but finally got it.', postedAt: new Date(now - 22 * 3600_000).toISOString(), reactionCount: 14, commentCount: 4 },
  ]

  return items
}

const SOLUTIONS: PeerSolution[] = [
  { solutionId: 'sol-1', questionId: 'q_001', authorStudentId: 'u-priya', authorDisplayName: 'Priya Rao', content: 'I use the distributive property: 12 × 8 = (10 × 8) + (2 × 8) = 80 + 16 = 96. Much easier than memorizing.', upvoteCount: 15, downvoteCount: 0, postedAt: new Date(Date.now() - 2 * 86400_000).toISOString() },
  { solutionId: 'sol-2', questionId: 'q_003', authorStudentId: 'u-alex', authorDisplayName: 'Alex Chen', content: 'For x² the derivative is 2x. Just bring the exponent down and subtract 1 from it: x^(2-1) = x^1 = x, times 2 = 2x.', upvoteCount: 22, downvoteCount: 1, postedAt: new Date(Date.now() - 3 * 86400_000).toISOString() },
  { solutionId: 'sol-3', questionId: 'q_005', authorStudentId: 'u-jordan', authorDisplayName: 'Jordan Smith', content: 'The speed of light is ~3 × 10⁸ m/s which is 300,000 km/s. Worth memorizing!', upvoteCount: 9, downvoteCount: 0, postedAt: new Date(Date.now() - 5 * 86400_000).toISOString() },
  { solutionId: 'sol-4', questionId: 'q_002', authorStudentId: 'u-sam', authorDisplayName: 'Sam Park', content: 'Subtract 5 from both sides first: 2x = 10. Then divide by 2: x = 5. Always isolate the variable.', upvoteCount: 11, downvoteCount: 0, postedAt: new Date(Date.now() - 6 * 86400_000).toISOString() },
  { solutionId: 'sol-5', questionId: 'q_004', authorStudentId: 'u-maya', authorDisplayName: 'Maya Patel', content: 'Water = H₂O. 2 hydrogen atoms bonded to 1 oxygen. Easy to remember because it\'s the "2 Hs on O" rule.', upvoteCount: 6, downvoteCount: 2, postedAt: new Date(Date.now() - 7 * 86400_000).toISOString() },
]

const FRIENDS: Friend[] = [
  { studentId: 'u-alex', displayName: 'Alex Chen', avatarUrl: null, level: 10, streakDays: 15, isOnline: true },
  { studentId: 'u-priya', displayName: 'Priya Rao', avatarUrl: null, level: 9, streakDays: 22, isOnline: true },
  { studentId: 'u-jordan', displayName: 'Jordan Smith', avatarUrl: null, level: 7, streakDays: 4, isOnline: false },
  { studentId: 'u-sam', displayName: 'Sam Park', avatarUrl: null, level: 8, streakDays: 11, isOnline: false },
]

const pendingRequests: FriendRequest[] = [
  { requestId: 'req-1', fromStudentId: 'u-casey', fromDisplayName: 'Casey Kim', fromAvatarUrl: null, requestedAt: new Date(Date.now() - 3 * 3600_000).toISOString() },
  { requestId: 'req-2', fromStudentId: 'u-riley', fromDisplayName: 'Riley Evans', fromAvatarUrl: null, requestedAt: new Date(Date.now() - 1 * 86400_000).toISOString() },
]

/**
 * Per-item reaction state. Keyed by `${itemId}:${reactionType}`.
 * Tracks whether the current user has reacted and the running count
 * (seeded from the initial feed data on first toggle).
 */
const reactionState = new Map<string, { reacted: boolean; count: number }>()

/** Seed data keeps its own mutable counts so GET /class-feed reflects toggles. */
let feedItems: FeedItem[] | null = null

function getFeed(): FeedItem[] {
  if (!feedItems)
    feedItems = makeFeed()

  return feedItems
}

// FIND-privacy-018: Report & Block state for MSW dev mode
interface Report {
  reportId: string
  reporterStudentId: string
  contentType: string
  contentId: string
  category: string
  severity: string
  reason: string | null
  reportedAt: string
  status: string
}

const reports: Report[] = []
const blockedUsers = new Set<string>()

// Rate limiter: tracks report timestamps per-hour window
const reportTimestamps: number[] = []

function inferSeverity(category: string): string {
  switch (category) {
    case 'self-harm-risk': return 'critical'
    case 'bullying': return 'high'
    case 'inappropriate': return 'medium'
    case 'spam': return 'low'
    case 'other': return 'low'
    default: return 'medium'
  }
}

export const handlerStudentSocial = [
  http.get('/api/social/class-feed', () => {
    const items = getFeed()

    return HttpResponse.json({
      items,
      page: 1,
      pageSize: items.length,
      hasMore: false,
    })
  }),

  http.post('/api/social/reactions', async ({ request }) => {
    const body = await request.json() as { itemId: string; reactionType: string }
    const key = `${body.itemId}:${body.reactionType}`
    const items = getFeed()
    const feedItem = items.find(i => i.itemId === body.itemId)

    let state = reactionState.get(key)
    if (!state) {
      // Seed from the feed item's current count (first interaction)
      state = { reacted: false, count: feedItem?.reactionCount ?? 0 }
    }

    // Toggle: un-react if already reacted, react if not
    if (state.reacted) {
      state.count = Math.max(0, state.count - 1)
      state.reacted = false
    }
    else {
      state.count += 1
      state.reacted = true
    }

    reactionState.set(key, state)

    // Keep the feed item in sync so subsequent GET /class-feed reflects the change
    if (feedItem)
      feedItem.reactionCount = state.count

    return HttpResponse.json({
      ok: true,
      itemId: body.itemId,
      reactionType: body.reactionType,
      newCount: state.count,
      reacted: state.reacted,
    })
  }),

  http.get('/api/social/peers/solutions', () => {
    return HttpResponse.json({ solutions: SOLUTIONS })
  }),

  http.post('/api/social/peers/solutions/:id/vote', async ({ params, request }) => {
    const body = await request.json() as { direction: 'up' | 'down' }
    const sol = SOLUTIONS.find(s => s.solutionId === params.id)
    if (!sol)
      return HttpResponse.json({ error: 'Solution not found' }, { status: 404 })

    if (body.direction === 'up')
      sol.upvoteCount += 1
    else
      sol.downvoteCount += 1

    return HttpResponse.json({
      ok: true,
      solutionId: sol.solutionId,
      upvoteCount: sol.upvoteCount,
      downvoteCount: sol.downvoteCount,
    })
  }),

  http.get('/api/social/friends', () => {
    return HttpResponse.json({
      friends: FRIENDS,
      pendingRequests,
    })
  }),

  http.post('/api/social/friends/:id/accept', ({ params }) => {
    const idx = pendingRequests.findIndex(r => r.requestId === params.id)
    if (idx === -1)
      return HttpResponse.json({ error: 'Request not found' }, { status: 404 })

    const req = pendingRequests[idx]

    pendingRequests.splice(idx, 1)
    FRIENDS.push({
      studentId: req.fromStudentId,
      displayName: req.fromDisplayName,
      avatarUrl: null,
      level: 5,
      streakDays: 1,
      isOnline: false,
    })

    return HttpResponse.json({
      ok: true,
      friendshipId: `friend-${params.id}`,
    })
  }),

  // ─── FIND-privacy-018: Report & Block handlers ───────────────────

  http.post('/api/social/report', async ({ request }) => {
    const body = await request.json() as {
      contentType: string
      contentId: string
      category: string
      reason?: string
    }

    const validContentTypes = ['feed-item', 'comment', 'peer-solution', 'friend-request', 'study-room']
    const validCategories = ['bullying', 'inappropriate', 'spam', 'self-harm-risk', 'other']

    if (!validContentTypes.includes(body.contentType))
      return HttpResponse.json({ error: 'Invalid contentType' }, { status: 400 })
    if (!validCategories.includes(body.category))
      return HttpResponse.json({ error: 'Invalid category' }, { status: 400 })
    if (!body.contentId)
      return HttpResponse.json({ error: 'contentId is required' }, { status: 400 })

    // Rate limiting: max 10 per hour
    const oneHourAgo = Date.now() - 3600_000
    const recentCount = reportTimestamps.filter(ts => ts > oneHourAgo).length
    if (recentCount >= 10)
      return new HttpResponse(null, { status: 429 })

    reportTimestamps.push(Date.now())

    const severity = inferSeverity(body.category)
    const now = new Date().toISOString()
    const reportId = `report_${Date.now()}`

    const report: Report = {
      reportId,
      reporterStudentId: 'current-user',
      contentType: body.contentType,
      contentId: body.contentId,
      category: body.category,
      severity,
      reason: body.reason ?? null,
      reportedAt: now,
      status: 'pending',
    }

    reports.push(report)

    return HttpResponse.json({
      reportId,
      severity,
      reportedAt: now,
    })
  }),

  http.post('/api/social/block', async ({ request }) => {
    const body = await request.json() as { targetStudentId: string }

    if (!body.targetStudentId)
      return HttpResponse.json({ error: 'targetStudentId is required' }, { status: 400 })

    blockedUsers.add(body.targetStudentId)

    return HttpResponse.json({
      ok: true,
      targetStudentId: body.targetStudentId,
      blockedAt: new Date().toISOString(),
    })
  }),

  http.delete('/api/social/block/:targetStudentId', ({ params }) => {
    const targetStudentId = params.targetStudentId as string

    blockedUsers.delete(targetStudentId)

    return HttpResponse.json({
      ok: true,
      targetStudentId,
    })
  }),
]
