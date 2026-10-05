const RECENT_EVENT_DAYS = 30
const DAY_IN_MILLISECONDS = 24 * 60 * 60 * 1000

export type CompetitionEventHistoryRange = {
  from?: string
  to?: string
}

export function competitionEventHistoryRange(
  hasStaffAccess: boolean,
  competitionStartTime?: string,
  now = Date.now(),
): CompetitionEventHistoryRange {
  if (hasStaffAccess)
    return {}

  const competitionStart = competitionStartTime
    ? Date.parse(competitionStartTime)
    : Number.NaN
  const earliestRecentEvent = now - RECENT_EVENT_DAYS * DAY_IN_MILLISECONDS
  const from = Number.isFinite(competitionStart)
    ? Math.min(now, Math.max(competitionStart, earliestRecentEvent))
    : earliestRecentEvent

  return {
    from: new Date(from).toISOString(),
    to: new Date(now).toISOString(),
  }
}
