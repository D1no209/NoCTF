export function useLocale() {
  const locale = computed(() => currentLocale())
  const isEnglish = computed(() => locale.value === 'en')

  function switchLocale() {
    setLocale(isEnglish.value ? 'zh-CN' : 'en')
    window.location.reload()
  }

  return {
    locale,
    isEnglish,
    switchLocale,
    t: translate,
  }
}
