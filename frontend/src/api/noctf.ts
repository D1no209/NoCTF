import { client } from './generated/client.gen'
import * as sdk from './generated/sdk.gen'
import { translate as tt } from '@/i18n'

export class ApiError extends Error {
  readonly status?: number
  readonly details?: unknown

  constructor(message: string, status?: number, details?: unknown) {
    super(message)
    this.status = status
    this.details = details
  }
}

type ApiResult<T> = {
  data?: T
  error?: unknown
  response?: Response
}

function unwrap<T>(result: ApiResult<T>, fallback = tt('errors.requestFailed')): T {
  if (result.error) {
    const status = result.response?.status
    const details = isEmptyObject(result.error) && status
      ? `HTTP ${status}`
      : result.error
    throw new ApiError(fallback, status, details)
  }
  return result.data as T
}

async function requireSuccess<T>(request: Promise<ApiResult<T>>, fallback?: string): Promise<T> {
  return unwrap(await request, fallback)
}

function isEmptyObject(value: unknown) {
  return Boolean(
    value &&
    typeof value === 'object' &&
    !Array.isArray(value) &&
    Object.keys(value).length === 0,
  )
}

export function apiUrl(path: string) {
  const baseUrl = import.meta.env.VITE_API_BASE_URL ?? ''
  if (!baseUrl || /^https?:\/\//i.test(path)) return path
  return new URL(path, baseUrl).toString()
}

export function setAuthToken(token: string | null) {
  client.setConfig({
    headers: { Authorization: token ? `Bearer ${token}` : '' },
  })
}

export interface UserNotification {
  id: string
  competitionId?: string | null
  subjectId?: string | null
  type: string
  data: Record<string, string>
  isRead: boolean
  createdAt: string
  readAt?: string | null
}

export interface UserNotificationPage {
  items: UserNotification[]
  unreadCount: number
}

export const notificationApi = {
  async list(limit = 20) {
    return unwrap(await client.get<{ 200: UserNotificationPage }, unknown, false>({
      url: '/api/notifications',
      query: { limit },
    }), tt('errors.loadNotifications'))
  },
  async markRead(id: string) {
    await requireSuccess(client.post<{ 204: never }, unknown, false>({
      url: '/api/notifications/{id}/read',
      path: { id },
    }), tt('errors.updateNotifications'))
  },
  async markAllRead() {
    await requireSuccess(client.post<{ 204: never }, unknown, false>({
      url: '/api/notifications/read-all',
    }), tt('errors.updateNotifications'))
  },
}

export const authApi = {
  async login(email: string, password: string) {
    return unwrap(await sdk.noCtfapiEndpointsAuthLoginEndpoint({ body: { email, password } }), tt('errors.login'))
  },
  async register(userName: string, email: string, password: string) {
    return unwrap(await sdk.noCtfapiEndpointsAuthRegisterEndpoint({ body: { userName, email, password } }), tt('errors.registration'))
  },
  async verifyEmail(token: string) {
    return unwrap(await client.post<{ 200: { status: string } }, unknown, false>({
      url: '/api/auth/email-verification/verify',
      body: { token },
    }), tt('errors.verifyEmail'))
  },
  async resendEmailVerification(email: string) {
    return unwrap(await client.post<{ 202: string }, unknown, false>({
      url: '/api/auth/email-verification/resend',
      body: { email },
    }), tt('errors.resendVerification'))
  },
  async refresh() {
    return unwrap(await sdk.noCtfapiEndpointsAuthRefreshTokenEndpoint(), tt('errors.refreshToken'))
  },
}

export const competitionApi = {
  async list<T = unknown[]>() {
    return unwrap(await client.get<{ 200: T }, unknown, false>({ url: '/api/competitions' }), tt('errors.loadCompetitions'))
  },
  async get<T = unknown>(id: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/competitions/{id}',
      path: { id },
    }), tt('errors.loadCompetition'))
  },
  async create(body: Parameters<typeof sdk.noCtfapiEndpointsCompetitionsCreateCompetitionEndpoint>[0]['body']) {
    return unwrap(await sdk.noCtfapiEndpointsCompetitionsCreateCompetitionEndpoint({ body }), tt('errors.createCompetition'))
  },
  async challenges<T = unknown[]>(competitionId: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/competitions/{id}/challenges',
      path: { id: competitionId },
    }), tt('errors.loadChallenges'))
  },
  async submissions<T = unknown>(competitionId: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/competitions/{id}/submissions',
      path: { id: competitionId },
    }), tt('errors.loadSubmissions'))
  },
  async leaderboard(competitionId: string) {
    return unwrap(await sdk.noCtfapiEndpointsCompetitionsGetLeaderboardEndpoint({
      path: { competitionId },
    }), tt('errors.loadLeaderboard'))
  },
  async leaderboardTrend<T = unknown>(competitionId: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/competitions/{competitionId}/leaderboard/trend',
      path: { competitionId },
    }), tt('errors.loadLeaderboardTrend'))
  },
  async leaderboardTeam<T = unknown>(competitionId: string, teamId: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/competitions/{competitionId}/leaderboard/teams/{teamId}',
      path: { competitionId, teamId },
    }), tt('errors.loadLeaderboardTeam'))
  },
  async submitFlag<T = unknown>(competitionId: string, challengeId: string, flag: string) {
    return unwrap(await client.post<{ 200: T }, unknown, false>({
      url: '/api/competitions/{id}/challenges/{challengeId}/submit',
      path: { id: competitionId, challengeId },
      body: { flag },
    }), tt('errors.submitFlag'))
  },
  async createInstance<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(await client.post<{ 200: T }, unknown, false>({
      url: '/api/competitions/{id}/challenges/{challengeId}/instance',
      path: { id: competitionId, challengeId },
      body: {},
    }), tt('errors.createInstance'))
  },
  async getInstance<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/competitions/{id}/challenges/{challengeId}/instance',
      path: { id: competitionId, challengeId },
    }), tt('errors.loadInstance'))
  },
  async destroyInstance<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(await client.delete<{ 200: T }, unknown, false>({
      url: '/api/competitions/{id}/challenges/{challengeId}/instance',
      path: { id: competitionId, challengeId },
    }), tt('errors.destroyInstance'))
  },
  async extendInstance<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(await client.post<{ 200: T }, unknown, false>({
      url: '/api/competitions/{id}/challenges/{challengeId}/instance/extend',
      path: { id: competitionId, challengeId },
      body: {},
    }), tt('errors.extendInstance'))
  },
  async awdDashboard<T = unknown>(competitionId: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/competitions/{id}/awd-dashboard',
      path: { id: competitionId },
    }), tt('errors.loadAwdDashboard'))
  },
  async kohDashboard<T = unknown>(competitionId: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/competitions/{id}/koh-dashboard',
      path: { id: competitionId },
    }), tt('errors.loadKohDashboard'))
  },
  async view<T = unknown>(competitionId: string, viewKey: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/competitions/{id}/views/{viewKey}',
      path: { id: competitionId, viewKey },
    }), tt('errors.loadCompetitionView'))
  },
  async teams<T = unknown[]>(competitionId: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/competitions/{id}/teams',
      path: { id: competitionId },
    }), tt('errors.loadAdminTeams'))
  },
  async myTeams<T = unknown[]>(competitionId: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/competitions/{id}/teams/mine',
      path: { id: competitionId },
    }), tt('errors.loadMyTeams'))
  },
  async submitPatch<T = unknown>(competitionId: string, challengeId: string, file: File) {
    const body = new FormData()
    body.append('file', file)
    return unwrap(await client.post<{ 202: T }, unknown, false>({
      url: '/api/competitions/{id}/challenges/{challengeId}/patch',
      path: { id: competitionId, challengeId },
      body,
    }), tt('errors.submitPatch'))
  },
  async patchSubmissions<T = unknown[]>(competitionId: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/competitions/{id}/patch-submissions',
      path: { id: competitionId },
    }), tt('errors.loadPatchSubmissions'))
  },
  async patchSubmission(competitionId: string, submissionId: string) {
    return unwrap(await sdk.noCtfapiEndpointsCompetitionsGetPatchSubmissionEndpoint({
      path: { id: competitionId, submissionId },
    }), tt('errors.loadPatchSubmissions'))
  },
  async capabilities(competitionId: string) {
    return unwrap(await sdk.noCtfapiEndpointsCompetitionsGetCompetitionCapabilitiesEndpoint({
      path: { id: competitionId },
    }), tt('errors.loadCompetition'))
  },
  async action(
    competitionId: string,
    actionKey: string,
    body: Parameters<typeof sdk.noCtfapiEndpointsCompetitionsPostCompetitionActionEndpoint>[0]['body'],
  ) {
    return unwrap(await sdk.noCtfapiEndpointsCompetitionsPostCompetitionActionEndpoint({
      path: { id: competitionId, actionKey },
      body,
    }), tt('errors.requestFailed'))
  },
  async scoreboard(competitionId: string) {
    return unwrap(await sdk.noCtfapiEndpointsCompetitionsGetCompetitionScoreboardEndpoint({
      path: { id: competitionId },
    }), tt('errors.loadLeaderboard'))
  },
}

