

/** Owns state, effects and commands for CompetitionsByIdNotificationsPage. */
export function useCompetitionsByIdNotificationsPage() {
  const route = useRoute()

  const notification = typeof route.query.notification === 'string' ? route.query.notification : undefined

  async function initialize() {
    await navigateTo({ path: '/notifications', query: notification ? { notification } : {} }, { replace: true })
  }

  return {
      initialize,

    }
}

export type CompetitionsByIdNotificationsPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionsByIdNotificationsPage>>>
