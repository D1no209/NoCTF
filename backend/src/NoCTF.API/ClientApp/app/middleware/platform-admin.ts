/** Platform administration (Administrator role only). */
export default defineNuxtRouteMiddleware((to) => {
  const { isLoggedIn, isAdministrator } = useAuth()
  if (!isLoggedIn.value) {
    return navigateTo({ path: '/auth/login', query: { redirect: to.fullPath } })
  }
  if (!isAdministrator.value) {
    return navigateTo('/')
  }
})
