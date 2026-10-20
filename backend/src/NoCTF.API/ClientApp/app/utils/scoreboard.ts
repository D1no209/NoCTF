import type {
  NoCtfapiEndpointsCompetitionsScoreboardBreakdownKindProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardAwardProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogItemResponse,
  NoCtfapiEndpointsCompetitionsScoreboardEntryKindProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardEntryOutcomeProtocol,
  NoCtfapiEndpointsCompetitionsGameModeProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardRankingStateProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardOperationStateProtocol,
  NoCtfApplicationScoringLeaderboardScoreboardChallengeBenefit,
} from '../api'
import type {
  ScoreboardActivity,
  ScoreboardChallengeTotal,
  ScoreboardColumn,
  ScoreboardLayout,
  ScoreboardSlot,
  ScoreboardSnapshot,
  ScoreboardTeam,
} from './scoreboard-board'
import { translate } from './i18n'
import { directionKey, directionLabel } from './directions'

export interface ScoreboardChallengeColumnGroup {
  competitionChallengeId: string
  challenge: NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogItemResponse | null
  columns: ScoreboardColumn[]
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
  team: ScoreboardTeam | null,
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
  Eligible: "common.label.eligible",
  Banned: "common.label.banned",
  Disqualified: "common.label.disqualified",
} satisfies Record<NoCtfapiEndpointsCompetitionsScoreboardRankingStateProtocol, string>

const entryKindLabels = {
  Solve: "common.label.solve",
  Attack: "competitions.label.attack",
  Defense: "competitions.label.defense",
  Availability: "common.label.availability",
  Control: "common.label.control",
  Penalty: "common.label.penalty.scoreboard",
  BloodAward: "common.label.bloodListReward",
  Hint: "administration.label.hint",
  ManualAdjustment: "common.label.manualAdjustment",
  WriteUp: "challengeWriteUp.deductionLabel",
} satisfies Record<NoCtfapiEndpointsCompetitionsScoreboardEntryKindProtocol, string>

const entryOutcomeLabels = {
  Pending: "administration.label.pending",
  Succeeded: "competitions.label.success",
  Failed: "common.error.failed",
  Rejected: "common.label.rejected",
} satisfies Record<NoCtfapiEndpointsCompetitionsScoreboardEntryOutcomeProtocol, string>

export function scoreboardRankingStateLabel(
  state: NoCtfapiEndpointsCompetitionsScoreboardRankingStateProtocol | null | undefined,
): string {
  if (!state) return '—'
  return translate(rankingStateLabels[state])
}

