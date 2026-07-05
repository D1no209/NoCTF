import { client } from './generated/client.gen'
import {
  noCtfapiEndpointsAuthLoginEndpoint,
  noCtfapiEndpointsAuthRefreshTokenEndpoint,
  noCtfapiEndpointsAuthRegisterEndpoint,
  noCtfapiEndpointsCompetitionsCreateCompetitionEndpoint,
  noCtfapiEndpointsCompetitionsGetLeaderboardEndpoint,
  noCtfapiEndpointsTeamsUpdateTeamEndpoint,
} from './generated/sdk.gen'

export class ApiError extends Error {
  readonly status?: number
  readonly details?: unknown

  constructor(message: string, status?: number, details?: unknown) {
    super(message)
    this.status = status
    this.details = details
  }
}

interface ApiResult<T> {
  data?: T
  error?: unknown
  response?: Response
}

function unwrap<T>(result: ApiResult<T>, fallback = 'Request failed'): T {
  if (result.error) {
    const status = result.response?.status
    const details = isEmptyObject(result.error) && status ? `HTTP ${status}` : result.error
    throw new ApiError(fallback, status, details)
  }
  return result.data as T
}

function isEmptyObject(value: unknown) {
  return Boolean(
    value && typeof value === 'object' && !Array.isArray(value) && Object.keys(value).length === 0,
  )
}

