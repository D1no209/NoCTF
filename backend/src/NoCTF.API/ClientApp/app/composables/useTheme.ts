import { useDark, useToggle } from '@vueuse/core'

interface ThemeTransitionDocument {
  startViewTransition?: (update: () => void | Promise<void>) => {
    finished: Promise<void>
  }
}

/** 深浅色主题:默认深色,用户选择经 localStorage 持久化(vueuse-color-scheme)。 */
export function useTheme() {
  const isDark = useDark({ initialValue: 'dark' })
  const toggleDark = useToggle(isDark)
  const themeTransitioning = useState<boolean>('theme:transitioning', () => false)

  function toggle() {
    if (!import.meta.client || themeTransitioning.value) return
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    const transitionDocument = document as ThemeTransitionDocument
    if (reducedMotion || !transitionDocument.startViewTransition) {
      toggleDark()
      return
    }

    themeTransitioning.value = true
    document.documentElement.dataset.themeTransition = 'down'
    const transition = transitionDocument.startViewTransition(async () => {
      toggleDark()
      await nextTick()
    })
    void transition.finished.finally(() => {
      delete document.documentElement.dataset.themeTransition
      themeTransitioning.value = false
    })
  }

  return { isDark, themeTransitioning, toggle }
}
