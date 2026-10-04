import { h, markRaw, shallowReactive } from 'vue'
import { toast } from '../../utils/message-toast'

import type { NoCTFAPIEndpointsNotificationsNotificationResponse } from '../../api/models'
import NotificationNoticeContentComponent from '../../components/views/layout/NotificationNoticeContent.vue'
import NoticeToastComponent from '../../components/ui/sonner/NoticeToast.vue'
import type { NoticePayload } from '../../components/ui/sonner/notice-state'

interface NotificationNoticeOptions {
  idPrefix?: string | null
}

const displayedNoticeIds = new Set<string>()
const maximumDisplayedNoticeIds = 256

function rememberDisplayedNotice(id: string): boolean {
  if (displayedNoticeIds.has(id)) return false
  displayedNoticeIds.add(id)
  if (displayedNoticeIds.size > maximumDisplayedNoticeIds) {
    const oldest = displayedNoticeIds.values().next().value
    if (oldest) displayedNoticeIds.delete(oldest)
  }
  return true
}

export function showNotificationNotice(
  notification: NoCTFAPIEndpointsNotificationsNotificationResponse,
  options: NotificationNoticeOptions = {},
): boolean {
  if (!notification.id) return false

  const id = `${options.idPrefix ?? 'notification'}-${notification.id}`
  if (!rememberDisplayedNotice(id)) return false

  const payload = shallowReactive<NoticePayload>({
    id,
    destructive: false,
    content: () => [h(NotificationNoticeContentComponent, {
      title: notificationTitle(notification),
      body: notificationBody(notification),
      href: notificationTargetPath(notification),
      actionLabel: notificationActionLabel(notification),
    })],
  })
  const release = () => { payload.content = undefined }
  toast.custom(markRaw(NoticeToastComponent), {
    id,
    duration: 8000,
    position: 'top-right',
    class: 'noctf-notice-info',
    componentProps: { payload },
    onDismiss: release,
    onAutoClose: release,
  })
  return true
}
