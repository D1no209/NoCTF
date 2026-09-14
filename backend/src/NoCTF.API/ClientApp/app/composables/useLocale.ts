import { animateLocaleLayout } from '~/motion/locale-layout'

export function useLocale() {
  const locale = computed(() => currentLocale())
  const isEnglish = computed(() => locale.value === 'en')

  async function switchLocale() {
    await animateLocaleLayout(() => setLocale(isEnglish.value ? 'zh-CN' : 'en'))
  }

  return {
    locale,
    isEnglish,
    switchLocale,
    t: translate,
  }
}