export const penetrationApi = {
  async detail(competitionId: string, challengeId: string) {
    return unwrap(await sdk.noCtfapiEndpointsCompetitionsGetPenetrationChallengeEndpoint({
      path: { id: competitionId, challengeId },
    }), tt('errors.loadChallenges'))
  },
  async instance(competitionId: string, challengeId: string) {
    return unwrap(await sdk.noCtfapiEndpointsCompetitionsGetPenetrationInstanceEndpoint({
      path: { id: competitionId, challengeId },
    }), tt('errors.loadInstance'))
  },
  async start(competitionId: string, challengeId: string) {
    return unwrap(await sdk.noCtfapiEndpointsCompetitionsStartPenetrationInstanceEndpoint({
      path: { id: competitionId, challengeId },
    }), tt('errors.createInstance'))
  },
  async stop(competitionId: string, challengeId: string) {
    return unwrap(await sdk.noCtfapiEndpointsCompetitionsStopPenetrationInstanceEndpoint({
      path: { id: competitionId, challengeId },
    }), tt('errors.destroyInstance'))
  },
  async reset(competitionId: string, challengeId: string) {
    return unwrap(await sdk.noCtfapiEndpointsCompetitionsResetPenetrationInstanceEndpoint({
      path: { id: competitionId, challengeId },
    }), tt('errors.requestFailed'))
  },
  async destroy(competitionId: string, challengeId: string) {
    return unwrap(await sdk.noCtfapiEndpointsCompetitionsDestroyPenetrationInstanceEndpoint({
      path: { id: competitionId, challengeId },
    }), tt('errors.destroyInstance'))
  },
  async submitFlag(competitionId: string, challengeId: string, flag: string) {
    return unwrap(await sdk.noCtfapiEndpointsCompetitionsSubmitPenetrationFlagEndpoint({
      path: { id: competitionId, challengeId },
      // The generated schema combines an empty base request with the body,
      // producing an impossible intersection. Keep the workaround localized.
      body: { flag } as unknown as Parameters<typeof sdk.noCtfapiEndpointsCompetitionsSubmitPenetrationFlagEndpoint>[0]['body'],
    }), tt('errors.submitFlag'))
  },
}

