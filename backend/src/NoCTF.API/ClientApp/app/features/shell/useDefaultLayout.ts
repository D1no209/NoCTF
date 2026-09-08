import { markRaw } from 'vue'

import { Bell, CalendarCog, Database, Flag, LogOut, ShieldCheck, User } from '@lucide/vue'
import LanguageToggleComponent from '../LanguageToggle.vue'
import ThemeToggleComponent from '../ThemeToggle.vue'

/** Owns state, effects and commands for DefaultLayout. */
export function useDefaultLayout() {
  const { user, isLoggedIn, isAdministrator, canOrganize, logout } = useAuth()

  const { configuration, error: platformError, loading: platformLoading, ensureLoaded } = usePlatform()

  const route = useRoute()

  const { hasUnread, refreshUnread } = useNotificationUnread()

  const { t } = useLocale()

  let notificationTimer: ReturnType<typeof setInterval> | undefined

  const navItems = computed(() => [
    { to: '/competitions', label: t("ui.competitions"), icon: Flag, show: true },
    { to: '/admin/competitions', label: t("ui.competitionAdmin"), icon: CalendarCog, show: isLoggedIn.value },
    { to: '/admin/challenges', label: t("ui.challengeLibrary2"), icon: Database, show: canOrganize.value },
    { to: '/admin/platform', label: t("ui.platformAdmin"), icon: ShieldCheck, show: isAdministrator.value },
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

  onBeforeUnmount(() => {
    if (notificationTimer) clearInterval(notificationTimer)
  })

  const LanguageToggle = markRaw(LanguageToggleComponent)

  const ThemeToggle = markRaw(ThemeToggleComponent)

  return {
      Bell,
      LogOut,
      ShieldCheck,
      User,
      user,
      isLoggedIn,
      isAdministrator,
      logout,
      configuration,
      platformError,
      platformLoading,
      ensureLoaded,
      hasUnread,
      t,
      navItems,
      isActive,
      LanguageToggle,
      ThemeToggle
    }
}

export type DefaultLayoutViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useDefaultLayout>>>
