import { listNotificationsEndpoint } from '../api'
import { ref } from 'vue'

const latestNotificationId = ref<string | null>(null)

export function isNotificationUnread(
  latestId: string | null,
  lastReadId: string | null,
): boolean {
  return latestId !== null && latestId !== lastReadId
}

export function useNotificationUnread() {
  const { user } = useAuth()
  const hasUnread = useState<boolean>('notifications:has-unread', () => false)

  function storageKey(): string | null {
    return user.value?.userId ? `noctf:notifications:last-read:${user.value.userId}` : null
  }

  async function refreshUnread(): Promise<void> {
    const key = storageKey()
    if (!key) {
      latestNotificationId.value = null
      hasUnread.value = false
      return
    }

    const { data, error } = await listNotificationsEndpoint({
      query: { scope: 'Inbox', limit: 1 },
    })
    if (error) return

    latestNotificationId.value = data?.items?.[0]?.id ?? null
    const lastReadId = import.meta.client ? localStorage.getItem(key) : null
    hasUnread.value = isNotificationUnread(latestNotificationId.value, lastReadId)
  }

  function markAllRead(notificationId?: string | null): void {
    const key = storageKey()
    const id = notificationId ?? latestNotificationId.value
    if (key && id && import.meta.client)
      localStorage.setItem(key, id)
    latestNotificationId.value = id ?? null
    hasUnread.value = false
  }

  return { hasUnread, markAllRead, refreshUnread }
}