export const teamApi = {
  async mine<T = unknown[]>() {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/teams/mine',
    }), tt('errors.loadMyTeams'))
  },
  async create<T = unknown>(body: { competitionId: string; name: string; avatarUrl?: string; trackName?: string }) {
    return unwrap(await client.post<{ 201: T }, unknown, false>({
      url: '/api/teams',
      body,
    }), tt('errors.createTeam'))
  },
  async join<T = unknown>(teamId: string) {
    return unwrap(await client.post<{ 200: T }, unknown, false>({
      url: '/api/teams/{teamId}/join',
      path: { teamId },
    }), tt('errors.joinTeam'))
  },
  async joinByToken<T = unknown>(token: string) {
    return unwrap(await client.post<{ 200: T }, unknown, false>({
      url: '/api/teams/join-by-token',
      body: { token },
    }), tt('errors.joinTeam'))
  },
  async leave(teamId: string) {
    await requireSuccess(client.post({ url: '/api/teams/{teamId}/leave', path: { teamId } }), tt('errors.requestFailed'))
  },
  async update(id: string, body: Parameters<typeof sdk.noCtfapiEndpointsTeamsUpdateTeamEndpoint>[0]['body']) {
    return unwrap(await sdk.noCtfapiEndpointsTeamsUpdateTeamEndpoint({ path: { id }, body }), tt('errors.updateTeam'))
  },
  async transferCaptain(teamId: string, newCaptainUserId: string) {
    await requireSuccess(client.post({
      url: '/api/teams/{teamId}/transfer-captain',
      path: { teamId },
      body: { newCaptainUserId },
    }), tt('errors.requestFailed'))
  },
  async removeMember(teamId: string, userId: string) {
    await requireSuccess(client.delete({
      url: '/api/teams/{teamId}/members/{userId}',
      path: { teamId, userId },
    }), tt('errors.requestFailed'))
  },
}

