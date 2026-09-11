

import { markRaw } from 'vue'
import HumanVerificationGateComponent from '~/features/security/HumanVerificationGate.vue'
import { formatDocumentTitle, routeTitleKey } from './page-title'

/** Owns state, effects and commands for ApplicationRoot. */
export function useApplicationRoot() {
  const { configuration, ensureLoaded } = usePlatform()

  const route = useRoute()

  const { isDark } = useTheme()

  useHead(() => {
    const titleKey = routeTitleKey(route.path)
    return {
      title: formatDocumentTitle(
        configuration.value?.name,
        titleKey ? translate(titleKey) : null,
      ),
      link: configuration.value?.logoUrl
        ? [{ key: 'platform-icon', rel: 'icon', href: configuration.value.logoUrl }]
        : [],
    }
  })

  void ensureLoaded()

  const pageTransition = { name: 'noctf-page-slide', mode: 'out-in' as const }

  return {
      isDark,
      pageTransition,
      HumanVerificationGate: markRaw(HumanVerificationGateComponent),
    }
}

export type ApplicationRootViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useApplicationRoot>>>
