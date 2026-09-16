import { h, markRaw, shallowReactive } from 'vue'
import { toast } from 'vue-sonner'

import { Bell, Database, Flag, ShieldAlert } from '@lucide/vue'
import AccountPanelComponent from '../account/AccountPanel.vue'
import LanguageToggleComponent from '../LanguageToggle.vue'
import ThemeToggleComponent from '../ThemeToggle.vue'
import ThemePalettePanelComponent from '../theme/ThemePalettePanel.vue'
import NoticeToastComponent from '../../components/ui/sonner/NoticeToast.vue'
import type { NoticePayload } from '../../components/ui/sonner/notice-state'
import NotificationNoticeContentComponent from '../../components/views/layout/NotificationNoticeContent.vue'

/** Owns state, effects and commands for DefaultLayout. */
export function useDefaultLayout() {
  const {
    user,
    isLoggedIn,
    isAdministrator,
    canOrganize,
    impersonation,
    impersonationEnding,
    endImpersonation,
  } = useAuth()

  const { configuration, error: platformError, loading: platformLoading, ensureLoaded } = usePlatform()

  const route = useRoute()
  const isHome = computed(() => route.path === '/')
  const {
    wallpaperActive,
    wallpaperStyle,
    refreshWallpaper,
    clearWallpaper,
  } = usePersonalWallpaper()

  const { hasUnread, refreshUnread } = useNotificationUnread()

  const { locale, t } = useLocale()

  const impersonationExpiresAt = computed(() => {
    if (!impersonation.value) return ''
    return new Intl.DateTimeFormat(locale.value, {
      dateStyle: 'medium',
      timeStyle: 'medium',
    }).format(new Date(impersonation.value.expiresAt))
  })

  let notificationTimer: ReturnType<typeof setInterval> | undefined
  let notificationBaselineReady = false
  let lastNotificationId: string | null = null

  function showNotificationNotice(notification: NonNullable<Awaited<ReturnType<typeof refreshUnread>>>): void {
    if (!notification.id) return
    const id = `notification-${notification.id}`
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
  }

  async function refreshNotifications(showNotice: boolean): Promise<void> {
    const latest = await refreshUnread()
    if (latest === undefined) return
    const latestId = latest?.id ?? null
    if (showNotice && notificationBaselineReady && latest && latestId && latestId !== lastNotificationId)
      showNotificationNotice(latest)
    lastNotificationId = latestId
    notificationBaselineReady = true
  }

  const navItems = computed(() => [
    { to: '/competitions', label: t("ui.competitions"), icon: Flag, show: true },
    { to: '/admin/challenges', label: t("ui.challengeLibrary2"), icon: Database, show: canOrganize.value },
  ])

  function isActive(to: string) {
    return route.path === to || route.path.startsWith(`${to}/`)
  }

  onMounted(() => {
    void refreshNotifications(false)
    notificationTimer = setInterval(() => void refreshNotifications(true), 20_000)
  })

  watch(
    () => user.value?.userId,
    () => {
      notificationBaselineReady = false
      lastNotificationId = null
      void refreshNotifications(false)
    },
  )

  watch(
    () => [user.value?.userId, user.value?.wallpaperRevision],
    () => void refreshWallpaper(),
    { immediate: true },
  )

  onBeforeUnmount(() => {
    if (notificationTimer) clearInterval(notificationTimer)
    clearWallpaper()
  })

  const LanguageToggle = markRaw(LanguageToggleComponent)

  const ThemeToggle = markRaw(ThemeToggleComponent)
  const ThemePalettePanel = markRaw(ThemePalettePanelComponent)
  const AccountPanel = markRaw(AccountPanelComponent)

  return {
      Bell,
      ShieldAlert,
      isHome,
      wallpaperActive,
      wallpaperStyle,
      isLoggedIn,
      isAdministrator,
      impersonation,
      impersonationEnding,
      impersonationExpiresAt,
      endImpersonation,
      configuration,
      platformError,
      platformLoading,
      ensureLoaded,
      hasUnread,
      t,
      navItems,
      isActive,
      LanguageToggle,
      ThemeToggle,
      ThemePalettePanel,
      AccountPanel
    }
}

export type DefaultLayoutViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useDefaultLayout>>>
