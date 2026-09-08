import { markRaw } from 'vue'

import NotificationCenterComponent from '../notifications/NotificationCenter.vue'

/** Owns state, effects and commands for NotificationsPage. */
export function useNotificationsPage() {
  const NotificationCenter = markRaw(NotificationCenterComponent)

  return {
      NotificationCenter
    }
}

export type NotificationsPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useNotificationsPage>>>
