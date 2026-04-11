import { HttpResponse, http } from 'msw'

/**
 * MSW handlers for the student `/api/notifications/*` endpoint group
 * from STB-07 Phase 1 (reads) + STB-07b Phase 1b (writes).
 *
 * STU-W-14 wires these into the /notifications center.
 */

interface NotificationItem {
  notificationId: string
  kind: 'xp' | 'badge' | 'streak' | 'friend-request' | 'review-due' | 'system'
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
    { notificationId: 'n-4', kind: 'review-due', priority: 'normal', title: '3 items due for review', body: 'Keep your streak going with a quick review session.', iconName: 'tabler-refresh', deepLinkUrl: '/session', read: true, createdAt: new Date(now - 8 * 3600_000).toISOString() },
    { notificationId: 'n-5', kind: 'streak', priority: 'high', title: '12-day streak — nice!', body: 'You\'re on a roll.', iconName: 'tabler-flame', deepLinkUrl: '/home', read: true, createdAt: new Date(now - 1 * 86400_000).toISOString() },
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
