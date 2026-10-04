
import { api } from '../../lib/api'
import { message as describeMessage } from '../../utils/i18n'
import type { UiMessage } from '../../utils/i18n'


import { ArrowRight, Bell, Mail } from '@lucide/vue'

import type { NoCTFAPIEndpointsNotificationsNotificationResponse } from '../../api/models'
import { createLatestRequestGuard } from '../../lib/latest-request'
import { createLatestPageRefresh } from '../../lib/latest-page-refresh'
import { watchNotifications } from '../../composables/useNotificationHub'

type Notification = NoCTFAPIEndpointsNotificationsNotificationResponse

/** Owns state, effects and commands for NotificationCenter. */
export function useNotificationCenter() {
  const route = useRoute()

  const router = useRouter()

  const { markAllRead } = useNotificationUnread()

  const selected = ref<Notification | null>(null)

  const thread = ref<Notification[]>([])

  const threadLoading = ref(false)

  const threadError = ref<UiMessage | null>(null)

  const routeError = ref<UiMessage | null>(null)

  const threadRequests = createLatestRequestGuard()

  const { items, loading, error, hasMore, initialized, loadMore, reset } =
    useCursorPagination<Notification>(async (cursor) => {
      let requestError: unknown;
      const data = await api.api.v1.notifications.get({ queryParameters: {
          scope: 'Inbox',
          offset: cursor ? Number(cursor) || 0 : 0,
          limit: 50,
          desc: true,
        } }).catch(cause => { requestError = cause; return undefined });
      if (requestError || !data) throw requestError ?? new Error(translate("notifications.error.loadNotificationFailed"))
      const pageItems = data.items ?? []
      const offset = cursor ? Number(cursor) || 0 : 0
      const nextOffset = offset + pageItems.length
      return { items: pageItems, nextCursor: nextOffset < (data.total ?? 0) ? String(nextOffset) : null }
    })

  const { refreshLatest } = createLatestPageRefresh({
    loadMore,
    reset: () => reset({ preserveItems: true }),
  })

  const notificationOptions = computed(() => items.value.flatMap(notification => notification.id
    ? [{ value: notification.id, label: notificationTitle(notification), notification }]
    : []))

  const selectedId = computed(() => selected.value?.id ?? null)

  function sourceLabel(notification: Notification): string {
    if (notification.sourceDisplayName) return notification.sourceDisplayName
    return notification.sourceType === 0 ? translate("notifications.label.eventSystem") : translate("common.label.eventStaff")
  }

  function audienceLabel(notification: Notification): string {
    return ({
      0: translate("notifications.label.notificationCenter"),
      1: translate("notifications.label.eventWorkingGroup"),
      2: translate("common.label.eventAnnouncement"),
      3: translate("notifications.label.ourTeam"),
      4: translate("notifications.label.platformAdministrator"),
    } as Record<number, string>)[notification.targetType ?? -1] ?? translate("notifications.label.targetedMessages")
  }

  function categoryLabel(notification: Notification): string {
    if (notification.kind === 'QuestionOpened' || notification.kind === 'QuestionStatusChanged' || notification.kind === 'Message')
      return translate("common.label.questions")
    if (notification.kind === 'TeamBanned' || notification.kind === 'TeamBanCorrected' || notification.kind === 'TeamBanAppealSubmitted' || notification.kind === 'TeamRegistrationChanged')
      return translate("common.label.team")
    if (notification.kind === 'GameplayFactAdjudicated') return translate("notifications.label.review")
    if (notification.kind === 'RuntimeStateChanged') return translate("notifications.label.environment")
    if (notification.kind === 'CheatIncidentDetected' || notification.kind === 'ManagementFailure' || notification.kind === 'StartGateFailed')
      return translate("common.label.management")
    if (notification.kind === 'UserAccountLifecycleChanged') return translate("notifications.label.accountNumber")
    if (notification.kind === 'CompetitionForceDeleted') return translate("common.label.management")
    return translate("common.label.news")
  }

  function threadText(notification: Notification): string {
    const body = notificationBody(notification)
    if (body) return body
    const payload = notification.content && typeof notification.content === 'object'
      ? notification.content as Record<string, unknown>
      : {}
    if (notification.kind === 'QuestionStatusChanged') {
      const from = typeof payload.from === 'string' ? payload.from : translate("common.label.unknown")
      const to = typeof payload.to === 'string' ? payload.to : translate("common.label.unknown")
      return translate("notifications.label.questionStatus", { from, to })
    }
    return notificationText(notification)
  }

  async function openNotification(notification: Notification, updateRoute = true): Promise<void> {
    const request = threadRequests.begin()
    selected.value = notification
    thread.value = []
    threadError.value = null
    routeError.value = null
    threadLoading.value = false
    if (updateRoute) {
      await router.replace({
        query: { ...route.query, notification: notification.id },
      })
      if (!threadRequests.isCurrent(request)) return
    }

    const rootId = notificationThreadRootId(notification)
    if (!rootId) return
    threadLoading.value = true
    try {
      let requestError: unknown;
      const data = await api.api.v1.notifications.byNotificationId(rootId).thread.get().catch(cause => { requestError = cause; return undefined });
      if (!threadRequests.isCurrent(request)) return
      if (requestError || !data) throw requestError ?? new Error(translate("notifications.notificationCenter.error.loadNotificationDetailsFailed"))
      thread.value = data.items ?? []
    }
    catch (requestError) {
      if (threadRequests.isCurrent(request))
        threadError.value = parseApiError(requestError, describeMessage("notifications.notificationCenter.error.loadNotificationDetailsFailed")).displayMessage
    }
    finally {
      if (threadRequests.isCurrent(request))
        threadLoading.value = false
    }
  }

  async function selectNotification(id: string): Promise<void> {
    const notification = items.value.find(item => item.id === id)
    if (notification) await openNotification(notification)
  }

  async function closeDetail(): Promise<void> {
    threadRequests.invalidate()
    selected.value = null
    thread.value = []
    threadError.value = null
    routeError.value = null
    threadLoading.value = false
    const query = { ...route.query }
    delete query.notification
    await router.replace({ query })
  }

  async function openFromRoute(): Promise<void> {
    const selectedId = typeof route.query.notification === 'string'
      ? route.query.notification
      : null
    if (!selectedId || selected.value?.id === selectedId) return
    const notification = items.value.find(item => item.id === selectedId)
    if (notification) {
      await openNotification(notification, false)
      return
    }

    const request = threadRequests.begin()
    selected.value = null
    thread.value = []
    threadError.value = null
    routeError.value = null
    threadLoading.value = true
    try {
      let requestError: unknown;
      const data = await api.api.v1.notifications.byNotificationId(selectedId).thread.get().catch(cause => { requestError = cause; return undefined });
      if (!threadRequests.isCurrent(request)) return
      if (requestError || !data) throw requestError ?? new Error(translate("notifications.notificationCenter.error.loadNotificationDetailsFailed"))
      const routedNotification = data.items?.find(item => item.id === selectedId)
      if (!routedNotification) throw new Error(translate("notifications.notificationCenter.description.notificationExistAllowed"))
      selected.value = routedNotification
      thread.value = data.items ?? []
    }
    catch (requestError) {
      if (threadRequests.isCurrent(request))
        routeError.value = parseApiError(requestError, describeMessage("notifications.notificationCenter.error.loadNotificationDetailsFailed")).displayMessage
    }
    finally {
      if (threadRequests.isCurrent(request))
        threadLoading.value = false
    }
  }

  onMounted(async () => {
    await loadMore()
    markAllRead(items.value[0]?.id)
    await openFromRoute()
  })

  let unwatchNotifications: (() => void) | undefined
  onMounted(() => {
    unwatchNotifications = watchNotifications({
      notificationChanged: () => void refreshInbox(),
      onReconnected: () => void refreshInbox(),
    })
  })
  onUnmounted(() => unwatchNotifications?.())

  async function refreshInbox(): Promise<void> {
    await refreshLatest()
    if (!error.value) markAllRead(items.value[0]?.id)
    if (selected.value?.id && notificationThreadRootId(selected.value))
      await refreshSelectedThread()
  }

  async function refreshSelectedThread(): Promise<void> {
    const current = selected.value
    const rootId = current && notificationThreadRootId(current)
    if (!rootId) return
    const request = threadRequests.begin()
    try {
      let requestError: unknown;
      const data = await api.api.v1.notifications.byNotificationId(rootId).thread.get().catch(cause => { requestError = cause; return undefined });
      if (!threadRequests.isCurrent(request) || selected.value?.id !== current.id) return
      if (requestError || !data) throw requestError
      thread.value = data.items ?? []
      threadError.value = null
      const updated = data.items?.find(item => item.id === current.id)
      if (updated) selected.value = updated
    }
    catch (failure) {
      if (!threadRequests.isCurrent(request) || selected.value?.id !== current.id) return
      const parsed = parseApiError(failure, describeMessage('notifications.notificationCenter.error.loadNotificationDetailsFailed'))
      if (parsed.status === 403 || parsed.status === 404) {
        selected.value = null
        thread.value = []
        routeError.value = parsed.displayMessage
      }
      else threadError.value = parsed.displayMessage
    }
  }

  watch(() => route.query.notification, () => void openFromRoute())

  const actionTarget = computed(() =>
    thread.value.find(item => item.kind === 'QuestionOpened') ?? selected.value,
  )

  const actionPath = computed(() => actionTarget.value ? notificationTargetPath(actionTarget.value) : null)

  const showAction = computed(() => selected.value?.kind !== 'CompetitionAnnouncement' && actionPath.value !== null)

  return {
      ArrowRight,
      Bell,
      Mail,
      selected,
      thread,
      threadLoading,
      threadError,
      routeError,
      items,
      notificationOptions,
      selectedId,
      loading,
      error,
      hasMore,
      initialized,
      loadMore,
      sourceLabel,
      audienceLabel,
      categoryLabel,
      threadText,
      openNotification,
      selectNotification,
      closeDetail,
      actionPath,
      showAction
    }
}

export type NotificationCenterViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useNotificationCenter>>>
