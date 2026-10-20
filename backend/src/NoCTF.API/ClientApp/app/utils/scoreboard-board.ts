import type {
  NoCtfapiEndpointsCompetitionsGameModeProtocol,
  NoCtfapiEndpointsCompetitionsLeaderboardDataScopeProtocol,
  NoCtfapiEndpointsCompetitionsLeaderboardVisibilityProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardActivityResponse,
  NoCtfapiEndpointsCompetitionsScoreboardAwardProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardEntryKindProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardOperationStateProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardRankingStateProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardRoundStateProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardScoreStateProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse,
  NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
  NoCtfapiEndpointsCompetitionsScoreboardTrackResponse,
} from '../api'

/**
 * The leaderboard wire format references challenges, rounds and members by position to stay small.
 * The application works on this resolved model instead: every slot knows its matrix column and ids,
 * every achievement knows its member, and the column layout replaces the former schema resource.
 */
export type ScoreboardMode = NoCtfapiEndpointsCompetitionsGameModeProtocol

export interface ScoreboardRound {
  id: string
  number: number
  startAt: string
  endAt: string
  state: NoCtfapiEndpointsCompetitionsScoreboardRoundStateProtocol
}

/** A matrix column: challenge-major, one per challenge (CTF/KoH) or per challenge × visible round (AWD/AWDP). */
export interface ScoreboardColumn {
  index: number
  competitionChallengeId: string
  roundId: string | null
}

export interface ScoreboardLayout {
  competitionId: string
  mode: ScoreboardMode | null
  rounds: ScoreboardRound[]
  columns: ScoreboardColumn[]
  roundWindowStart: number | null
  roundWindowEnd: number | null
  latestRound: number | null
}

export type ScoreboardActivity = NoCtfapiEndpointsCompetitionsScoreboardActivityResponse

export interface ScoreboardSlot {
  columnIndex: number
  competitionChallengeId: string
  roundId: string | null
  scoreState: NoCtfapiEndpointsCompetitionsScoreboardScoreStateProtocol
  netPoints: number | null
  offenseState: NoCtfapiEndpointsCompetitionsScoreboardOperationStateProtocol
  defenseState: NoCtfapiEndpointsCompetitionsScoreboardOperationStateProtocol
  activity: ScoreboardActivity[]
  award: NoCtfapiEndpointsCompetitionsScoreboardAwardProtocol | null
  awardPoints: number
}

export interface ScoreboardMember {
  userId: string
  displayName: string
  earnedPoints: number
}

export interface ScoreboardAchievement {
  competitionChallengeId: string
  kind: NoCtfapiEndpointsCompetitionsScoreboardEntryKindProtocol
  userId: string | null
  displayName: string | null
  occurredAt: string
}

export interface ScoreboardWriteUp {
  deductionPoints: number
  deductionPercent: number | null
  unlockedAt: string
}

/** Authoritative per-challenge total the visible slots cannot express (round windows, write-ups). */
export interface ScoreboardChallengeTotal {
  competitionChallengeId: string
  grossPoints: number
  netPoints: number
  attackScore: number | null
  defenseScore: number | null
  writeUp: ScoreboardWriteUp | null
}

export interface ScoreboardTeam {
  teamId: string
  teamName: string
  trackKey: string
  rank: number | null
  rankingState: NoCtfapiEndpointsCompetitionsScoreboardRankingStateProtocol
  totalScore: number
  attackScore: number | null
  defenseScore: number | null
  globalAdjustmentCount: number
  theoryScore: number
  hasTheorySubmission: boolean
  members: ScoreboardMember[]
  /** Null identifies snapshots written before achievement attribution was available. */
  achievements: ScoreboardAchievement[] | null
  challengeTotals: ScoreboardChallengeTotal[]
  slots: ScoreboardSlot[]
}

export interface ScoreboardChallenge {
  competitionChallengeId: string
  currentScore: number | null
}

export interface ScoreboardSnapshot {
  competitionId: string
  version: string
  challengeCatalogRevision: string
  generatedAt: string
  currentRoundId: string | null
  theoryEnabled: boolean
  tracksEnabled: boolean
  tracks: NoCtfapiEndpointsCompetitionsScoreboardTrackResponse[]
  visibility: NoCtfapiEndpointsCompetitionsLeaderboardVisibilityProtocol | null
  dataScope: NoCtfapiEndpointsCompetitionsLeaderboardDataScopeProtocol | null
  dataAsOf: string | null
  challenges: ScoreboardChallenge[]
  teams: ScoreboardTeam[]
  layout: ScoreboardLayout
}

/** Column index of a challenge × round cell in the challenge-major layout. */
function columnIndex(challenge: number, round: number | null | undefined, roundCount: number): number {
  return roundCount > 0 && round !== null && round !== undefined ? challenge * roundCount + round : challenge
}