export function apiUrl(path: string) {
  const baseUrl = import.meta.env.VITE_API_BASE_URL ?? ''
  if (!baseUrl || /^https?:\/\//i.test(path)) return path
  return new URL(path, baseUrl).toString()
}

export function setAuthToken(token: string | null) {
  client.setConfig({
    headers: { Authorization: token ? `Bearer ${token}` : null },
  })
}

export const authApi = {
  async login(email: string, password: string) {
    return unwrap(
      await noCtfapiEndpointsAuthLoginEndpoint({ body: { email, password } }),
      'Login failed',
    )
  },
  async register(userName: string, email: string, password: string) {
    return unwrap(
      await noCtfapiEndpointsAuthRegisterEndpoint({ body: { userName, email, password } }),
      'Registration failed',
    )
  },
  async refresh() {
    return unwrap(await noCtfapiEndpointsAuthRefreshTokenEndpoint(), 'Refresh token failed')
  },
}

export const competitionApi = {
  async list<T = unknown[]>() {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({ url: '/api/competitions' }),
      'Failed to load competitions',
    )
  },
  async get<T = unknown>(id: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}',
        path: { id },
      }),
      'Failed to load competition',
    )
  },
  async create(
    body: Parameters<typeof noCtfapiEndpointsCompetitionsCreateCompetitionEndpoint>[0]['body'],
  ) {
    return unwrap(
      await noCtfapiEndpointsCompetitionsCreateCompetitionEndpoint({ body }),
      'Failed to create competition',
    )
  },
  async challenges<T = unknown[]>(competitionId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/challenges',
        path: { id: competitionId },
      }),
      'Failed to load challenges',
    )
  },
  async submissions<T = unknown>(competitionId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/submissions',
        path: { id: competitionId },
      }),
      'Failed to load submissions',
    )
  },
  async leaderboard(competitionId: string) {
    return unwrap(
      await noCtfapiEndpointsCompetitionsGetLeaderboardEndpoint({
        path: { competitionId },
      }),
      'Failed to load leaderboard',
    )
  },
  async leaderboardTrend<T = unknown>(competitionId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/competitions/{competitionId}/leaderboard/trend',
        path: { competitionId },
      }),
      'Failed to load leaderboard trend',
    )
  },
  async leaderboardTeam<T = unknown>(competitionId: string, teamId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/competitions/{competitionId}/leaderboard/teams/{teamId}',
        path: { competitionId, teamId },
      }),
      'Failed to load leaderboard team detail',
    )
  },
  async submitFlag<T = unknown>(competitionId: string, challengeId: string, flag: string) {
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/challenges/{challengeId}/submit',
        path: { id: competitionId, challengeId },
        body: { flag },
      }),
      'Failed to submit flag',
    )
  },
  async penetrationDetail<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/challenges/{challengeId}/penetration',
        path: { id: competitionId, challengeId },
      }),
      'Failed to load penetration challenge',
    )
  },
  async penetrationStart<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/challenges/{challengeId}/penetration/instance/start',
        path: { id: competitionId, challengeId },
        body: {},
      }),
      'Failed to start penetration instance',
    )
  },
  async penetrationStop<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/challenges/{challengeId}/penetration/instance/stop',
        path: { id: competitionId, challengeId },
        body: {},
      }),
      'Failed to stop penetration instance',
    )
  },
  async penetrationReset<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/challenges/{challengeId}/penetration/instance/reset',
        path: { id: competitionId, challengeId },
        body: {},
      }),
      'Failed to reset penetration instance',
    )
  },
  async penetrationDestroy<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(
      await client.delete<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/challenges/{challengeId}/penetration/instance',
        path: { id: competitionId, challengeId },
      }),
      'Failed to destroy penetration instance',
    )
  },
  async submitPenetrationFlag<T = unknown>(
    competitionId: string,
    challengeId: string,
    flag: string,
  ) {
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/challenges/{challengeId}/penetration/flags/submit',
        path: { id: competitionId, challengeId },
        body: { flag },
      }),
      'Failed to submit penetration flag',
    )
  },
  async createInstance<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/challenges/{challengeId}/instance',
        path: { id: competitionId, challengeId },
        body: {},
      }),
      'Failed to create instance',
    )
  },
  async getInstance<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/challenges/{challengeId}/instance',
        path: { id: competitionId, challengeId },
      }),
      'Failed to load instance',
    )
  },
  async destroyInstance<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(
      await client.delete<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/challenges/{challengeId}/instance',
        path: { id: competitionId, challengeId },
      }),
      'Failed to destroy instance',
    )
  },
  async extendInstance<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/challenges/{challengeId}/instance/extend',
        path: { id: competitionId, challengeId },
        body: {},
      }),
      'Failed to extend instance',
    )
  },
  async awdDashboard<T = unknown>(competitionId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/awd-dashboard',
        path: { id: competitionId },
      }),
      'Failed to load AWD dashboard',
    )
  },
  async kohDashboard<T = unknown>(competitionId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/koh-dashboard',
        path: { id: competitionId },
      }),
      'Failed to load KoH dashboard',
    )
  },
  async view<T = unknown>(competitionId: string, viewKey: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/views/{viewKey}',
        path: { id: competitionId, viewKey },
      }),
      'Failed to load competition view',
    )
  },
  async teams<T = unknown[]>(competitionId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/teams',
        path: { id: competitionId },
      }),
      'Failed to load teams',
    )
  },
  async myTeams<T = unknown[]>(competitionId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/teams/mine',
        path: { id: competitionId },
      }),
      'Failed to load my teams',
    )
  },
  async submitPatch<T = unknown>(competitionId: string, challengeId: string, file: File) {
    const body = new FormData()
    body.append('file', file)
    return unwrap(
      await client.post<{ 202: T }, unknown, false>({
        url: '/api/competitions/{id}/challenges/{challengeId}/patch',
        path: { id: competitionId, challengeId },
        body,
      }),
      'Failed to submit patch',
    )
  },
  async patchSubmissions<T = unknown[]>(competitionId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/patch-submissions',
        path: { id: competitionId },
      }),
      'Failed to load patch submissions',
    )
  },
}

export const teamApi = {
  async mine<T = unknown[]>() {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/teams/mine',
      }),
      'Failed to load my teams',
    )
  },
  async create<T = unknown>(body: {
    competitionId: string
    name: string
    avatarUrl?: string
    trackName?: string
  }) {
    return unwrap(
      await client.post<{ 201: T }, unknown, false>({
        url: '/api/teams',
        body,
      }),
      'Failed to create team',
    )
  },
  async join<T = unknown>(teamId: string) {
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/teams/{teamId}/join',
        path: { teamId },
      }),
      'Failed to join team',
    )
  },
  async joinByToken<T = unknown>(token: string) {
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/teams/join-by-token',
        body: { token },
      }),
      'Failed to join team',
    )
  },
  async leave(teamId: string) {
    return unwrap(
      await client.post<{ 200: unknown; 204: unknown }, unknown, false>({
        url: '/api/teams/{teamId}/leave',
        path: { teamId },
      }),
      'Failed to leave team',
    )
  },
  async update(
    id: string,
    body: Parameters<typeof noCtfapiEndpointsTeamsUpdateTeamEndpoint>[0]['body'],
  ) {
    return unwrap(
      await noCtfapiEndpointsTeamsUpdateTeamEndpoint({ path: { id }, body }),
      'Failed to update team',
    )
  },
  async transferCaptain(teamId: string, newCaptainUserId: string) {
    return unwrap(
      await client.post<{ 200: unknown; 204: unknown }, unknown, false>({
        url: '/api/teams/{teamId}/transfer-captain',
        path: { teamId },
        body: { newCaptainUserId },
      }),
      'Failed to transfer captain',
    )
  },
  async removeMember(teamId: string, userId: string) {
    return unwrap(
      await client.delete<{ 200: unknown; 204: unknown }, unknown, false>({
        url: '/api/teams/{teamId}/members/{userId}',
        path: { teamId, userId },
      }),
      'Failed to remove team member',
    )
  },
}

