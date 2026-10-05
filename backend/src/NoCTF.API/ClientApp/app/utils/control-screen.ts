import type {
  NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse,
  NoCtfapiEndpointsCompetitionsScoreboardCurrentChallengeScoreResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse,
  NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
} from '../api'
import { scoreboardBreakdown, scoreboardColumnsForChallenge, scoreboardSlot } from './scoreboard'
import { directionLabel } from './directions'

export type ControlScreenBloodRank = 'First' | 'Second' | 'Third'

export interface ControlScreenChallenge {
  competitionChallengeId: string
  title: string
  direction: string
  currentScore: number
  solveCount: number
  completionPercent: number
  towerHeightPercent: number
}

export interface ControlScreenSolve {
  key: string
  teamId: string
  teamName: string
  competitionChallengeId: string
  challengeTitle: string
  direction: string
  score: number
  bloodRank: ControlScreenBloodRank | null
  awardPoints: number
  solvedAt: string
}

export interface ControlScreenSolveReconciliation {
  seenKeys: Set<string>
  newSolves: ControlScreenSolve[]
}

export function controlScreenPublicEntries(
  snapshot: NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse | null,
): NoCtfapiEndpointsCompetitionsScoreboardTeamResponse[] {
  const publicTracks = new Set((snapshot?.tracks ?? [])
    .filter(track => !track.isInternal && track.key)
    .map(track => track.key!))
  return (snapshot?.teams ?? []).filter(team => !team.trackKey || publicTracks.has(team.trackKey))
}

export function controlScreenChallenges(
  catalog: NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse | null,
  schema: NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse | null,
  entries: readonly NoCtfapiEndpointsCompetitionsScoreboardTeamResponse[],
  currentScores: readonly NoCtfapiEndpointsCompetitionsScoreboardCurrentChallengeScoreResponse[] = [],
): ControlScreenChallenge[] {
  const currentScoreByChallenge = new Map(currentScores
    .filter(score => score.competitionChallengeId)
    .map(score => [score.competitionChallengeId!, score.score ?? 0]))
  const provisional = (catalog?.items ?? []).filter(challenge => challenge.id && challenge.published).map((challenge) => {
    const columns = scoreboardColumnsForChallenge(schema, challenge.id!)
    let solveCount = 0
    let fallbackScore = 0
    for (const team of entries) {
      let solved = false
      for (const column of columns) {
        if (column.index === undefined) continue
        const slot = scoreboardSlot(team, column.index)
        solved ||= (scoreboardBreakdown(slot, 'Solve')?.successfulCount ?? 0) > 0
        if (slot?.scoreState === 'Settled' || slot?.scoreState === 'Provisional')
          fallbackScore = Math.max(fallbackScore, slot.netPoints ?? 0)
      }
      if (solved) solveCount += 1
    }
    return {
      competitionChallengeId: challenge.id!,
      title: challenge.title ?? '',
      direction: directionLabel(challenge.direction),
      currentScore: currentScoreByChallenge.get(challenge.id!) ?? fallbackScore,
      solveCount,
      completionPercent: entries.length ? Math.round(solveCount / entries.length * 100) : 0,
      towerHeightPercent: 30,
    }
  })
  const maximumScore = Math.max(1, ...provisional.map(challenge => challenge.currentScore))
  return provisional.map(challenge => ({
    ...challenge,
    towerHeightPercent: 30 + Math.round(challenge.currentScore / maximumScore * 60),
  }))
}

export function controlScreenSolveFeed(
  catalog: NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse | null,
  schema: NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse | null,
  entries: readonly NoCtfapiEndpointsCompetitionsScoreboardTeamResponse[],
): ControlScreenSolve[] {
  const challengeMap = new Map((catalog?.items ?? []).filter(item => item.id).map(item => [item.id!, item]))
  const solves: ControlScreenSolve[] = []
  for (const team of entries) {
    if (!team.teamId || !team.teamName) continue
    for (const column of schema?.columns ?? []) {
      if (column.index === undefined || !column.competitionChallengeId) continue
      const slot = scoreboardSlot(team, column.index)
      const challenge = challengeMap.get(column.competitionChallengeId)
      for (const entry of slot?.entries ?? []) {
        if (entry.kind !== 'Solve' || entry.outcome !== 'Succeeded' || !entry.id || !entry.occurredAt) continue
        const bloodRank: ControlScreenBloodRank | null = entry.award === 'FirstBlood'
          ? 'First' : entry.award === 'SecondBlood' ? 'Second' : entry.award === 'ThirdBlood' ? 'Third' : null
        solves.push({
          key: entry.id,
          teamId: team.teamId,
          teamName: team.teamName,
          competitionChallengeId: column.competitionChallengeId,
          challengeTitle: challenge?.title ?? '',
          direction: directionLabel(challenge?.direction),
          score: entry.netPoints ?? 0,
          bloodRank,
          awardPoints: entry.awardPoints ?? 0,
          solvedAt: entry.occurredAt,
        })
      }
    }
  }
  return solves.sort((left, right) => right.solvedAt.localeCompare(left.solvedAt) || right.key.localeCompare(left.key))
}

export function reconcileControlScreenSolves(
  previousKeys: ReadonlySet<string> | null,
  currentSolves: readonly ControlScreenSolve[],
): ControlScreenSolveReconciliation {
  const seenKeys = new Set(previousKeys ?? [])
  const newSolves = previousKeys
    ? currentSolves.filter(solve => !seenKeys.has(solve.key))
        .sort((left, right) => left.solvedAt.localeCompare(right.solvedAt) || left.key.localeCompare(right.key))
    : []
  for (const solve of currentSolves) seenKeys.add(solve.key)
  return { seenKeys, newSolves }
}
