

import { ArrowRight, Bell, Mail } from '@lucide/vue'
import { listNotificationsEndpoint, readNotificationThreadEndpoint } from '../../api'
import type { NoCtfapiEndpointsNotificationsNotificationResponse } from '../../api'
import { createLatestRequestGuard } from '../../lib/latest-request'

type Notification = NoCtfapiEndpointsNotificationsNotificationResponse

/** Owns state, effects and commands for NotificationCenter. */
export function useNotificationCenter() {
  const route = useRoute()

  const router = useRouter()

  const { markAllRead } = useNotificationUnread()

  const selected = ref<Notification | null>(null)

  const thread = ref<Notification[]>([])

  const threadLoading = ref(false)

  const threadError = ref<string | null>(null)

  const routeError = ref<string | null>(null)

  const threadRequests = createLatestRequestGuard()

  const { items, loading, error, hasMore, initialized, loadMore } =
    useCursorPagination<Notification>(async (cursor) => {
      const { data, error: requestError } = await listNotificationsEndpoint({
        query: {
          scope: 'Inbox',
          cursor,
          limit: 50,
        },
      })
      if (requestError || !data) throw requestError ?? new Error(translate("ui.failedToLoadNotification"))
      return { items: data.items, nextCursor: data.nextCursor }
    })

  function sourceLabel(notification: Notification): string {
    if (notification.sourceDisplayName) return notification.sourceDisplayName
    return notification.sourceType === 0 ? translate("ui.eventSystem") : translate("ui.eventStaff")
  }

  function audienceLabel(notification: Notification): string {
    return ({
      0: translate("ui.onlyYou"),
      1: translate("ui.eventWorkingGroup"),
      2: translate("ui.eventAnnouncement"),
      3: translate("ui.ourTeam"),
      4: translate("ui.platformAdministrator"),
    } as Record<number, string>)[notification.targetType ?? -1] ?? translate("ui.targetedMessages")
  }

  function categoryLabel(notification: Notification): string {
    if (notification.kind === 'QuestionOpened' || notification.kind === 'QuestionStatusChanged' || notification.kind === 'Message')
      return translate("ui.questions")
    if (notification.kind === 'TeamBanned' || notification.kind === 'TeamBanCorrected' || notification.kind === 'TeamBanAppealSubmitted' || notification.kind === 'TeamRegistrationChanged')
      return translate("ui.team")
    if (notification.kind === 'GameplayFactAdjudicated') return translate("ui.review")
    if (notification.kind === 'RuntimeStateChanged') return translate("ui.environment")
    if (notification.kind === 'CheatIncidentDetected' || notification.kind === 'ManagementFailure' || notification.kind === 'StartGateFailed')
      return translate("ui.management")
    if (notification.kind === 'UserAccountLifecycleChanged') return translate("ui.accountNumber")
    if (notification.kind === 'CompetitionForceDeleted') return translate("ui.management")
    return translate("ui.news")
  }

  function threadText(notification: Notification): string {
    const body = notificationBody(notification)
    if (body) return body
    const payload = notification.content && typeof notification.content === 'object'
      ? notification.content as Record<string, unknown>
      : {}
    if (notification.kind === 'QuestionStatusChanged') {
      const from = typeof payload.from === 'string' ? payload.from : translate("ui.unknown")
      const to = typeof payload.to === 'string' ? payload.to : translate("ui.unknown")
      return translate("ui.questionStatus", { from, to })
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
      const { data, error: requestError } = await readNotificationThreadEndpoint({
        path: { notificationId: rootId },
      })
      if (!threadRequests.isCurrent(request)) return
      if (requestError || !data) throw requestError ?? new Error(translate("ui.failedToLoadNotificationDetails"))
      thread.value = data.items ?? []
    }
    catch (requestError) {
      if (threadRequests.isCurrent(request))
        threadError.value = parseApiError(requestError, translate("ui.failedToLoadNotificationDetails")).message
    }
    finally {
      if (threadRequests.isCurrent(request))
        threadLoading.value = false
    }
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
      const { data, error: requestError } = await readNotificationThreadEndpoint({
        path: { notificationId: selectedId },
      })
      if (!threadRequests.isCurrent(request)) return
      if (requestError || !data) throw requestError ?? new Error(translate("ui.failedToLoadNotificationDetails"))
      const routedNotification = data.items?.find(item => item.id === selectedId)
      if (!routedNotification) throw new Error(translate("ui.theNotificationDoesNotExistOrYouAreNotAllowed"))
      selected.value = routedNotification
      thread.value = data.items ?? []
    }
    catch (requestError) {
      if (threadRequests.isCurrent(request))
        routeError.value = parseApiError(requestError, translate("ui.failedToLoadNotificationDetails")).message
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
      closeDetail,
      actionPath,
      showAction
    }
}

export type NotificationCenterViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useNotificationCenter>>>