export const adminApi = {
  async competitions<T = unknown[]>() {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({ url: '/api/admin/competitions' }),
      'Failed to load competitions',
    )
  },
  async competition<T = unknown>(id: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/admin/competitions/{id}',
        path: { id },
      }),
      'Failed to load competition',
    )
  },
  async createCompetition<T = unknown>(body: unknown) {
    return unwrap(
      await client.post<{ 201: T }, unknown, false>({ url: '/api/admin/competitions', body }),
      'Failed to create competition',
    )
  },
  async updateCompetition<T = unknown>(id: string, body: unknown) {
    return unwrap(
      await client.put<{ 200: T }, unknown, false>({
        url: '/api/admin/competitions/{id}',
        path: { id },
        body,
      }),
      'Failed to update competition',
    )
  },
  async deleteCompetition(id: string) {
    return unwrap(
      await client.delete<{ 200: unknown; 204: unknown }, unknown, false>({
        url: '/api/admin/competitions/{id}',
        path: { id },
      }),
      'Failed to delete competition',
    )
  },
  async users<T = unknown[]>(query?: { q?: string; limit?: number }) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/admin/users',
        query,
      }),
      'Failed to load users',
    )
  },
  async updateUserRole(id: string, role: string) {
    return unwrap(
      await client.post<{ 200: unknown; 204: unknown }, unknown, false>({
        url: '/api/admin/users/{id}/role',
        path: { id },
        body: { role },
      }),
      'Failed to update user role',
    )
  },
  async resetUserPassword(id: string, newPassword: string) {
    return unwrap(
      await client.post<{ 200: unknown; 204: unknown }, unknown, false>({
        url: '/api/admin/users/{id}/reset-password',
        path: { id },
        body: { newPassword },
      }),
      'Failed to reset user password',
    )
  },
  async teams<T = unknown[]>() {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({ url: '/api/admin/teams' }),
      'Failed to load teams',
    )
  },
  async deleteTeam(id: string) {
    return unwrap(
      await client.delete<{ 200: unknown; 204: unknown }, unknown, false>({
        url: '/api/admin/teams/{id}',
        path: { id },
      }),
      'Failed to delete team',
    )
  },
  async teamMembers<T = unknown[]>(teamId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/admin/teams/{teamId}/members',
        path: { teamId },
      }),
      'Failed to load team members',
    )
  },
  async challenges<T = unknown[]>() {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({ url: '/api/admin/challenges' }),
      'Failed to load challenges',
    )
  },
  async createChallenge<T = unknown>(body: unknown) {
    return unwrap(
      await client.post<{ 201: T }, unknown, false>({ url: '/api/admin/challenges', body }),
      'Failed to create challenge',
    )
  },
  async uploadChallengeAttachment<T = unknown>(id: string, file: File) {
    const body = new FormData()
    body.append('file', file)
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/admin/challenges/{id}/attachment',
        path: { id },
        body,
      }),
      'Failed to upload attachment',
    )
  },
  async uploadChallengePatchTemplate<T = unknown>(id: string, file: File) {
    const body = new FormData()
    body.append('file', file)
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/admin/challenges/{id}/patch-template',
        path: { id },
        body,
      }),
      'Failed to upload patch template',
    )
  },
  async updateChallenge<T = unknown>(id: string, body: unknown) {
    return unwrap(
      await client.put<{ 200: T }, unknown, false>({
        url: '/api/admin/challenges/{id}',
        path: { id },
        body,
      }),
      'Failed to update challenge',
    )
  },
  async penetrationTemplateTopology<T = unknown>(id: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/admin/challenges/{templateId}/penetration-topology',
        path: { templateId: id },
      }),
      'Failed to load penetration topology',
    )
  },
  async updatePenetrationTemplateTopology<T = unknown>(id: string, body: unknown) {
    return unwrap(
      await client.put<{ 200: T }, unknown, false>({
        url: '/api/admin/challenges/{templateId}/penetration-topology',
        path: { templateId: id },
        body,
      }),
      'Failed to update penetration topology',
    )
  },
  async competitionPenetrationTopology<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/admin/competitions/{competitionId}/challenges/{challengeId}/penetration/topology',
        path: { competitionId, challengeId },
      }),
      'Failed to load penetration topology',
    )
  },
  async updateCompetitionPenetrationTopology<T = unknown>(
    competitionId: string,
    challengeId: string,
    body: unknown,
  ) {
    return unwrap(
      await client.put<{ 200: T }, unknown, false>({
        url: '/api/admin/competitions/{competitionId}/challenges/{challengeId}/penetration/topology',
        path: { competitionId, challengeId },
        body,
      }),
      'Failed to update penetration topology',
    )
  },
  async penetrationInstances<T = unknown>(
    competitionId: string,
    query: Record<string, unknown> = {},
  ) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/admin/competitions/{competitionId}/penetration/instances',
        path: { competitionId },
        query,
      }),
      'Failed to load penetration instances',
    )
  },
  async resetPenetrationInstance<T = unknown>(competitionId: string, instanceId: string) {
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/admin/competitions/{competitionId}/penetration/instances/{instanceId}/reset',
        path: { competitionId, instanceId },
        body: {},
      }),
      'Failed to reset penetration instance',
    )
  },
  async destroyPenetrationInstance<T = unknown>(competitionId: string, instanceId: string) {
    return unwrap(
      await client.delete<{ 200: T }, unknown, false>({
        url: '/api/admin/competitions/{competitionId}/penetration/instances/{instanceId}',
        path: { competitionId, instanceId },
      }),
      'Failed to destroy penetration instance',
    )
  },
  async deleteChallenge(id: string) {
    return unwrap(
      await client.delete<{ 200: unknown; 204: unknown }, unknown, false>({
        url: '/api/admin/challenges/{id}',
        path: { id },
      }),
      'Failed to delete challenge',
    )
  },
  async revealChallengeSecret<T = unknown>(id: string) {
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/admin/challenges/{id}/reveal-secret',
        path: { id },
      }),
      'Failed to reveal secret',
    )
  },
  async competitionChallenges<T = unknown[]>(competitionId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/admin/competitions/{competitionId}/challenges',
        path: { competitionId },
      }),
      'Failed to load competition challenges',
    )
  },
  async bindCompetitionChallenge<T = unknown>(competitionId: string, body: unknown) {
    return unwrap(
      await client.post<{ 201: T }, unknown, false>({
        url: '/api/admin/competitions/{competitionId}/challenges',
        path: { competitionId },
        body,
      }),
      'Failed to bind challenge',
    )
  },
  async updateCompetitionChallenge<T = unknown>(
    competitionId: string,
    challengeId: string,
    body: unknown,
  ) {
    return unwrap(
      await client.put<{ 200: T }, unknown, false>({
        url: '/api/admin/competitions/{competitionId}/challenges/{challengeId}',
        path: { competitionId, challengeId },
        body,
      }),
      'Failed to update competition challenge',
    )
  },
  async deleteCompetitionChallenge(competitionId: string, challengeId: string) {
    return unwrap(
      await client.delete<{ 200: unknown; 204: unknown }, unknown, false>({
        url: '/api/admin/competitions/{competitionId}/challenges/{challengeId}',
        path: { competitionId, challengeId },
      }),
      'Failed to delete competition challenge',
    )
  },
  async restartCompetitionChallengeContainer<T = unknown>(
    competitionId: string,
    challengeId: string,
  ) {
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/admin/competitions/{competitionId}/challenges/{challengeId}/container/restart',
        path: { competitionId, challengeId },
      }),
      'Failed to restart challenge container',
    )
  },
  async competitionTeams<T = unknown[]>(competitionId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/admin/competitions/{competitionId}/teams',
        path: { competitionId },
      }),
      'Failed to load competition teams',
    )
  },
  async approveCompetitionTeam<T = unknown>(competitionId: string, teamId: string) {
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/admin/competitions/{competitionId}/teams/{teamId}/approve',
        path: { competitionId, teamId },
      }),
      'Failed to approve team',
    )
  },
  async rejectCompetitionTeam<T = unknown>(competitionId: string, teamId: string) {
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/admin/competitions/{competitionId}/teams/{teamId}/reject',
        path: { competitionId, teamId },
      }),
      'Failed to reject team',
    )
  },
  async setCompetitionTeamLock<T = unknown>(
    competitionId: string,
    teamId: string,
    isLocked: boolean,
  ) {
    return unwrap(
      await client.put<{ 200: T }, unknown, false>({
        url: '/api/admin/competitions/{competitionId}/teams/{teamId}/lock',
        path: { competitionId, teamId },
        body: { isLocked },
      }),
      'Failed to update team lock',
    )
  },
  async banCompetitionTeam<T = unknown>(competitionId: string, teamId: string, reason?: string) {
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/admin/competitions/{competitionId}/teams/{teamId}/ban',
        path: { competitionId, teamId },
        body: { reason },
      }),
      'Failed to ban team',
    )
  },
  async unbanCompetitionTeam<T = unknown>(competitionId: string, teamId: string) {
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/admin/competitions/{competitionId}/teams/{teamId}/unban',
        path: { competitionId, teamId },
      }),
      'Failed to unban team',
    )
  },
  async competitionLogs<T = unknown[]>(competitionId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/admin/competitions/{competitionId}/logs',
        path: { competitionId },
      }),
      'Failed to load competition logs',
    )
  },
  async competitionCheatIncidents<T = unknown[]>(competitionId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/admin/competitions/{competitionId}/cheat-incidents',
        path: { competitionId },
      }),
      'Failed to load cheat incidents',
    )
  },
  async collaborators<T = unknown[]>(competitionId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/competitions/{competitionId}/collaborators',
        path: { competitionId },
      }),
      'Failed to load collaborators',
    )
  },
  async addCollaborator(competitionId: string, body: unknown) {
    return unwrap(
      await client.post<{ 200: unknown; 201: unknown; 204: unknown }, unknown, false>({
        url: '/api/competitions/{competitionId}/collaborators',
        path: { competitionId },
        body,
      }),
      'Failed to add collaborator',
    )
  },
  async removeCollaborator(competitionId: string, userId: string) {
    return unwrap(
      await client.delete<{ 200: unknown; 204: unknown }, unknown, false>({
        url: '/api/competitions/{competitionId}/collaborators/{userId}',
        path: { competitionId, userId },
      }),
      'Failed to remove collaborator',
    )
  },
  async containers<T = unknown[]>() {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({ url: '/api/admin/containers' }),
      'Failed to load containers',
    )
  },
  async destroyContainer(containerId: string) {
    return unwrap(
      await client.delete<{ 200: unknown; 204: unknown }, unknown, false>({
        url: '/api/admin/containers/{containerId}',
        path: { containerId },
      }),
      'Failed to destroy container',
    )
  },
  async plugins<T = unknown[]>() {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({ url: '/api/admin/plugins' }),
      'Failed to load plugins',
    )
  },
  async infrastructure<T = unknown>() {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({ url: '/api/admin/infrastructure' }),
      'Failed to load infrastructure',
    )
  },
  async logs<T = unknown[]>() {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({ url: '/api/admin/logs' }),
      'Failed to load logs',
    )
  },
  async auditLogs<T = unknown>(query: Record<string, unknown>) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({ url: '/api/admin/audit-logs', query }),
      'Failed to load audit logs',
    )
  },
  async health<T = unknown>() {
    const controller = new AbortController()
    const timeout = window.setTimeout(() => controller.abort(), 12000)
    const response = await fetch(apiUrl('/api/health'), { signal: controller.signal })
      .finally(() => window.clearTimeout(timeout))
    const data = await response.json().catch(() => undefined)
    if (!response.ok && !data) {
      throw new ApiError('Failed to load health', response.status)
    }
    return data as T
  },
}
