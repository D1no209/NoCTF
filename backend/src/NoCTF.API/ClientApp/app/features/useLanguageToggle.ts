

import { Languages } from '@lucide/vue'

/** Owns state, effects and commands for LanguageToggle. */
export function useLanguageToggle() {
  const { isEnglish, switchLocale, t } = useLocale()

  return {
      Languages,
      isEnglish,
      switchLocale,
      t
    }
}

export type LanguageToggleViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useLanguageToggle>>>
