import type {
  NoCtfapiEndpointsCompetitionsScoreboardBreakdownKindProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardBreakdownResponse,
  NoCtfapiEndpointsCompetitionsScoreboardColumnResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSlotResponse,
  NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
} from '~/api'

export function scoreboardSlot(
  team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
  columnIndex: number,
): NoCtfapiEndpointsCompetitionsScoreboardSlotResponse | null {
  return (team.slots ?? []).find(slot => slot.columnIndex === columnIndex) ?? null
}

export function scoreboardBreakdown(
  slot: NoCtfapiEndpointsCompetitionsScoreboardSlotResponse | null | undefined,
  kind: NoCtfapiEndpointsCompetitionsScoreboardBreakdownKindProtocol,
): NoCtfapiEndpointsCompetitionsScoreboardBreakdownResponse | null {
  return (slot?.breakdown ?? []).find(item => item.kind === kind) ?? null
}

export function scoreboardColumnsForChallenge(
  schema: NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse | null | undefined,
  competitionChallengeId: string,
): NoCtfapiEndpointsCompetitionsScoreboardColumnResponse[] {
  return (schema?.columns ?? []).filter(column =>
    column.competitionChallengeId === competitionChallengeId,
  )
}

export function latestSettledScore(
  team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
  columns: NoCtfapiEndpointsCompetitionsScoreboardColumnResponse[],
): number | null {
  for (let index = columns.length - 1; index >= 0; index -= 1) {
    const columnIndex = columns[index]?.index
    if (columnIndex === undefined) continue
    const slot = scoreboardSlot(team, columnIndex)
    if (slot?.scoreState === 'Settled' && slot.netPoints !== null && slot.netPoints !== undefined)
      return slot.netPoints
  }
  return null
}

export function scoreboardTeamSolveCount(
  team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
): number {
  return (team.slots ?? []).reduce((total, slot) => {
    const solve = scoreboardBreakdown(slot, 'Solve')
    return total + (solve?.successfulCount ?? 0)
  }, 0)
}
