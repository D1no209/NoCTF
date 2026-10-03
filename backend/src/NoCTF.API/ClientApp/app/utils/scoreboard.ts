import type {
  NoCtfapiEndpointsCompetitionsScoreboardBreakdownKindProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardBreakdownResponse,
  NoCtfapiEndpointsCompetitionsScoreboardAwardProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogItemResponse,
  NoCtfapiEndpointsCompetitionsScoreboardColumnResponse,
  NoCtfapiEndpointsCompetitionsScoreboardEntryKindProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardEntryOutcomeProtocol,
  NoCtfapiEndpointsCompetitionsGameModeProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardRankingStateProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardOperationStateProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSlotResponse,
  NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
} from '../api'
import { translate } from './i18n'
import { directionKey, directionLabel } from './directions'

export interface ScoreboardChallengeColumnGroup {
  competitionChallengeId: string
  challenge: NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogItemResponse | null
  columns: NoCtfapiEndpointsCompetitionsScoreboardColumnResponse[]
}

export interface ScoreboardDirectionGroup {
  key: string
  name: string
  groups: ScoreboardChallengeColumnGroup[]
}

export interface ScoreboardDirectionScore {
  attack: number
  defense: number
  total: number
}

export interface ScoreboardSlotSignals {
  showFlag: boolean
  flagState: NoCtfapiEndpointsCompetitionsScoreboardOperationStateProtocol
  flagAttempted: boolean
  flagSucceeded: boolean
  showShield: boolean
  shieldState: NoCtfapiEndpointsCompetitionsScoreboardOperationStateProtocol
  shieldAttempted: boolean
  shieldSucceeded: boolean
}

/** Completed achievements are independent of score sign, compact entries and round windows. */
export function scoreboardTeamAchievements(
  team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse | null,
  competitionChallengeId: string,
  mode: NoCtfapiEndpointsCompetitionsGameModeProtocol | null | undefined,
) {
  return (team?.achievements ?? []).filter(item => item.competitionChallengeId === competitionChallengeId
    && (mode === 'Ctf' ? item.kind === 'Solve' : item.kind === 'Attack' || item.kind === 'Defense'))
    .sort((a, b) => String(a.occurredAt).localeCompare(String(b.occurredAt)))
}

export interface ScoreboardMemberContributionSlice {
  name: string
  value: number
  userId: string
}

const rankingStateLabels = {
  Eligible: "ui.eligible",
  Banned: "ui.banned2",
  Disqualified: "ui.disqualified",
} satisfies Record<NoCtfapiEndpointsCompetitionsScoreboardRankingStateProtocol, string>

const entryKindLabels = {
  Solve: "ui.solve2",
  Attack: "ui.attack",
  Defense: "ui.defense",
  Availability: "ui.availability",
  Control: "ui.control",
  Penalty: "ui.penalty2",
  BloodAward: "ui.bloodListReward",
  Hint: "ui.hint",
  ManualAdjustment: "ui.manualAdjustment",
} satisfies Record<NoCtfapiEndpointsCompetitionsScoreboardEntryKindProtocol, string>

const entryOutcomeLabels = {
  Pending: "ui.pending",
  Succeeded: "ui.success",
  Failed: "ui.failed",
  Rejected: "ui.rejected",
} satisfies Record<NoCtfapiEndpointsCompetitionsScoreboardEntryOutcomeProtocol, string>

export function scoreboardRankingStateLabel(
  state: NoCtfapiEndpointsCompetitionsScoreboardRankingStateProtocol | null | undefined,
): string {
  if (!state) return '—'
  return translate(rankingStateLabels[state])
}

