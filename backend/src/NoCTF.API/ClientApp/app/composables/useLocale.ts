import { animateLocaleLayout } from '~/motion/locale-layout'

export function useLocale() {
  const locale = computed(() => currentLocale())
  const isEnglish = computed(() => locale.value === 'en')

  async function switchLocale() {
    const nextLocale = isEnglish.value ? 'zh-CN' : 'en'
    await prepareLocale(nextLocale)
    await animateLocaleLayout(() => setLocale(nextLocale))
  }

  return {
    locale,
    isEnglish,
    switchLocale,
    t: translate,
  }
}
