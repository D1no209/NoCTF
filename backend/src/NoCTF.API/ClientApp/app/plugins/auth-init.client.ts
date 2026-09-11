import { setImpersonationEndHandler, setSessionInvalidationHandler } from '../lib/session'

/** Restore the session from the refresh cookie before the app renders. */
export default defineNuxtPlugin(async (nuxtApp) => {
  const auth = useAuth()
  const removeInvalidationHandler = setSessionInvalidationHandler(auth.invalidate)
  const removeImpersonationHandler = setImpersonationEndHandler(() => auth.endImpersonation())
  nuxtApp.vueApp.onUnmount(removeInvalidationHandler)
  nuxtApp.vueApp.onUnmount(removeImpersonationHandler)
  await auth.restore()
})
