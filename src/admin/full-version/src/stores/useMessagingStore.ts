import { $api } from '@/utils/api'

export interface MessagingThread {
  threadId: string
  threadType: string
  participantIds: string[]
  participantNames: string[]
  classRoomId: string | null
  lastMessagePreview: string
  lastMessageAt: string
  messageCount: number
  createdAt: string
}

export interface MessagingMessage {
  messageId: string
  senderId: string
  senderName: string
  senderRole: string
  text: string
  contentType: string
  resourceUrl: string | null
  channel: string
  replyToMessageId: string | null
  wasBlocked: boolean | null
  blockReason: string | null
  sentAt: string
}

export interface MessagingContact {
  userId: string
  displayName: string
  role: string
  email: string | null
  avatarUrl: string | null
}

interface ThreadListResponse {
  items: MessagingThread[]
  totalCount: number
  page: number
  pageSize: number
}

interface ThreadDetailResponse {
  threadId: string
  threadType: string
  participantIds: string[]
  participantNames: string[]
  messages: MessagingMessage[]
  totalMessages: number
  nextCursor: string | null
}

interface ContactListResponse {
  items: MessagingContact[]
}

export const useMessagingStore = defineStore('messaging', {
  state: () => ({
    threads: [] as MessagingThread[],
    totalThreads: 0,
    contacts: [] as MessagingContact[],
    activeThread: null as ThreadDetailResponse | null,
    loading: false,
    error: null as string | null,
  }),

  getters: {
    sortedThreads: state =>
      [...state.threads].sort((a, b) =>
        new Date(b.lastMessageAt).getTime() - new Date(a.lastMessageAt).getTime()),
  },

  actions: {
    async fetchThreads(params?: { type?: string; participantId?: string; search?: string; page?: number }) {
      this.loading = true
      this.error = null
      try {
        const query = new URLSearchParams()
        if (params?.type) query.set('type', params.type)
        if (params?.participantId) query.set('participantId', params.participantId)
        if (params?.search) query.set('search', params.search)
        if (params?.page) query.set('page', String(params.page))

        const qs = query.toString()
        const url = qs ? `/admin/messaging/threads?${qs}` : '/admin/messaging/threads'

        const result = await $api<ThreadListResponse>(url)
        this.threads = result.items
        this.totalThreads = result.totalCount
      }
      catch (err: any) {
        this.error = err?.message || 'Failed to load threads'
      }
      finally {
        this.loading = false
      }
    },

    async fetchThreadDetail(threadId: string) {
      this.loading = true
      this.error = null
      try {
        const result = await $api<ThreadDetailResponse>(`/admin/messaging/threads/${threadId}`)
        this.activeThread = result
      }
      catch (err: any) {
        this.error = err?.message || 'Failed to load thread'
      }
      finally {
        this.loading = false
      }
    },

    async sendMessage(threadId: string, text: string, replyToMessageId?: string) {
      try {
        await $api(`/admin/messaging/threads/${threadId}/messages`, {
          method: 'POST',
          body: { text, replyToMessageId },
        })

        // Refresh thread detail to show new message
        await this.fetchThreadDetail(threadId)
      }
      catch (err: any) {
        this.error = err?.message || 'Failed to send message'
      }
    },

    async createThread(threadType: string, participantIds: string[], initialMessage?: string, classRoomId?: string) {
      try {
        const result = await $api<{ threadId: string }>('/admin/messaging/threads', {
          method: 'POST',
          body: { threadType, participantIds, classRoomId, initialMessage },
        })

        await this.fetchThreads()

        return result.threadId
      }
      catch (err: any) {
        this.error = err?.message || 'Failed to create thread'

        return null
      }
    },

    async muteThread(threadId: string) {
      try {
        await $api(`/admin/messaging/threads/${threadId}/mute`, { method: 'PUT' })
      }
      catch (err: any) {
        this.error = err?.message || 'Failed to mute thread'
      }
    },

    async fetchContacts(search?: string) {
      try {
        const url = search
          ? `/admin/messaging/contacts?search=${encodeURIComponent(search)}`
          : '/admin/messaging/contacts'

        const result = await $api<ContactListResponse>(url)
        this.contacts = result.items
      }
      catch (err: any) {
        this.error = err?.message || 'Failed to load contacts'
      }
    },
  },
})