export const penetrationAdminApi = {
  async templateTopology(templateId: string) {
    return unwrap(await sdk.noCtfapiEndpointsAdminGetPenetrationTemplateTopologyEndpoint({
      path: { templateId },
    }), tt('errors.loadChallenges'))
  },
  async updateTemplateTopology(
    templateId: string,
    body: Parameters<typeof sdk.noCtfapiEndpointsAdminUpdatePenetrationTemplateTopologyEndpoint>[0]['body'],
  ) {
    return unwrap(await sdk.noCtfapiEndpointsAdminUpdatePenetrationTemplateTopologyEndpoint({
      path: { templateId },
      body,
    }), tt('errors.updateChallenge'))
  },
  async competitionTopology(competitionId: string, challengeId: string) {
    return unwrap(await sdk.noCtfapiEndpointsAdminGetPenetrationCompetitionTopologyEndpoint({
      path: { competitionId, challengeId },
    }), tt('errors.loadChallenges'))
  },
  async updateCompetitionTopology(
    competitionId: string,
    challengeId: string,
    body: Parameters<typeof sdk.noCtfapiEndpointsAdminUpdatePenetrationCompetitionTopologyEndpoint>[0]['body'],
  ) {
    return unwrap(await sdk.noCtfapiEndpointsAdminUpdatePenetrationCompetitionTopologyEndpoint({
      path: { competitionId, challengeId },
      body,
    }), tt('errors.updateChallenge'))
  },
  async instances(
    competitionId: string,
    query?: Parameters<typeof sdk.noCtfapiEndpointsAdminListPenetrationAdminInstancesEndpoint>[0]['query'],
  ) {
    return unwrap(await sdk.noCtfapiEndpointsAdminListPenetrationAdminInstancesEndpoint({
      path: { competitionId },
      query,
    }), tt('errors.loadChallenges'))
  },
  async instance(competitionId: string, instanceId: string) {
    return unwrap(await sdk.noCtfapiEndpointsAdminGetPenetrationAdminInstanceEndpoint({
      path: { competitionId, instanceId },
    }), tt('errors.loadChallenges'))
  },
  async resetInstance(competitionId: string, instanceId: string) {
    return unwrap(await sdk.noCtfapiEndpointsAdminResetPenetrationAdminInstanceEndpoint({
      path: { competitionId, instanceId },
    }), tt('errors.requestFailed'))
  },
  async destroyInstance(competitionId: string, instanceId: string) {
    return unwrap(await sdk.noCtfapiEndpointsAdminDestroyPenetrationAdminInstanceEndpoint({
      path: { competitionId, instanceId },
    }), tt('errors.destroyInstance'))
  },
}

