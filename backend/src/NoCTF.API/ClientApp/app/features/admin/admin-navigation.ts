import type { NoCtfapiEndpointsAdministrationPlatformPlatformAuditLogResponse, NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse } from '~/api'

const segment = (id: string) => encodeURIComponent(id)
export const adminUserPath = (id?: string | null) => id ? `/admin/platform/users/${segment(id)}` : undefined
export const adminCompetitionPath = (id?: string | null) => id ? `/admin/competitions/${segment(id)}` : undefined
export const adminTemplatePath = (id?: string | null) => id ? `/admin/challenges/${segment(id)}` : undefined
export const adminTeamPath = (competitionId?: string | null, teamId?: string | null) => competitionId && teamId
  ? `/admin/competitions/${segment(competitionId)}/teams/${segment(teamId)}` : undefined
export const adminChallengePath = (competitionId?: string | null, challengeId?: string | null) => competitionId && challengeId
  ? `/admin/competitions/${segment(competitionId)}/challenges/${segment(challengeId)}` : undefined
export const adminRuntimePath = (competitionId?: string | null, runtimeId?: string | null) => runtimeId
  ? competitionId ? `/admin/competitions/${segment(competitionId)}/runtimes/${segment(runtimeId)}` : `/admin/platform/runtimes/${segment(runtimeId)}` : undefined
export const adminRuntimeTeamPath = (runtime?: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null) =>
  runtime?.purpose === 'TemplateTest' ? undefined : adminTeamPath(runtime?.competitionId, runtime?.sourceTeamId ?? runtime?.teamId)
export const adminRuntimeChallengePath = (runtime?: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null) =>
  runtime?.challengeId ? adminTemplatePath(runtime.challengeId) : adminChallengePath(runtime?.competitionId, runtime?.competitionChallengeId)

export function adminAuditSubjectPath(log: NoCtfapiEndpointsAdministrationPlatformPlatformAuditLogResponse): string | undefined {
  switch (log.kind) {
    case 'UserAccountLifecycle': return adminUserPath(log.subjectId)
    case 'CompetitionLifecycle':
    case 'CompetitionLeaderboardVisibility': return adminCompetitionPath(log.competitionId ?? log.subjectId)
    default:
      if (log.runtimeInstanceId) return adminRuntimePath(log.competitionId, log.runtimeInstanceId)
      if (log.competitionChallengeId) return adminChallengePath(log.competitionId, log.competitionChallengeId)
      if (log.teamId) return adminTeamPath(log.competitionId, log.teamId)
      if (log.relatedUserId) return adminUserPath(log.relatedUserId)
      return undefined
  }
}

export function adminRouteId(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null
}
export const validAdminId = (id: string) => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id)

/** Opening a detail sheet keeps its list and scroll surfaces mounted. */
export function adminWorkspacePath(path: string): string {
  return path.match(/^(\/admin\/platform\/(?:users|runtimes)|\/admin\/competitions\/[^/]+\/(?:teams|runtimes))(?:\/[^/]+)?$/)?.[1] ?? path
}