function scoreboardLayout(response: NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse): ScoreboardLayout {
  const mode = response.mode ?? null
  const rounds: ScoreboardRound[] = (response.rounds ?? []).map(round => ({
    id: round.id ?? '',
    number: round.number ?? 0,
    startAt: round.startAt ?? '',
    endAt: round.endAt ?? '',
    state: round.state ?? 'Pending',
  }))
  const perRound = mode === 'Awd' || mode === 'Awdp'
  const columns: ScoreboardColumn[] = []
  for (const [challengeIndex, challenge] of (response.challenges ?? []).entries()) {
    const competitionChallengeId = challenge.competitionChallengeId ?? ''
    if (!perRound) {
      columns.push({ index: challengeIndex, competitionChallengeId, roundId: null })
      continue
    }
    for (const [roundIndex, round] of rounds.entries()) {
      columns.push({ index: columnIndex(challengeIndex, roundIndex, rounds.length), competitionChallengeId, roundId: round.id })
    }
  }
  return {
    competitionId: response.competitionId ?? '',
    mode,
    rounds,
    columns,
    roundWindowStart: response.roundWindowStart ?? null,
    roundWindowEnd: response.roundWindowEnd ?? null,
    latestRound: response.latestRound ?? null,
  }
}

function scoreboardTeam(
  team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
  challengeIds: readonly string[],
  layout: ScoreboardLayout,
): ScoreboardTeam {
  const members: ScoreboardMember[] = (team.members ?? []).map(member => ({
    userId: member.userId ?? '',
    displayName: member.displayName ?? '',
    earnedPoints: member.earnedPoints ?? 0,
  }))
  const challengeId = (index: number | undefined) => index === undefined ? '' : challengeIds[index] ?? ''
  const perRound = layout.mode === 'Awd' || layout.mode === 'Awdp'
  return {
    teamId: team.teamId ?? '',
    teamName: team.teamName ?? '',
    trackKey: team.trackKey ?? '',
    rank: team.rank ?? null,
    rankingState: team.rankingState ?? 'Eligible',
    totalScore: team.totalScore ?? 0,
    attackScore: team.attackScore ?? null,
    defenseScore: team.defenseScore ?? null,
    globalAdjustmentCount: team.globalAdjustmentCount ?? 0,
    theoryScore: team.theoryScore ?? 0,
    hasTheorySubmission: team.hasTheorySubmission ?? false,
    members,
    achievements: team.achievements === null || team.achievements === undefined
      ? null
      : team.achievements.map((achievement) => {
          const member = achievement.member === null || achievement.member === undefined
            ? null
            : members[achievement.member] ?? null
          return {
            competitionChallengeId: challengeId(achievement.challenge),
            kind: achievement.kind ?? 'Solve',
            userId: member?.userId ?? null,
            displayName: member?.displayName ?? null,
            occurredAt: achievement.occurredAt ?? '',
          }
        }),
    challengeTotals: (team.challengeTotals ?? []).map(total => ({
      competitionChallengeId: challengeId(total.challenge),
      grossPoints: total.grossPoints ?? 0,
      netPoints: total.netPoints ?? 0,
      attackScore: total.attackScore ?? null,
      defenseScore: total.defenseScore ?? null,
      writeUp: total.writeUp
        ? {
            deductionPoints: total.writeUp.deductionPoints ?? 0,
            deductionPercent: total.writeUp.deductionPercent ?? null,
            unlockedAt: total.writeUp.unlockedAt ?? '',
          }
        : null,
    })),
    slots: (team.slots ?? []).map((slot) => {
      const round = perRound ? slot.round ?? null : null
      return {
        columnIndex: columnIndex(slot.challenge ?? 0, round, perRound ? layout.rounds.length : 0),
        competitionChallengeId: challengeId(slot.challenge),
        roundId: round === null ? null : layout.rounds[round]?.id ?? null,
        scoreState: slot.scoreState ?? 'Pending',
        netPoints: slot.netPoints ?? null,
        offenseState: slot.offenseState ?? 'None',
        defenseState: slot.defenseState ?? 'None',
        activity: slot.activity ?? [],
        award: slot.award ?? null,
        awardPoints: slot.awardPoints ?? 0,
      }
    }),
  }
}

/** Resolves the compact leaderboard response once, when it is accepted. */
export function normalizeScoreboardSnapshot(
  response: NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse,
): ScoreboardSnapshot {
  const layout = scoreboardLayout(response)
  const challenges = (response.challenges ?? []).map(challenge => ({
    competitionChallengeId: challenge.competitionChallengeId ?? '',
    currentScore: challenge.currentScore ?? null,
  }))
  const challengeIds = challenges.map(challenge => challenge.competitionChallengeId)
  return {
    competitionId: response.competitionId ?? '',
    version: response.version ?? '',
    challengeCatalogRevision: response.challengeCatalogRevision ?? '',
    generatedAt: response.generatedAt ?? '',
    currentRoundId: response.currentRoundId ?? null,
    theoryEnabled: response.theoryEnabled ?? false,
    tracksEnabled: response.tracksEnabled ?? true,
    tracks: response.tracks ?? [],
    visibility: response.visibility ?? null,
    dataScope: response.dataScope ?? null,
    dataAsOf: response.dataAsOf ?? null,
    challenges,
    teams: (response.teams ?? []).map(team => scoreboardTeam(team, challengeIds, layout)),
    layout,
  }
}
