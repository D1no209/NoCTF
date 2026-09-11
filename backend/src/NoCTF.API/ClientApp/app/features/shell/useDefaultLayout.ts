import { markRaw } from 'vue'

import { Bell, Database, Flag, ShieldAlert } from '@lucide/vue'
import AccountPanelComponent from '../account/AccountPanel.vue'
import LanguageToggleComponent from '../LanguageToggle.vue'
import ThemeToggleComponent from '../ThemeToggle.vue'
import ThemePalettePanelComponent from '../theme/ThemePalettePanel.vue'

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

  const navItems = computed(() => [
    { to: '/competitions', label: t("ui.competitions"), icon: Flag, show: true },
    { to: '/admin/challenges', label: t("ui.challengeLibrary2"), icon: Database, show: canOrganize.value },
  ])

  function isActive(to: string) {
    return route.path === to || route.path.startsWith(`${to}/`)
  }

  onMounted(() => {
    void refreshUnread()
    notificationTimer = setInterval(() => void refreshUnread(), 20_000)
  })

  watch(
    () => user.value?.userId,
    () => void refreshUnread(),
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
