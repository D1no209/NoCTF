import type { NuxtError } from '#app'
import { markRaw } from 'vue'
import { ArrowLeft, Flag, House, LogIn, RotateCcw } from '@lucide/vue'
import LanguageToggleComponent from '../LanguageToggle.vue'
import ThemeToggleComponent from '../ThemeToggle.vue'
import { errorDisplayPath, errorPagePresentation, errorRecoveryPath, type ErrorRecoveryAction } from './error-page'
import { formatDocumentTitle } from './page-title'

/** A small independent shell: recovery must not depend on account, notification or competition loading. */
export function useApplicationError(error: NuxtError) {
  const { configuration, ensureLoaded } = usePlatform()
  const { t } = useLocale()
  const router = useRouter()
  const route = useRoute()
  // Keep the failure stable while a recovery command opens another page.
  const presentation = errorPagePresentation(error.status ?? error.statusCode)
  const failedPath = errorRecoveryPath(route.fullPath) ?? '/'
  const displayPath = presentation.showPath ? errorDisplayPath(failedPath) : ''
  const previousPath = ref<string | null>(null)
  const recovering = ref(false)
  const navigationFailed = ref(false)
  const icons = { home: markRaw(House), competitions: markRaw(Flag), login: markRaw(LogIn), retry: markRaw(RotateCcw) }
  const secondaryAction: ErrorRecoveryAction = presentation.action === 'home' ? 'competitions' : 'home'
  const secondaryKey = secondaryAction === 'home' ? 'errorPage.action.home' : 'errorPage.action.competitions'

  onMounted(() => {
    const back = errorRecoveryPath(window.history.state?.back)
    if (back && back !== failedPath && router.resolve(back).matched.length) previousPath.value = back
  })

  function recoverTo(path: string): void {
    if (recovering.value) return
    recovering.value = true
    navigationFailed.value = false
    try {
      // Initial-route errors can leave Nuxt's app bootstrap incomplete. A full
      // document navigation also recovers plugins instead of reusing that app.
      window.location.assign(path)
    }
    catch {
      recovering.value = false
      navigationFailed.value = true
    }
  }

  function recover(action: ErrorRecoveryAction): void {
    if (recovering.value) return
    if (action === 'retry') {
      recovering.value = true
      window.location.reload()
      return
    }
    const path = action === 'login'
      ? `/auth/login?redirect=${encodeURIComponent(failedPath)}`
      : action === 'competitions' ? '/competitions' : '/'
    return recoverTo(path)
  }

  useHead(() => ({
    title: formatDocumentTitle(configuration.value?.name, t(presentation.titleKey)),
    meta: [{ name: 'robots', content: 'noindex' }],
    link: configuration.value?.logoUrl
      ? [{ key: 'platform-icon', rel: 'icon', href: configuration.value.logoUrl }]
      : [],
  }))
  void ensureLoaded()

  return {
    configuration, t, displayPath, previousPath, recovering, navigationFailed,
    statusCode: presentation.status,
    title: computed(() => t(presentation.titleKey)),
    description: computed(() => t(presentation.descriptionKey)),
    primaryLabel: computed(() => t(presentation.actionKey)),
    secondaryLabel: computed(() => t(secondaryKey)),
    PrimaryIcon: icons[presentation.action],
    SecondaryIcon: icons[secondaryAction],
    ArrowLeft: markRaw(ArrowLeft),
    LanguageToggle: markRaw(LanguageToggleComponent),
    ThemeToggle: markRaw(ThemeToggleComponent),
    primaryAction: () => recover(presentation.action),
    secondaryAction: () => recover(secondaryAction),
    goHome: () => recoverTo('/'),
    goBack: () => recoverTo(previousPath.value ?? '/'),
  }
}

export type ApplicationErrorViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useApplicationError>>
