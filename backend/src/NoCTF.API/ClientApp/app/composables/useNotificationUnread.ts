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

export function newNotificationNotices<T extends { id?: string | null }>(
  newestFirst: readonly T[],
  previousId: string | null,
  maximum = 3,
): T[] {
  const previousIndex = newestFirst.findIndex(item => item.id === previousId)
  return (previousIndex < 0 ? newestFirst : newestFirst.slice(0, previousIndex))
    .slice(0, maximum)
    .toReversed()
}

export function useNotificationUnread() {
  const { user } = useAuth()
  const hasUnread = useState<boolean>('notifications:has-unread', () => false)

  function storageKey(): string | null {
    return user.value?.userId ? `noctf:notifications:last-read:${user.value.userId}` : null
  }

  async function refreshUnread(): Promise<NoCtfapiEndpointsNotificationsNotificationResponse[] | undefined> {
    const key = storageKey()
    if (!key) {
      latestNotificationId.value = null
      hasUnread.value = false
      return []
    }

    const { data, error } = await listNotificationsEndpoint({
      query: { scope: 'Inbox', offset: 0, limit: 20, desc: true },
    })
    if (error) return undefined

    const items = data?.items ?? []
    const latest = items[0] ?? null
    latestNotificationId.value = latest?.id ?? null
    const lastReadId = safeLocalStorage.getItem(key)
    hasUnread.value = isNotificationUnread(latestNotificationId.value, lastReadId)
    return items
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