export function scoreboardMemberContributionSlices(
  team: ScoreboardTeam | null | undefined,
): ScoreboardMemberContributionSlice[] {
  const members = (team?.members ?? [])
    .filter(member => member.earnedPoints > 0)
    .map(member => ({
      name: member.displayName || translate("common.label.unknownUser"),
      value: member.earnedPoints,
      userId: member.userId,
    }))
  const attributed = members.reduce((total, contribution) => total + contribution.value, 0)
  const unattributed = Math.max(0, (team?.totalScore ?? 0) - attributed)
  return unattributed > 0
    ? [...members, { name: translate("leaderboard.label.teamSystem"), value: unattributed, userId: '' }]
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

// Normalized teams are immutable, so each one indexes its sparse slots once instead of per cell lookup.
const slotsByTeam = new WeakMap<ScoreboardTeam, Map<number, ScoreboardSlot>>()

export function scoreboardSlot(
  team: ScoreboardTeam,
  columnIndex: number,
): ScoreboardSlot | null {
  let slots = slotsByTeam.get(team)
  if (!slots) {
    slots = new Map(team.slots.map(slot => [slot.columnIndex, slot]))
    slotsByTeam.set(team, slots)
  }
  return slots.get(columnIndex) ?? null
}

export function scoreboardBreakdown(
  slot: ScoreboardSlot | null | undefined,
  kind: NoCtfapiEndpointsCompetitionsScoreboardBreakdownKindProtocol,
): ScoreboardActivity | null {
  return (slot?.activity ?? []).find(item => item.kind === kind) ?? null
}

/**
 * Rebuilds the per-challenge benefit list of the team-standing contract. Challenges without a sent
 * total are CTF/KoH cells whose gross and net equal the slot's net points.
 */
export function scoreboardChallengeBenefits(team: ScoreboardTeam): NoCtfApplicationScoringLeaderboardScoreboardChallengeBenefit[] {
  const totals = new Map(team.challengeTotals.map(total => [total.competitionChallengeId, total]))
  const slotNet = new Map<string, number>()
  for (const slot of team.slots) {
    if (slot.netPoints !== null)
      slotNet.set(slot.competitionChallengeId, (slotNet.get(slot.competitionChallengeId) ?? 0) + slot.netPoints)
  }
  const benefits = team.challengeTotals.map(total => ({
    competitionChallengeId: total.competitionChallengeId,
    grossPoints: total.grossPoints,
    writeUpDeductionPoints: total.writeUp?.deductionPoints ?? 0,
    writeUpDeductionPercent: total.writeUp?.deductionPercent ?? null,
    writeUpUnlockedAt: total.writeUp?.unlockedAt ?? null,
    netPoints: total.netPoints,
  }))
  for (const [competitionChallengeId, netPoints] of slotNet) {
    if (totals.has(competitionChallengeId)) continue
    benefits.push({
      competitionChallengeId,
      grossPoints: Math.max(0, netPoints),
      writeUpDeductionPoints: 0,
      writeUpDeductionPercent: null,
      writeUpUnlockedAt: null,
      netPoints,
    })
  }
  return benefits.sort((left, right) => left.competitionChallengeId.localeCompare(right.competitionChallengeId))
}

/** Challenge total that overrides the visible slots (round-window totals and write-up deductions). */
export function scoreboardChallengeTotal(
  team: ScoreboardTeam | null | undefined,
  competitionChallengeId: string,
): ScoreboardChallengeTotal | null {
  return team?.challengeTotals.find(item => item.competitionChallengeId === competitionChallengeId) ?? null
}

/** Signals only need activity; offense/defense states fall back to it when a slot omits them. */
type ScoreboardSignalSlot = Pick<ScoreboardSlot, 'activity'> & Partial<Pick<ScoreboardSlot, 'offenseState' | 'defenseState'>>

function scoreboardActivity(
  slot: ScoreboardSignalSlot | null | undefined,
  kinds: NoCtfapiEndpointsCompetitionsScoreboardBreakdownKindProtocol[],
): { attempted: boolean; succeeded: boolean } {
  const items = slot?.activity.filter(item => item.kind && kinds.includes(item.kind)) ?? []
  return {
    attempted: items.some(item => (item.attemptCount ?? 0) > 0),
    succeeded: items.some(item => (item.successfulCount ?? 0) > 0),
  }
}

export function scoreboardSlotSignals(
  slot: ScoreboardSignalSlot | null | undefined,
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
  schema: ScoreboardLayout | null | undefined,
  competitionChallengeId: string,
): ScoreboardColumn[] {
  return (schema?.columns ?? []).filter(column =>
    column.competitionChallengeId === competitionChallengeId,
  )
}

export function scoreboardChallengeColumnGroups(
  schema: ScoreboardLayout | null | undefined,
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
  team: ScoreboardTeam,
  group: ScoreboardChallengeColumnGroup,
  mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol | null,
): number {
  const challengeTotal = scoreboardChallengeTotal(team, group.competitionChallengeId)
  if (challengeTotal) return challengeTotal.netPoints

  return group.columns.reduce((total, column) => {
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
    const name = (group.challenge?.directionIcon ? group.challenge.direction : directionLabel(group.challenge?.direction)) || translate("common.label.uncategorized")
    const key = directionKey(name)
    const existing = directions.get(key)
    if (existing) existing.groups.push(group)
    else directions.set(key, { key, name, groups: [group] })
  }
  return [...directions.values()]
}

/** Reorder only the presentation groups; each schema column keeps its authoritative index. */
export function scoreboardChallengeColumnGroupsByDirection(
  schema: ScoreboardLayout | null | undefined,
  catalog: NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogItemResponse[] | null | undefined,
): ScoreboardChallengeColumnGroup[] {
  return scoreboardDirectionGroups(scoreboardChallengeColumnGroups(schema, catalog))
    .flatMap(direction => direction.groups)
}

export function scoreboardTeamDirectionScore(
  team: ScoreboardTeam,
  direction: ScoreboardDirectionGroup,
  mode: NoCtfapiEndpointsCompetitionsGameModeProtocol | null | undefined,
): ScoreboardDirectionScore {
  let attack = 0
  let defense = 0
  let total = 0
  for (const group of direction.groups) {
    if (mode === 'Awdp') {
      const aggregate = scoreboardChallengeTotal(team, group.competitionChallengeId)
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
  snapshot: ScoreboardSnapshot | null | undefined,
  competitionChallengeId: string | null | undefined,
): number | null {
  if (!competitionChallengeId) return null
  return snapshot?.challenges.find(item => item.competitionChallengeId === competitionChallengeId)
    ?.currentScore ?? null
}

export function scoreboardBloodAward(
  slot: Pick<ScoreboardSlot, 'award' | 'awardPoints'> | null | undefined,
): { award: NoCtfapiEndpointsCompetitionsScoreboardAwardProtocol; label: string; points: number } | null {
  if (!slot?.award) return null
  const labels = {
    FirstBlood: "common.label.firstBlood",
    SecondBlood: "common.label.secondBlood",
    ThirdBlood: "common.label.thirdBlood",
  } as const
  return {
    award: slot.award,
    label: translate(labels[slot.award]),
    points: slot.awardPoints,
  }
}

export function scoreboardTeamChallengeSignals(
  team: ScoreboardTeam,
  group: ScoreboardChallengeColumnGroup,
  mode: NoCtfapiEndpointsCompetitionsGameModeProtocol | null | undefined,
): ScoreboardSlotSignals {
  const signals = group.columns
    .map(column => scoreboardSlotSignals(scoreboardSlot(team, column.index), mode))
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
  team: ScoreboardTeam,
  columns: ScoreboardColumn[],
): number | null {
  for (let index = columns.length - 1; index >= 0; index -= 1) {
    const column = columns[index]
    if (!column) continue
    const slot = scoreboardSlot(team, column.index)
    if (slot?.scoreState === 'Settled' && slot.netPoints !== null && slot.netPoints !== undefined)
      return slot.netPoints
  }
  return null
}

export function scoreboardTeamSolveCount(
  team: ScoreboardTeam,
): number {
  return team.slots.reduce((total, slot) => {
    const solve = scoreboardBreakdown(slot, 'Solve')
    return total + (solve?.successfulCount ?? 0)
  }, 0)
}
