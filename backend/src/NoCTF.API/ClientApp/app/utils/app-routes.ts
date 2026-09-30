function routeSegment(value: string): string {
  return encodeURIComponent(value)
}

export const competitionsPath = '/competitions'
export const notificationsPath = '/notifications'

export function isCompetitionOverviewPath(path: string): boolean {
  return /^\/competitions(?:\/[^/]+)?\/?$/.test(path)
}

export function competitionPageKey(route: { path: string; params: Record<string, string | string[] | undefined> }): string {
  if (isCompetitionOverviewPath(route.path)) return 'competition-browser'
  return typeof route.params.id === 'string' ? route.params.id : route.path
}

export function competitionPath(competitionId: string): string {
  return `${competitionsPath}/${routeSegment(competitionId)}`
}

export function competitionChallengesPath(competitionId: string): string {
  return `${competitionPath(competitionId)}/challenges`
}

export function competitionChallengePath(
  competitionId: string,
  competitionChallengeId: string,
): string {
  return `${competitionChallengesPath(competitionId)}/${routeSegment(competitionChallengeId)}`
}

export function competitionEventsPath(competitionId: string): string {
  return `${competitionPath(competitionId)}/events`
}

export function competitionQuestionsPath(competitionId: string): string {
  return `${competitionPath(competitionId)}/questions`
}

export function competitionMyTeamPath(competitionId: string): string {
  return `${competitionPath(competitionId)}/my/team`
}

export function competitionTeamsPath(competitionId: string): string {
  return `${competitionPath(competitionId)}/teams`
}

export function adminCompetitionPath(competitionId: string): string {
  return `/admin/competitions/${routeSegment(competitionId)}`
}

export function adminCompetitionCheatsPath(competitionId: string): string {
  return `${adminCompetitionPath(competitionId)}/cheats`
}

export function adminCompetitionTeamsPath(competitionId: string): string {
  return `${adminCompetitionPath(competitionId)}/teams`
}
