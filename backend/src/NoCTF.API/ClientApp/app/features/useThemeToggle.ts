

import { Moon, Sun } from '@lucide/vue'

/** Owns state, effects and commands for ThemeToggle. */
export function useThemeToggle() {
  const { isDark, themeTransitioning, toggle } = useTheme()

  const { t } = useLocale()

  return {
      Moon,
      Sun,
      isDark,
      themeTransitioning,
      toggle,
      t
    }
}

export type ThemeToggleViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useThemeToggle>>>
