import { HttpResponse, http } from 'msw'

/**
 * MSW handlers for the student `/api/notifications/*` endpoint group.
 *
 * These mock responses let the student web dev loop work without the
 * student API host. In production the MSW worker is NOT registered,
 * so real requests pass through to the deployed notifications center.
 */

interface NotificationItem {
  notificationId: string
  kind: 'xp' | 'badge' | 'friend-request' | 'review-due' | 'system'
  priority: 'low' | 'normal' | 'high'
  title: string
  body: string
  iconName: string | null
  deepLinkUrl: string | null
  read: boolean
  createdAt: string
}

function seed(): NotificationItem[] {
  const now = Date.now()

  return [
    { notificationId: 'n-1', kind: 'badge', priority: 'normal', title: 'Quiz Master badge earned!', body: 'You hit 90% accuracy on 10 quizzes in a row.', iconName: 'tabler-brain', deepLinkUrl: '/progress', read: false, createdAt: new Date(now - 20 * 60_000).toISOString() },
    { notificationId: 'n-2', kind: 'xp', priority: 'low', title: '+25 XP', body: 'Great work on your last session.', iconName: 'tabler-bolt', deepLinkUrl: '/progress', read: false, createdAt: new Date(now - 2 * 3600_000).toISOString() },
    { notificationId: 'n-3', kind: 'friend-request', priority: 'normal', title: 'Casey Kim sent a friend request', body: 'Tap to review.', iconName: 'tabler-user-plus', deepLinkUrl: '/social/friends', read: false, createdAt: new Date(now - 3 * 3600_000).toISOString() },
    { notificationId: 'n-4', kind: 'review-due', priority: 'normal', title: '3 items due for review', body: 'A quick 10-minute review locks in what you practiced yesterday.', iconName: 'tabler-refresh', deepLinkUrl: '/session', read: true, createdAt: new Date(now - 8 * 3600_000).toISOString() },
    { notificationId: 'n-5', kind: 'badge', priority: 'normal', title: 'Consistency badge earned', body: 'You completed a session on 12 of the last 14 days.', iconName: 'tabler-calendar-check', deepLinkUrl: '/home', read: true, createdAt: new Date(now - 1 * 86400_000).toISOString() },
    { notificationId: 'n-6', kind: 'xp', priority: 'low', title: '+10 XP', body: 'Daily challenge complete.', iconName: 'tabler-bolt', deepLinkUrl: '/challenges', read: true, createdAt: new Date(now - 2 * 86400_000).toISOString() },
    { notificationId: 'n-7', kind: 'system', priority: 'low', title: 'Welcome to Cena', body: 'Explore your dashboard and start learning.', iconName: 'tabler-sparkles', deepLinkUrl: '/home', read: true, createdAt: new Date(now - 7 * 86400_000).toISOString() },
  ]
}

const notifications = seed()

function unreadCount(): number {
  return notifications.filter(n => !n.read).length
}

export const handlerStudentNotifications = [
  http.get('/api/notifications', () => {
    return HttpResponse.json({
      items: notifications,
      page: 1,
      pageSize: notifications.length,
      total: notifications.length,
      hasMore: false,
      unreadCount: unreadCount(),
    })
  }),

  http.get('/api/notifications/unread-count', () => {
    return HttpResponse.json({ count: unreadCount() })
  }),

  http.post('/api/notifications/:id/read', ({ params }) => {
    const n = notifications.find(x => x.notificationId === params.id)
    if (!n)
      return HttpResponse.json({ error: 'Notification not found' }, { status: 404 })

    n.read = true

    return HttpResponse.json({ ok: true, id: params.id })
  }),

  http.post('/api/notifications/mark-all-read', () => {
    let markedCount = 0
    for (const n of notifications) {
      if (!n.read) {
        n.read = true
        markedCount += 1
      }
    }

    return HttpResponse.json({ ok: true, markedCount })
  }),
]
