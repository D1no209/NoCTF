import type {
  NoCtfapiEndpointsCompetitionsScoreboardBreakdownKindProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardBreakdownResponse,
  NoCtfapiEndpointsCompetitionsScoreboardColumnResponse,
  NoCtfapiEndpointsCompetitionsScoreboardEntryKindProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardEntryOutcomeProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardRankingStateProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSlotResponse,
  NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
} from '~/api'
import { translate } from './i18n'

const rankingStateLabels = {
  Eligible: '符合排名资格',
  Banned: '已封禁',
  Disqualified: '已取消资格',
} satisfies Record<NoCtfapiEndpointsCompetitionsScoreboardRankingStateProtocol, string>

const entryKindLabels = {
  Solve: '解题',
  Attack: '攻击',
  Defense: '防御',
  Availability: '可用性',
  Control: '控制',
  Penalty: '处罚',
  BloodAward: '血榜奖励',
  Hint: '提示',
  ManualAdjustment: '人工调分',
} satisfies Record<NoCtfapiEndpointsCompetitionsScoreboardEntryKindProtocol, string>

const entryOutcomeLabels = {
  Pending: '待处理',
  Succeeded: '成功',
  Failed: '失败',
  Rejected: '已拒绝',
} satisfies Record<NoCtfapiEndpointsCompetitionsScoreboardEntryOutcomeProtocol, string>

export function scoreboardRankingStateLabel(
  state: NoCtfapiEndpointsCompetitionsScoreboardRankingStateProtocol | null | undefined,
): string {
  if (!state) return '—'
  return translate(rankingStateLabels[state])
}

export function scoreboardEntryKindLabel(
  kind: NoCtfapiEndpointsCompetitionsScoreboardEntryKindProtocol | null | undefined,
): string {
  if (!kind) return '—'
  return translate(entryKindLabels[kind])
}

export function scoreboardEntryOutcomeLabel(
  outcome: NoCtfapiEndpointsCompetitionsScoreboardEntryOutcomeProtocol | null | undefined,
): string {
  if (!outcome) return '—'
  return translate(entryOutcomeLabels[outcome])
}

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