export const adminApi = {
  async competitions<T = unknown[]>() {
    return unwrap(await client.get<{ 200: T }, unknown, false>({ url: '/api/admin/competitions' }), tt('errors.loadCompetitions'))
  },
  async competition<T = unknown>(id: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({ url: '/api/admin/competitions/{id}', path: { id } }), tt('errors.loadCompetition'))
  },
  async createCompetition<T = unknown>(body: unknown) {
    return unwrap(await client.post<{ 201: T }, unknown, false>({ url: '/api/admin/competitions', body }), tt('errors.createCompetition'))
  },
  async updateCompetition<T = unknown>(id: string, body: unknown) {
    return unwrap(await client.put<{ 200: T }, unknown, false>({ url: '/api/admin/competitions/{id}', path: { id }, body }), tt('errors.updateCompetition'))
  },
  async deleteCompetition(id: string) {
    await requireSuccess(client.delete({ url: '/api/admin/competitions/{id}', path: { id } }), tt('errors.requestFailed'))
  },
  async users<T = unknown[]>() {
    return unwrap(await client.get<{ 200: T }, unknown, false>({ url: '/api/admin/users' }), tt('errors.loadUsers'))
  },
  async updateUserRole(id: string, role: string) {
    await requireSuccess(client.post({ url: '/api/admin/users/{id}/role', path: { id }, body: { role } }), tt('errors.requestFailed'))
  },
  async resetUserPassword(id: string, newPassword: string) {
    await requireSuccess(client.post({ url: '/api/admin/users/{id}/reset-password', path: { id }, body: { newPassword } }), tt('errors.requestFailed'))
  },
  async teams<T = unknown[]>() {
    return unwrap(await client.get<{ 200: T }, unknown, false>({ url: '/api/admin/teams' }), tt('errors.loadAdminTeams'))
  },
  async deleteTeam(id: string) {
    await requireSuccess(client.delete({ url: '/api/admin/teams/{id}', path: { id } }), tt('errors.requestFailed'))
  },
  async teamMembers<T = unknown[]>(teamId: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/admin/teams/{teamId}/members',
      path: { teamId },
    }), tt('errors.loadTeamMembers'))
  },
  async challenges<T = unknown[]>() {
    return unwrap(await client.get<{ 200: T }, unknown, false>({ url: '/api/admin/challenges' }), tt('errors.loadChallenges'))
  },
  async createChallenge<T = unknown>(body: unknown) {
    return unwrap(await client.post<{ 201: T }, unknown, false>({ url: '/api/admin/challenges', body }), tt('errors.createChallenge'))
  },
  async uploadChallengeAttachment<T = unknown>(id: string, file: File) {
    const body = new FormData()
    body.append('file', file)
    return unwrap(await client.post<{ 200: T }, unknown, false>({
      url: '/api/admin/challenges/{id}/attachment',
      path: { id },
      body,
    }), tt('errors.uploadAttachment'))
  },
  async uploadChallengePatchTemplate<T = unknown>(id: string, file: File) {
    const body = new FormData()
    body.append('file', file)
    return unwrap(await client.post<{ 200: T }, unknown, false>({
      url: '/api/admin/challenges/{id}/patch-template',
      path: { id },
      body,
    }), tt('errors.uploadPatchTemplate'))
  },
  async updateChallenge<T = unknown>(id: string, body: unknown) {
    return unwrap(await client.put<{ 200: T }, unknown, false>({ url: '/api/admin/challenges/{id}', path: { id }, body }), tt('errors.updateChallenge'))
  },
  async deleteChallenge(id: string) {
    await requireSuccess(client.delete({ url: '/api/admin/challenges/{id}', path: { id } }), tt('errors.requestFailed'))
  },
  async revealChallengeSecret<T = unknown>(id: string) {
    return unwrap(await client.post<{ 200: T }, unknown, false>({
      url: '/api/admin/challenges/{id}/reveal-secret',
      path: { id },
      body: {},
    }), tt('errors.revealSecret'))
  },
  async competitionChallenges<T = unknown[]>(competitionId: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/admin/competitions/{competitionId}/challenges',
      path: { competitionId },
    }), tt('errors.loadCompetitionChallenges'))
  },
  async bindCompetitionChallenge<T = unknown>(competitionId: string, body: unknown) {
    return unwrap(await client.post<{ 201: T }, unknown, false>({
      url: '/api/admin/competitions/{competitionId}/challenges',
      path: { competitionId },
      body,
    }), tt('errors.bindChallenge'))
  },
  async updateCompetitionChallenge<T = unknown>(competitionId: string, challengeId: string, body: unknown) {
    return unwrap(await client.put<{ 200: T }, unknown, false>({
      url: '/api/admin/competitions/{competitionId}/challenges/{challengeId}',
      path: { competitionId, challengeId },
      body,
    }), tt('errors.updateCompetitionChallenge'))
  },
  async deleteCompetitionChallenge(competitionId: string, challengeId: string) {
    await requireSuccess(client.delete({
      url: '/api/admin/competitions/{competitionId}/challenges/{challengeId}',
      path: { competitionId, challengeId },
    }), tt('errors.requestFailed'))
  },
  async restartCompetitionChallengeContainer<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(await client.post<{ 200: T }, unknown, false>({
      url: '/api/admin/competitions/{competitionId}/challenges/{challengeId}/container/restart',
      path: { competitionId, challengeId },
    }), tt('errors.restartChallengeContainer'))
  },
  async competitionTeams<T = unknown[]>(competitionId: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/admin/competitions/{competitionId}/teams',
      path: { competitionId },
    }), tt('errors.loadCompetitionTeams'))
  },
  async approveCompetitionTeam<T = unknown>(competitionId: string, teamId: string) {
    return unwrap(await client.post<{ 200: T }, unknown, false>({
      url: '/api/admin/competitions/{competitionId}/teams/{teamId}/approve',
      path: { competitionId, teamId },
    }), tt('errors.approveTeam'))
  },
  async rejectCompetitionTeam<T = unknown>(competitionId: string, teamId: string) {
    return unwrap(await client.post<{ 200: T }, unknown, false>({
      url: '/api/admin/competitions/{competitionId}/teams/{teamId}/reject',
      path: { competitionId, teamId },
    }), tt('errors.rejectTeam'))
  },
  async setCompetitionTeamLock<T = unknown>(competitionId: string, teamId: string, isLocked: boolean) {
    return unwrap(await client.put<{ 200: T }, unknown, false>({
      url: '/api/admin/competitions/{competitionId}/teams/{teamId}/lock',
      path: { competitionId, teamId },
      body: { isLocked },
    }), tt('errors.updateTeamLock'))
  },
  async banCompetitionTeam<T = unknown>(competitionId: string, teamId: string, reason?: string) {
    return unwrap(await client.post<{ 200: T }, unknown, false>({
      url: '/api/admin/competitions/{competitionId}/teams/{teamId}/ban',
      path: { competitionId, teamId },
      body: { reason },
    }), tt('errors.banTeam'))
  },
  async unbanCompetitionTeam<T = unknown>(competitionId: string, teamId: string) {
    return unwrap(await client.post<{ 200: T }, unknown, false>({
      url: '/api/admin/competitions/{competitionId}/teams/{teamId}/unban',
      path: { competitionId, teamId },
    }), tt('errors.unbanTeam'))
  },
  async competitionLogs<T = unknown[]>(competitionId: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/admin/competitions/{competitionId}/logs',
      path: { competitionId },
    }), tt('errors.loadCompetitionLogs'))
  },
  async competitionCheatIncidents<T = unknown[]>(competitionId: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/admin/competitions/{competitionId}/cheat-incidents',
      path: { competitionId },
    }), tt('errors.loadCheatIncidents'))
  },
  async collaborators<T = unknown[]>(competitionId: string) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({
      url: '/api/admin/competitions/{competitionId}/collaborators',
      path: { competitionId },
    }), tt('errors.loadCollaborators'))
  },
  async addCollaborator(competitionId: string, body: unknown) {
    await requireSuccess(client.post({
      url: '/api/admin/competitions/{competitionId}/collaborators',
      path: { competitionId },
      body,
    }), tt('errors.requestFailed'))
  },
  async removeCollaborator(competitionId: string, userId: string) {
    await requireSuccess(client.delete({
      url: '/api/admin/competitions/{competitionId}/collaborators/{userId}',
      path: { competitionId, userId },
    }), tt('errors.requestFailed'))
  },
  async containers<T = unknown[]>() {
    return unwrap(await client.get<{ 200: T }, unknown, false>({ url: '/api/admin/containers' }), tt('errors.loadContainers'))
  },
  async destroyContainer(containerId: string) {
    await requireSuccess(client.delete({ url: '/api/admin/containers/{containerId}', path: { containerId } }), tt('errors.requestFailed'))
  },
  async plugins<T = unknown[]>() {
    return unwrap(await client.get<{ 200: T }, unknown, false>({ url: '/api/admin/plugins' }), tt('errors.loadPlugins'))
  },
  async logs<T = unknown[]>() {
    return unwrap(await client.get<{ 200: T }, unknown, false>({ url: '/api/admin/logs' }), tt('errors.loadLogs'))
  },
  async auditLogs<T = unknown>(query: Record<string, unknown>) {
    return unwrap(await client.get<{ 200: T }, unknown, false>({ url: '/api/admin/audit-logs', query }), tt('errors.loadAuditLogs'))
  },
  async health<T = unknown>() {
    return unwrap(await sdk.getApiHealth(), tt('errors.loadHealth')) as T
  },
  async infrastructure() {
    return unwrap(await sdk.noCtfapiEndpointsAdminGetInfrastructureEndpoint(), tt('errors.loadHealth'))
  },
  async emailVerificationSettings() {
    return unwrap(await sdk.noCtfapiEndpointsAdminGetEmailVerificationSettingsEndpoint(), tt('errors.requestFailed'))
  },
  async updateEmailVerificationSettings(
    body: Parameters<typeof sdk.noCtfapiEndpointsAdminUpdateEmailVerificationSettingsEndpoint>[0]['body'],
  ) {
    return unwrap(await sdk.noCtfapiEndpointsAdminUpdateEmailVerificationSettingsEndpoint({ body }), tt('errors.requestFailed'))
  },
  async testEmailVerificationSettings() {
    return unwrap(await sdk.noCtfapiEndpointsAdminTestEmailVerificationSettingsEndpoint(), tt('errors.requestFailed'))
  },
  async rebuildScoreboard(competitionId: string) {
    return unwrap(await sdk.noCtfapiEndpointsAdminRebuildScoreboardEndpoint({
      path: { id: competitionId },
    }), tt('errors.requestFailed'))
  },
  async qqBotOverview() {
    return unwrap(await sdk.noCtfapiEndpointsAdminGetQqBotOverviewEndpoint(), tt('errors.requestFailed'))
  },
  async updateQqBotSettings(
    body: Parameters<typeof sdk.noCtfapiEndpointsAdminUpdateQqBotSettingsEndpoint>[0]['body'],
  ) {
    return unwrap(await sdk.noCtfapiEndpointsAdminUpdateQqBotSettingsEndpoint({ body }), tt('errors.requestFailed'))
  },
  async upsertQqBotAgent(
    body: Parameters<typeof sdk.noCtfapiEndpointsAdminUpsertQqBotAgentEndpoint>[0]['body'],
  ) {
    return unwrap(await sdk.noCtfapiEndpointsAdminUpsertQqBotAgentEndpoint({ body }), tt('errors.requestFailed'))
  },
  async authorizeQqBotGroup(
    groupId: string,
    body: Parameters<typeof sdk.noCtfapiEndpointsAdminAuthorizeQqBotGroupEndpoint>[0]['body'],
  ) {
    return unwrap(await sdk.noCtfapiEndpointsAdminAuthorizeQqBotGroupEndpoint({
      path: { groupId },
      body,
    }), tt('errors.requestFailed'))
  },
  async availableQqBotGroups(competitionId: string) {
    return unwrap(await sdk.noCtfapiEndpointsAdminGetAvailableQqBotGroupsEndpoint({
      path: { competitionId },
    }), tt('errors.requestFailed'))
  },
  async competitionQqBot(competitionId: string) {
    return unwrap(await sdk.noCtfapiEndpointsAdminGetCompetitionQqBotConfigEndpoint({
      path: { competitionId },
    }), tt('errors.requestFailed'))
  },
  async updateCompetitionQqBot(
    competitionId: string,
    body: Parameters<typeof sdk.noCtfapiEndpointsAdminUpdateCompetitionQqBotConfigEndpoint>[0]['body'],
  ) {
    return unwrap(await sdk.noCtfapiEndpointsAdminUpdateCompetitionQqBotConfigEndpoint({
      path: { competitionId },
      body,
    }), tt('errors.requestFailed'))
  },
  async competitionQqBotTemplates(competitionId: string) {
    return unwrap(await sdk.noCtfapiEndpointsAdminGetCompetitionQqBotTemplatesEndpoint({
      path: { competitionId },
    }), tt('errors.requestFailed'))
  },
  async upsertCompetitionQqBotTemplate(
    competitionId: string,
    body: Parameters<typeof sdk.noCtfapiEndpointsAdminUpsertCompetitionQqBotTemplateEndpoint>[0]['body'],
  ) {
    return unwrap(await sdk.noCtfapiEndpointsAdminUpsertCompetitionQqBotTemplateEndpoint({
      path: { competitionId },
      body,
    }), tt('errors.requestFailed'))
  },
  async previewQqBot(
    competitionId: string,
    body: Parameters<typeof sdk.noCtfapiEndpointsAdminPreviewCompetitionQqBotMessageEndpoint>[0]['body'],
  ) {
    return unwrap(await sdk.noCtfapiEndpointsAdminPreviewCompetitionQqBotMessageEndpoint({
      path: { competitionId },
      body,
    }), tt('errors.requestFailed'))
  },
  async sendQqBotNotification(
    competitionId: string,
    body: Parameters<typeof sdk.noCtfapiEndpointsAdminSendCompetitionQqBotNotificationEndpoint>[0]['body'],
  ) {
    return unwrap(await sdk.noCtfapiEndpointsAdminSendCompetitionQqBotNotificationEndpoint({
      path: { competitionId },
      body,
    }), tt('errors.requestFailed'))
  },
  async qqBotLogs(
    competitionId: string,
    query: Parameters<typeof sdk.noCtfapiEndpointsAdminGetCompetitionQqBotLogsEndpoint>[0]['query'],
  ) {
    return unwrap(await sdk.noCtfapiEndpointsAdminGetCompetitionQqBotLogsEndpoint({
      path: { competitionId },
      query,
    }), tt('errors.requestFailed'))
  },
  async retryQqBotDelivery(competitionId: string, deliveryId: string) {
    return unwrap(await sdk.noCtfapiEndpointsAdminRetryCompetitionQqBotDeliveryEndpoint({
      path: { competitionId, deliveryId },
    }), tt('errors.requestFailed'))
  },
}
