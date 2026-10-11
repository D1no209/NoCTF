/** Staff tools and participant play have separate, stable workspaces. */
export const liveSoloManagementPath = (competitionId: string) => `/admin/competitions/${encodeURIComponent(competitionId)}/live-solo`
export const liveSoloPlayerPath = (competitionId: string) => `/competitions/${encodeURIComponent(competitionId)}/live-solo`
export const isLiveSoloManagementPath = (path: string) => /^\/admin\/competitions\/[^/]+\/live-solo(?:\/|$)/.test(path)
export const liveSoloBasePath = (competitionId: string, path: string) => isLiveSoloManagementPath(path)
  ? liveSoloManagementPath(competitionId) : liveSoloPlayerPath(competitionId)
export const liveSoloMatchPath = (competitionId: string, matchId: string, path: string) =>
  `${liveSoloBasePath(competitionId, path)}/matches/${encodeURIComponent(matchId)}`
export const liveSoloMatchListLocation = (competitionId: string, path: string, matchId: string) =>
  ({ path: liveSoloBasePath(competitionId, path), query: { match: matchId } })
