export default defineNuxtPlugin((nuxtApp) => {
  initializeLocale()
  nuxtApp.vueApp.config.globalProperties.$t = translate
  nuxtApp.vueApp.config.globalProperties.$message = localizeMessage
})
