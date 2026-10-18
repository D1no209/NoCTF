import { competitionPath } from '../utils/app-routes'
import { competitionChallengeManagementPath, isCompetitionChallengeManagementRoute } from '../features/admin/admin-navigation'
import { isLiveSoloManagementPath } from '../features/live-solo/navigation'
import { useAdminCompetitionResource } from '../features/admin/useAdminCompetitionResource'

/** Competition staff access is scoped to the resource, independently of platform administration. */
export default defineNuxtRouteMiddleware(async (to, from) => {
  const { isLoggedIn, isAdministrator, user } = useAuth()
  if (!isLoggedIn.value)
    return navigateTo({ path: '/auth/login', query: { redirect: to.fullPath } })
  const competitionId = typeof to.params.id === 'string' ? to.params.id : null
  if (!competitionId) return navigateTo('/competitions')
  const challengeManagement = isCompetitionChallengeManagementRoute(to.path, competitionId)
  const soloManagement = isLiveSoloManagementPath(to.path)

  const resource = useAdminCompetitionResource(competitionId)
  const base = `/admin/competitions/${competitionId}`
  if (from?.path !== base && !from?.path?.startsWith(`${base}/`)) resource.invalidate()
  const actor = user.value?.userId
  const result = await resource.read()
  if (actor !== user.value?.userId) return navigateTo('/competitions')
  const { error, response } = result
  const data = resource.data.value
  if (data?.competition && (isAdministrator.value || data.competition.administrationRole)) {
    if (soloManagement && data.competition.mode !== 'LiveSolo') return navigateTo(`/admin/competitions/${competitionId}`, { replace: true })
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
