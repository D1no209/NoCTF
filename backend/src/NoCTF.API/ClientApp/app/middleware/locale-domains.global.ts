import { localeDomainsForPath } from '~/locales/route-domains'

export default defineNuxtRouteMiddleware(async (to) => {
  await ensureLocaleDomains(localeDomainsForPath(to.path))
})
