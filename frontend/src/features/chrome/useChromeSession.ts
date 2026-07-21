import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'

// Shared session chrome state (who is signed in, what they may manage) plus
// the canonical logout flow: clear auth, reset the score store (V1 NavBar
// behavior), then land on the login page.
export function useChromeSession() {
  const router = useRouter()
  const auth = useAuthStore()
  const scoreStore = useScoreStore()

  const isAuthenticated = computed(() => auth.isAuthenticated)
  const userRole = computed(() => auth.userRole)
  const userName = computed(() => auth.user?.userName ?? '')
  const canManage = computed(() => ['Admin', 'Organizer'].includes(auth.userRole))
  const isAdmin = computed(() => auth.userRole === 'Admin')

  async function logout() {
    auth.logout()
    scoreStore.reset()
    await router.push('/login')
  }

  return {
    isAuthenticated,
    userRole,
    userName,
    canManage,
    isAdmin,
    logout,
  }
}
