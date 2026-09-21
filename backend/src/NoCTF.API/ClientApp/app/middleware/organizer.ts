/** Challenge-bank administration for organizers and platform administrators. */
export default defineNuxtRouteMiddleware((to) => {
  const { isLoggedIn, canOrganize } = useAuth()
  if (!isLoggedIn.value) {
    return navigateTo({ path: '/auth/login', query: { redirect: to.fullPath } })
  }
  if (!canOrganize.value) {
    return navigateTo('/')
  }
})
