import { adminGetCompetition } from '../api'
import { competitionPath } from '../utils/app-routes'
import { competitionChallengeManagementPath, isCompetitionChallengeManagementRoute } from '../features/admin/admin-navigation'

/** Competition staff access is scoped to the resource, independently of platform administration. */
export default defineNuxtRouteMiddleware(async (to) => {
  const { isLoggedIn, isAdministrator } = useAuth()
  if (!isLoggedIn.value)
    return navigateTo({ path: '/auth/login', query: { redirect: to.fullPath } })
  const competitionId = typeof to.params.id === 'string' ? to.params.id : null
  if (!competitionId) return navigateTo('/competitions')
  const challengeManagement = isCompetitionChallengeManagementRoute(to.path, competitionId)
  if (isAdministrator.value && !challengeManagement) return

  const { data, error, response } = await adminGetCompetition({ path: { competitionId } })
  if (!error && data?.competition && (isAdministrator.value || data.competition.administrationRole)) {
    if (challengeManagement && data.competition.mode === 'LiveSolo') {
      return navigateTo({
        path: competitionChallengeManagementPath(competitionId, data.competition.mode),
        query: { ...to.query, ...(typeof to.params.ccId === 'string' ? { question: to.params.ccId } : {}) },
      }, { replace: true })
    }
    return
  }
  if (response?.status === 401)
    return navigateTo({ path: '/auth/login', query: { redirect: to.fullPath } })
  if (!error || response?.status === 403 || response?.status === 404)
    return navigateTo(competitionPath(competitionId))
  throw createError({ statusCode: response?.status ?? 503, statusMessage: translate('common.error.loadingCompetitionFailed') })
})
