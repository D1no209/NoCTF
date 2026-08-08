/** Guest-only pages (login/register/...); signed-in users go home. */
export default defineNuxtRouteMiddleware(() => {
  const { isLoggedIn } = useAuth()
  if (isLoggedIn.value) {
    return navigateTo('/')
  }
})