export function scoreboardMemberContributionSlices(
  team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse | null | undefined,
): ScoreboardMemberContributionSlice[] {
  const members = (team?.memberContributions ?? [])
    .filter(contribution => (contribution.earnedPoints ?? 0) > 0)
    .map(contribution => ({
      name: contribution.displayName ?? translate("ui.unknownUser"),
      value: contribution.earnedPoints ?? 0,
      userId: contribution.userId ?? '',
    }))
  const attributed = members.reduce((total, contribution) => total + contribution.value, 0)
  const unattributed = Math.max(0, (team?.totalScore ?? 0) - attributed)
  return unattributed > 0
    ? [...members, { name: translate("ui.teamSystem"), value: unattributed, userId: '' }]
    : members
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
  const flagState = slot?.offenseState ?? (flag.succeeded
    ? 'Succeeded'
    : flag.attempted ? 'Failed' : 'None')
  const shieldState = slot?.defenseState ?? (shield.succeeded
    ? 'Succeeded'
    : shield.attempted ? 'Failed' : 'None')

  return {
    showFlag: mode !== undefined && mode !== null,
    flagState,
    flagAttempted: flag.attempted,
    flagSucceeded: flagState === 'Succeeded',
    showShield: mode === 'Awd' || mode === 'Awdp',
    shieldState,
    shieldAttempted: shield.attempted,
    shieldSucceeded: shieldState === 'Succeeded',
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
  mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol | null,
): number {
  if (mode === 'Awdp') {
    const aggregate = (team.challengeScores ?? []).find(
      item => item.competitionChallengeId === group.competitionChallengeId,
    )
    if (aggregate) return (aggregate.attackScore ?? 0) + (aggregate.defenseScore ?? 0)
  }

  return group.columns.reduce((total, column) => {
    if (column.index === undefined) return total
    const slot = scoreboardSlot(team, column.index)
    const countsTowardChallengeScore = mode === 'Ctf' || mode === 'Koh'
      ? slot?.scoreState === 'Provisional' || slot?.scoreState === 'Settled'
      : slot?.scoreState === 'Settled'
    return countsTowardChallengeScore ? total + (slot?.netPoints ?? 0) : total
  }, 0)
}

export function scoreboardDirectionGroups(
  columnGroups: ScoreboardChallengeColumnGroup[],
): ScoreboardDirectionGroup[] {
  const directions = new Map<string, ScoreboardDirectionGroup>()
  for (const group of columnGroups) {
    const name = (group.challenge?.directionIcon ? group.challenge.direction : directionLabel(group.challenge?.direction)) || translate("ui.uncategorized")
    const key = directionKey(name)
    const existing = directions.get(key)
    if (existing) existing.groups.push(group)
    else directions.set(key, { key, name, groups: [group] })
  }
  return [...directions.values()]
}

/** Reorder only the presentation groups; each schema column keeps its authoritative index. */
export function scoreboardChallengeColumnGroupsByDirection(
  schema: NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse | null | undefined,
  catalog: NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogItemResponse[] | null | undefined,
): ScoreboardChallengeColumnGroup[] {
  return scoreboardDirectionGroups(scoreboardChallengeColumnGroups(schema, catalog))
    .flatMap(direction => direction.groups)
}

export function scoreboardTeamDirectionScore(
  team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
  direction: ScoreboardDirectionGroup,
  mode: NoCtfapiEndpointsCompetitionsGameModeProtocol | null | undefined,
): ScoreboardDirectionScore {
  let attack = 0
  let defense = 0
  let total = 0
  for (const group of direction.groups) {
    if (mode === 'Awdp') {
      const aggregate = (team.challengeScores ?? []).find(
        item => item.competitionChallengeId === group.competitionChallengeId,
      )
      attack += Math.max(0, aggregate?.attackScore ?? 0)
      defense += Math.max(0, aggregate?.defenseScore ?? 0)
    }
    else {
      total += Math.max(0, scoreboardTeamChallengeScore(team, group, mode))
    }
  }
  return { attack, defense, total: mode === 'Awdp' ? attack + defense : total }
}

export function scoreboardCurrentChallengeScore(
  snapshot: NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse | null | undefined,
  competitionChallengeId: string | null | undefined,
): number | null {
  if (!competitionChallengeId) return null
  const score = (snapshot?.currentChallengeScores ?? []).find(
    item => item.competitionChallengeId === competitionChallengeId,
  )?.score
  return score ?? null
}

export function scoreboardBloodAward(
  slot: NoCtfapiEndpointsCompetitionsScoreboardSlotResponse | null | undefined,
): { award: NoCtfapiEndpointsCompetitionsScoreboardAwardProtocol; label: string; points: number } | null {
  const entry = slot?.entries?.find(item => item.award)
  if (!entry?.award) return null
  const labels = {
    FirstBlood: "ui.firstBlood",
    SecondBlood: "ui.secondBlood",
    ThirdBlood: "ui.thirdBlood",
  } as const
  return {
    award: entry.award,
    label: translate(labels[entry.award]),
    points: entry.awardPoints ?? 0,
  }
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
    flagState: signals.some(signal => signal.flagState === 'Succeeded')
      ? 'Succeeded'
      : signals.some(signal => signal.flagState === 'Failed') ? 'Failed' : 'None',
    flagAttempted: signals.some(signal => signal.flagAttempted),
    flagSucceeded: signals.some(signal => signal.flagSucceeded),
    showShield: signals.some(signal => signal.showShield),
    shieldState: signals.some(signal => signal.shieldState === 'Succeeded')
      ? 'Succeeded'
      : signals.some(signal => signal.shieldState === 'Failed') ? 'Failed' : 'None',
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
