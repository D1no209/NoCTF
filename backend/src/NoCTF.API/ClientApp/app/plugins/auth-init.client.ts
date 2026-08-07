/** Restore the session from the refresh cookie before the app renders. */
export default defineNuxtPlugin(async () => {
  await useAuth().restore()
})
