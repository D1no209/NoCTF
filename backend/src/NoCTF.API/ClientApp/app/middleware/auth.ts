/** Require an authenticated session; otherwise bounce to login with a redirect back. */
export default defineNuxtRouteMiddleware((to) => {
  const { isLoggedIn } = useAuth()
  if (!isLoggedIn.value) {
    return navigateTo({ path: '/auth/login', query: { redirect: to.fullPath } })
  }
})
