import type { NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse } from '../api'
import { scoreboardBreakdown, scoreboardColumnsForChallenge, scoreboardSlot } from './scoreboard'
import type { ScoreboardChallenge, ScoreboardLayout, ScoreboardSnapshot, ScoreboardTeam } from './scoreboard-board'
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
  snapshot: ScoreboardSnapshot | null,
): ScoreboardTeam[] {
  const publicTracks = new Set((snapshot?.tracks ?? [])
    .filter(track => !track.isInternal && track.key)
    .map(track => track.key!))
  return (snapshot?.teams ?? []).filter(team => !team.trackKey || publicTracks.has(team.trackKey))
}

export function controlScreenChallenges(
  catalog: NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse | null,
  schema: ScoreboardLayout | null,
  entries: readonly ScoreboardTeam[],
  currentScores: readonly ScoreboardChallenge[] = [],
): ControlScreenChallenge[] {
  const currentScoreByChallenge = new Map(currentScores
    .filter(score => score.currentScore !== null)
    .map(score => [score.competitionChallengeId, score.currentScore!]))
  const provisional = (catalog?.items ?? []).filter(challenge => challenge.id && challenge.published).map((challenge) => {
    const columns = scoreboardColumnsForChallenge(schema, challenge.id!)
    let solveCount = 0
    let fallbackScore = 0
    for (const team of entries) {
      let solved = false
      for (const column of columns) {
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
  schema: ScoreboardLayout | null,
  entries: readonly ScoreboardTeam[],
): ControlScreenSolve[] {
  const challengeMap = new Map((catalog?.items ?? []).filter(item => item.id).map(item => [item.id!, item]))
  const columnsByChallenge = new Map((schema?.columns ?? []).map(column => [column.competitionChallengeId, column]))
  const solves: ControlScreenSolve[] = []
  for (const team of entries) {
    if (!team.teamId || !team.teamName) continue
    // A team solves a CTF challenge once: its first Solve achievement is the feed item and its cell
    // carries the published score and blood award.
    for (const achievement of team.achievements ?? []) {
      const column = columnsByChallenge.get(achievement.competitionChallengeId)
      if (achievement.kind !== 'Solve' || !column || !achievement.occurredAt) continue
      const slot = scoreboardSlot(team, column.index)
      const challenge = challengeMap.get(achievement.competitionChallengeId)
      const bloodRank: ControlScreenBloodRank | null = slot?.award === 'FirstBlood'
        ? 'First' : slot?.award === 'SecondBlood' ? 'Second' : slot?.award === 'ThirdBlood' ? 'Third' : null
      solves.push({
        key: `${team.teamId}:${achievement.competitionChallengeId}`,
        teamId: team.teamId,
        teamName: team.teamName,
        competitionChallengeId: achievement.competitionChallengeId,
        challengeTitle: challenge?.title ?? '',
        direction: directionLabel(challenge?.direction),
        score: slot?.netPoints ?? 0,
        bloodRank,
        awardPoints: slot?.awardPoints ?? 0,
        solvedAt: achievement.occurredAt,
      })
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
