

import { userProfileGet } from '../../../api'
import type { NoCtfapiEndpointsAuthenticationPublicUserProfileResponse } from '../../../api'

/** Owns state, effects and commands for UsersByIdPage. */
export function useUsersByIdPage() {
  const route = useRoute()

  const userId = route.params.id as string

  const profile = ref<NoCtfapiEndpointsAuthenticationPublicUserProfileResponse | null>(null)

  const loading = ref(true)

  const error = ref<string | null>(null)

  onMounted(async () => {
    const { data, error: err } = await userProfileGet({ path: { userId } })
    loading.value = false
    if (err || !data) {
      error.value = parseApiError(err, translate("ui.userDoesNotExistOrFailedToLoad")).message
      return
    }
    profile.value = data
  })

  return {
      profile,
      loading,
      error
    }
}

export type UsersByIdPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useUsersByIdPage>>>
