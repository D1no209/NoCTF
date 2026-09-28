export default defineNuxtPlugin(async (nuxtApp) => {
  await initializeLocale()
  nuxtApp.vueApp.config.globalProperties.$t = translate
  nuxtApp.vueApp.config.globalProperties.$message = localizeMessage
})
