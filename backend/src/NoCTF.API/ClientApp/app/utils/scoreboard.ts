import type {
  NoCtfapiEndpointsCompetitionsScoreboardBreakdownKindProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardBreakdownResponse,
  NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogItemResponse,
  NoCtfapiEndpointsCompetitionsScoreboardColumnResponse,
  NoCtfapiEndpointsCompetitionsScoreboardEntryKindProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardEntryOutcomeProtocol,
  NoCtfapiEndpointsCompetitionsGameModeProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardRankingStateProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSlotResponse,
  NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
} from '~/api'
import { translate } from './i18n'

export interface ScoreboardChallengeColumnGroup {
  competitionChallengeId: string
  challenge: NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogItemResponse | null
  columns: NoCtfapiEndpointsCompetitionsScoreboardColumnResponse[]
}

export interface ScoreboardSlotSignals {
  showFlag: boolean
  flagAttempted: boolean
  flagSucceeded: boolean
  showShield: boolean
  shieldAttempted: boolean
  shieldSucceeded: boolean
}

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

function scoreboardActivity(
  slot: NoCtfapiEndpointsCompetitionsScoreboardSlotResponse | null | undefined,
  kinds: NoCtfapiEndpointsCompetitionsScoreboardBreakdownKindProtocol[],
): { attempted: boolean; succeeded: boolean } {
  const items = slot?.breakdown?.filter(item => item.kind && kinds.includes(item.kind)) ?? []
  return {
    attempted: items.some(item => (item.attemptCount ?? 0) > 0),
    succeeded: items.some(item => (item.successfulCount ?? 0) > 0),
  }
}

export function scoreboardSlotSignals(
  slot: NoCtfapiEndpointsCompetitionsScoreboardSlotResponse | null | undefined,
  mode: NoCtfapiEndpointsCompetitionsGameModeProtocol | null | undefined,
): ScoreboardSlotSignals {
  const flag = mode === 'Ctf'
    ? scoreboardActivity(slot, ['Solve'])
    : mode === 'Koh'
      ? scoreboardActivity(slot, ['Control'])
      : scoreboardActivity(slot, ['Attack'])
  const shield = mode === 'Awd'
    ? scoreboardActivity(slot, ['Defense', 'Availability'])
    : scoreboardActivity(slot, ['Defense'])

  return {
    showFlag: mode !== undefined && mode !== null,
    flagAttempted: flag.attempted,
    flagSucceeded: flag.succeeded,
    showShield: mode === 'Awd' || mode === 'Awdp',
    shieldAttempted: shield.attempted,
    shieldSucceeded: shield.succeeded,
  }
}

export function scoreboardColumnsForChallenge(
  schema: NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse | null | undefined,
  competitionChallengeId: string,
): NoCtfapiEndpointsCompetitionsScoreboardColumnResponse[] {
  return (schema?.columns ?? []).filter(column =>
    column.competitionChallengeId === competitionChallengeId,
  )
}

export function scoreboardChallengeColumnGroups(
  schema: NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse | null | undefined,
  catalog: NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogItemResponse[] | null | undefined,
): ScoreboardChallengeColumnGroup[] {
  const catalogById = new Map<string, NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogItemResponse>()
  for (const challenge of catalog ?? []) {
    if (challenge.id) catalogById.set(challenge.id, challenge)
  }

  const groups = new Map<string, ScoreboardChallengeColumnGroup>()
  for (const column of schema?.columns ?? []) {
    const competitionChallengeId = column.competitionChallengeId
    if (!competitionChallengeId) continue
    const existing = groups.get(competitionChallengeId)
    if (existing) {
      existing.columns.push(column)
      continue
    }
    groups.set(competitionChallengeId, {
      competitionChallengeId,
      challenge: catalogById.get(competitionChallengeId) ?? null,
      columns: [column],
    })
  }

  return [...groups.values()]
}

export function scoreboardTeamChallengeScore(
  team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
  group: ScoreboardChallengeColumnGroup,
): number {
  const aggregate = (team.challengeScores ?? []).find(
    item => item.competitionChallengeId === group.competitionChallengeId,
  )
  if (aggregate) return (aggregate.attackScore ?? 0) + (aggregate.defenseScore ?? 0)

  return group.columns.reduce((total, column) => {
    if (column.index === undefined) return total
    const slot = scoreboardSlot(team, column.index)
    return slot?.scoreState === 'Settled' ? total + (slot.netPoints ?? 0) : total
  }, 0)
}

export function scoreboardTeamChallengeSignals(
  team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
  group: ScoreboardChallengeColumnGroup,
  mode: NoCtfapiEndpointsCompetitionsGameModeProtocol | null | undefined,
): ScoreboardSlotSignals {
  const signals = group.columns
    .filter(column => column.index !== undefined)
    .map(column => scoreboardSlotSignals(scoreboardSlot(team, column.index!), mode))
  return {
    showFlag: signals.some(signal => signal.showFlag),
    flagAttempted: signals.some(signal => signal.flagAttempted),
    flagSucceeded: signals.some(signal => signal.flagSucceeded),
    showShield: signals.some(signal => signal.showShield),
    shieldAttempted: signals.some(signal => signal.shieldAttempted),
    shieldSucceeded: signals.some(signal => signal.shieldSucceeded),
  }
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
