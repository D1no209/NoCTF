export default defineNuxtPlugin((nuxtApp) => {
  initializeLocale()
  nuxtApp.vueApp.config.globalProperties.$t = translate
})
