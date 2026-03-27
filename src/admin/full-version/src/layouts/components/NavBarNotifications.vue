<script lang="ts" setup>
import type { Notification } from '@layouts/types'

const notifications = ref<Notification[]>([])
const readIds = ref<Set<number>>(new Set())

const eventMeta: Record<string, { icon: string; color: string; label: string }> = {
  ConceptMastered: { icon: 'tabler-trophy', color: 'success', label: 'Concept Mastered' },
  ConceptAttempted: { icon: 'tabler-pencil', color: 'info', label: 'Concept Attempted' },
  StagnationDetected: { icon: 'tabler-alert-triangle', color: 'warning', label: 'Stagnation Detected' },
  MethodologySwitched: { icon: 'tabler-switch-horizontal', color: 'primary', label: 'Methodology Switched' },
  SessionStarted: { icon: 'tabler-player-play', color: 'info', label: 'Session Started' },
  SessionEnded: { icon: 'tabler-player-stop', color: 'secondary', label: 'Session Ended' },
  FocusAlert: { icon: 'tabler-eye-off', color: 'error', label: 'Focus Alert' },
  MicrobreakTaken: { icon: 'tabler-coffee', color: 'success', label: 'Microbreak Taken' },
}

const defaultMeta = { icon: 'tabler-bell', color: 'primary', label: 'Event' }

function formatTime(timestamp: string): string {
  const date = new Date(timestamp)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffMin = Math.floor(diffMs / 60000)

  if (diffMin < 1) return 'Just now'
  if (diffMin < 60) return `${diffMin}m ago`

  const diffHours = Math.floor(diffMin / 60)
  if (diffHours < 24) return `${diffHours}h ago`

  return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
}

const fetchNotifications = async () => {
  try {
    const response = await $api<any>('/admin/events/recent')
    const rawEvents = response?.events ?? response ?? []
    const events: any[] = Array.isArray(rawEvents) ? rawEvents : []

    notifications.value = events.slice(0, 8).map((e: any, i: number) => {
      const type = e.type ?? e.eventType ?? e.EventType ?? 'Event'
      const meta = eventMeta[type] ?? defaultMeta
      const summary = e.summary ?? `${meta.label} on ${e.aggregateType ?? e.AggregateType ?? 'unknown'}`
      const time = formatTime(e.timestamp ?? e.Timestamp ?? new Date().toISOString())
      const id = i + 1

      return {
        id,
        icon: meta.icon,
        title: meta.label,
        subtitle: summary,
        time,
        isSeen: readIds.value.has(id),
        color: meta.color,
      }
    })
  }
  catch {
    // API unavailable — show empty state
  }
}

let pollInterval: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  fetchNotifications()
  pollInterval = setInterval(fetchNotifications, 15000)
})

onUnmounted(() => {
  if (pollInterval) clearInterval(pollInterval)
})

const removeNotification = (notificationId: number) => {
  notifications.value = notifications.value.filter(item => item.id !== notificationId)
}

const markRead = (notificationId: number[]) => {
  notificationId.forEach(id => readIds.value.add(id))
  notifications.value.forEach(item => {
    if (notificationId.includes(item.id))
      item.isSeen = true
  })
}

const markUnRead = (notificationId: number[]) => {
  notificationId.forEach(id => readIds.value.delete(id))
  notifications.value.forEach(item => {
    if (notificationId.includes(item.id))
      item.isSeen = false
  })
}

const handleNotificationClick = (notification: Notification) => {
  if (!notification.isSeen)
    markRead([notification.id])
}
</script>

<template>
  <Notifications
    :notifications="notifications"
    @remove="removeNotification"
    @read="markRead"
    @unread="markUnRead"
    @click:notification="handleNotificationClick"
  />
</template>
