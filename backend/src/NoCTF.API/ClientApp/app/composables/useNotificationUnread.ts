import { listNotificationsEndpoint } from '../api'
import type { NoCtfapiEndpointsNotificationsNotificationResponse } from '../api'
import { safeLocalStorage } from '../lib/safe-storage'
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

  async function refreshUnread(): Promise<NoCtfapiEndpointsNotificationsNotificationResponse | null | undefined> {
    const key = storageKey()
    if (!key) {
      latestNotificationId.value = null
      hasUnread.value = false
      return null
    }

    const { data, error } = await listNotificationsEndpoint({
      query: { scope: 'Inbox', offset: 0, limit: 1, desc: true },
    })
    if (error) return undefined

    const latest = data?.items?.[0] ?? null
    latestNotificationId.value = latest?.id ?? null
    const lastReadId = safeLocalStorage.getItem(key)
    hasUnread.value = isNotificationUnread(latestNotificationId.value, lastReadId)
    return latest
  }

  function markAllRead(notificationId?: string | null): void {
    const key = storageKey()
    const id = notificationId ?? latestNotificationId.value
    if (key && id)
      safeLocalStorage.setItem(key, id)
    latestNotificationId.value = id ?? null
    hasUnread.value = false
  }

  return { hasUnread, markAllRead, refreshUnread }
}
