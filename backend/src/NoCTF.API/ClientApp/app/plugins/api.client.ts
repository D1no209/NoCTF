import { initializeApiClient } from '../lib/api'
import { getAccessToken, refreshSession, isImpersonatingSession, requestImpersonationEnd } from '../lib/session'

export default defineNuxtPlugin(() => {
  initializeApiClient({
    token: getAccessToken,
    refresh: refreshSession,
    impersonating: isImpersonatingSession,
    endImpersonation: () => requestImpersonationEnd('unauthorized'),
  })
})
