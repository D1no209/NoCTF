import { useDark, useToggle } from '@vueuse/core'

/** 深浅色主题:默认深色,用户选择经 localStorage 持久化(vueuse-color-scheme)。 */
export function useTheme() {
  const isDark = useDark({ initialValue: 'dark' })
  const toggle = useToggle(isDark)
  return { isDark, toggle }
}
