

import { Moon, Sun } from '@lucide/vue'

/** Owns state, effects and commands for ThemeToggle. */
export function useThemeToggle() {
  const { isDark, toggle } = useTheme()

  const { t } = useLocale()

  return {
      Moon,
      Sun,
      isDark,
      toggle,
      t
    }
}

export type ThemeToggleViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useThemeToggle>>>
