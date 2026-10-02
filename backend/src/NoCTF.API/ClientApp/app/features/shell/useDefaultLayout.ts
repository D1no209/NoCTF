import { markRaw } from 'vue'
import { adminWorkspacePath } from '~/features/admin/admin-navigation'

import { Bell, Database, Flag, ShieldAlert } from '@lucide/vue'
import PlatformGearIconComponent from '../../components/ui/icons/PlatformGearIcon.vue'
import AccountPanelComponent from '../account/AccountPanel.vue'
import LanguageToggleComponent from '../LanguageToggle.vue'
import ThemeToggleComponent from '../ThemeToggle.vue'
import ThemePalettePanelComponent from '../theme/ThemePalettePanel.vue'
import { showNotificationNotice } from '../notifications/showNotificationNotice'
import { watchNotifications } from '../../composables/useNotificationHub'
import { createTrailingRefresh } from '../../lib/latest-page-refresh'
import { newNotificationNotices } from '../../composables/useNotificationUnread'

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
  const routePath = computed(() => adminWorkspacePath(route.path))
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
  let unwatchNotifications: (() => void) | undefined
  let notificationBaselineReady = false
  let lastNotificationId: string | null = null

  async function refreshNotifications(showNotice: boolean): Promise<void> {
    const notifications = await refreshUnread()
    if (notifications === undefined) return
    const latestId = notifications[0]?.id ?? null
    if (showNotice && notificationBaselineReady && latestId !== lastNotificationId)
      for (const notification of newNotificationNotices(notifications, lastNotificationId))
        showNotificationNotice(notification)
    lastNotificationId = latestId
    notificationBaselineReady = true
  }

  const refreshOnSignal = createTrailingRefresh(() => refreshNotifications(true))

  function refreshWhenVisible(): void {
    if (document.visibilityState === 'visible') void refreshOnSignal()
  }

  function subscribeNotifications(): void {
    unwatchNotifications?.()
    unwatchNotifications = user.value?.userId
      ? watchNotifications({
          notificationChanged: () => void refreshOnSignal(),
          onReconnected: () => void refreshOnSignal(),
        })
      : undefined
  }

  const navItems = computed(() => [
    { to: '/competitions', label: t("ui.competitions"), icon: Flag, show: true, unread: false },
    { to: '/admin/challenges', label: t("ui.challengeLibrary2"), icon: Database, show: canOrganize.value, unread: false },
    { to: '/admin/platform', label: t("ui.platformAdmin"), icon: markRaw(PlatformGearIconComponent), show: isLoggedIn.value && isAdministrator.value, unread: false },
    { to: '/notifications', label: t("ui.notifications"), icon: Bell, show: isLoggedIn.value, unread: hasUnread.value },
  ])

  function isActive(to: string) {
    return route.path === to || route.path.startsWith(`${to}/`)
  }

  onMounted(() => {
    void refreshNotifications(false)
    subscribeNotifications()
    document.addEventListener('visibilitychange', refreshWhenVisible)
    window.addEventListener('focus', refreshWhenVisible)
    // SignalR is best-effort; reconcile missed changes after long disconnects.
    notificationTimer = setInterval(() => void refreshOnSignal(), 300_000)
  })

  watch(
    () => user.value?.userId,
    () => {
      notificationBaselineReady = false
      lastNotificationId = null
      void refreshNotifications(false)
      subscribeNotifications()
    },
  )

  watch(
    () => [user.value?.userId, user.value?.wallpaperRevision],
    () => void refreshWallpaper(),
    { immediate: true },
  )

  onBeforeUnmount(() => {
    if (notificationTimer) clearInterval(notificationTimer)
    document.removeEventListener('visibilitychange', refreshWhenVisible)
    window.removeEventListener('focus', refreshWhenVisible)
    unwatchNotifications?.()
    clearWallpaper()
  })

  const LanguageToggle = markRaw(LanguageToggleComponent)

  const ThemeToggle = markRaw(ThemeToggleComponent)
  const ThemePalettePanel = markRaw(ThemePalettePanelComponent)
  const AccountPanel = markRaw(AccountPanelComponent)

  return {
      ShieldAlert,
      isHome,
      routePath,
      wallpaperActive,
      wallpaperStyle,
      isLoggedIn,
      impersonation,
      impersonationEnding,
      impersonationExpiresAt,
      endImpersonation,
      configuration,
      platformError,
      platformLoading,
      ensureLoaded,
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
