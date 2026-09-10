import { setSessionInvalidationHandler } from '../lib/session'

/** Restore the session from the refresh cookie before the app renders. */
export default defineNuxtPlugin(async (nuxtApp) => {
  const auth = useAuth()
  const removeInvalidationHandler = setSessionInvalidationHandler(auth.invalidate)
  nuxtApp.vueApp.onUnmount(removeInvalidationHandler)
  await auth.restore()
})
