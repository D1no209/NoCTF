import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { notificationApi, type UserNotification } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { useAuthStore } from '@/stores/auth'

export type { UserNotification }

// Canonical behavior follows V1's NotificationCenter: poll 20 notifications
// every 30s while authenticated, and selecting one marks it read (best
// effort) before navigating to its competition.
export function useNotificationCenter() {
  const router = useRouter()
  const auth = useAuthStore()
  const queryClient = useQueryClient()

  const notificationsQuery = useQuery({
    queryKey: queryKeys.notifications,
    queryFn: () => notificationApi.list(20),
    enabled: computed(() => auth.isAuthenticated),
    refetchInterval: 30_000,
    staleTime: 10_000,
  })

  const markReadMutation = useMutation({
    mutationFn: (id: string) => notificationApi.markRead(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.notifications }),
  })

  const markAllReadMutation = useMutation({
    mutationFn: () => notificationApi.markAllRead(),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.notifications }),
  })

  const unreadCount = computed(() => notificationsQuery.data.value?.unreadCount ?? 0)

  async function selectNotification(notification: UserNotification) {
    if (!notification.isRead) {
      try {
        await markReadMutation.mutateAsync(notification.id)
      }
      catch {
        // Reading a notification must not block its competition navigation.
      }
    }

    if (notification.competitionId)
      await router.push(`/competitions/${notification.competitionId}`)
  }

  return {
    notificationsQuery,
    markAllRead: markAllReadMutation,
    unreadCount,
    selectNotification,
  }
}
