import { useDark, useToggle } from '@vueuse/core'
import { runDownRevealTransition } from '../motion/reveal-transition'

/** 深浅色主题:默认深色,用户选择经 localStorage 持久化(vueuse-color-scheme)。 */
export function useTheme() {
  const isDark = useDark({ initialValue: 'dark' })
  const toggleDark = useToggle(isDark)
  const themeTransitioning = useState<boolean>('theme:transitioning', () => false)

  function toggle() {
    if (!import.meta.client || themeTransitioning.value) return
    themeTransitioning.value = true
    void runDownRevealTransition('theme', () => {
      toggleDark()
    }).finally(() => {
      themeTransitioning.value = false
    })
  }

  return { isDark, themeTransitioning, toggle }
}
