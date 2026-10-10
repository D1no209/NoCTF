import { date, id, model, type Data } from './schema'
import type { createFixtures } from './data/fixtures'
import { defaultDefinition } from '../app/utils/game-config'

// Fictional local fixtures. Media connections and production operations are never simulated.
export function createMockLiveSolo(state: ReturnType<typeof createFixtures>) {
  const competitionId = id(2, 8)
  state.competitions.push({ ...state.competitions[0], id: competitionId, mode: 'LiveSolo',
    title: 'LiveSolo 对抗演练 · MOCK', tracksEnabled: false, posterUrl: null })
  const challenges = state.challenges.filter(row => row.competitionId === id(2)).slice(0, 4).map((row, index) => {
    const challengeId = id(3, 800 + index)
    state.templates.push({ ...state.templates[index], id: challengeId, mode: 'LiveSolo', definition: defaultDefinition('LiveSolo') })
    return { ...row, id: id(4, 800 + index), competitionId, challengeId,
      title: index ? row.title : 'LiveSolo — 长题名与窄屏操作区域验证 / Long challenge title for layout checks' }
  })
  state.challenges.push(...challenges)
  const teams = state.teams.filter(row => row.competitionId === id(2)).map((row, index) => ({ ...row,
    id: id(5, 800 + index), competitionId, name: index ? row.name : 'Long Team Name / 长队名排版验证' }))
  state.teams.push(...teams)
  const configuration = model('LiveSoloLiveSoloConfigurationContract', { enabled: true, bracketFormat: 'SingleElimination',
    requiredWins: 2, countdownSeconds: 5, questionIntervalSeconds: 180, roundLimitSeconds: 900,
    publicDelaySeconds: 60, participantsMayViewOpponents: false, recordingEnabled: true,
    recordingRetentionDays: 30, maximumConcurrentMatches: 4, maximumRosterMembers: 2, maximumViewers: 50,
    stageRules: [{ lane: 'GrandFinal', stage: 1, requiredWins: 3 }] })
  const groups = [0, 1, 2].map(index => model('LiveSoloLiveSoloQuestionGroupResponse', { id: id(30, index + 1),
    name: ['首轮题组 / Opening round', '决赛题组 / Final', '备用题组 / Reserve'][index], reserve: index === 2,
    questions: challenges.slice(0, index + 1).map((row, position) => ({ competitionChallengeId: row.id, openOffsetSeconds: position * 180 })) }))
  const matches = [0, 1, 2].map(index => {
    const pair = index === 1 ? [teams[0], teams[3]] : teams.slice(index * 2, index * 2 + 2)
    return model('LiveSoloLiveSoloMatchResponse', { id: id(31, index + 1), competitionId,
      state: ['Preparing', 'Running', 'Completed'][index], requiredWins: 2, leftWins: index === 2 ? 2 : 0, rightWins: index ? 1 : 0,
      leftTeamId: pair[0]?.id, leftTeamName: pair[0]?.name,
      rightTeamId: pair[1]?.id, rightTeamName: pair[1]?.name,
      currentRoundId: index === 1 ? id(32) : null,
      rosters: pair.map(team => ({ teamId: team.id, userIds: team.memberIds.slice(0, 2), locked: true, ready: true })) })
  })
  const round = model('LiveSoloLiveSoloRoundResponse', { id: id(32), matchId: id(31, 2), number: 1, state: 'Running',
    startedAt: date(-0.1), limitSeconds: 900, activeElapsedMilliseconds: 360000 })
  const recordings = [0, 1].map(index => model('LiveSoloLiveSoloRecordingResponse', { id: id(33, index + 1),
    userName: state.users[index]?.userName, teamName: teams[index]?.name, state: 'Finalizing', createdAt: date(-1), keepUntil: date(720) }))

  function read(route: string, params: Record<string, string>) {
    if (params.competitionId !== competitionId || !route.includes('/live-solo/')) return undefined
    const suffix = route.split('/live-solo/')[1]
    const match = matches.find(row => row.id === params.matchId)
    if (suffix === 'configuration' || suffix === 'player-policy') return configuration
    if (suffix === 'question-groups') return { items: groups }
    if (suffix === 'bracket') return model('LiveSoloLiveSoloBracketResponse', { competitionId, format: 'SingleElimination', matches: [] })
    if (suffix === 'matches') return { items: matches }
    if (suffix === 'matches/{matchId}') return match
    if (suffix === 'matches/{matchId}/rounds/{roundId}') return round
    if (suffix === 'matches/{matchId}/media') return model('LiveSoloLiveSoloMediaResponse', { matchId: params.matchId,
      state: 'Preparing', recordingEnabled: true, publicDelaySeconds: 60,
      members: match?.rosters.flatMap((roster: Data, index: number) => roster.userIds.map((userId: string) =>
        model('LiveSoloLiveSoloMediaMemberResponse', { userId, teamId: roster.teamId, side: index ? 'Right' : 'Left',
          userName: state.users.find(user => user.userId === userId)?.userName ?? 'Mock Member' }))) ?? [] })
    if (suffix === 'matches/{matchId}/rounds/{roundId}/questions') return { items: challenges.map((row, position) =>
      model('LiveSoloLiveSoloQuestionResponse', { ...row, id: id(34, position + 1), competitionChallengeId: row.id, position })) }
    if (suffix === 'matches/{matchId}/recordings') return { items: recordings, total: recordings.length, canJudge: true, canPublish: false }
    if (suffix?.endsWith('/corrections') || suffix?.endsWith('/media/program/decisions')) return []
    if (suffix?.endsWith('/recordings/{recordingId}/decisions')) return { items: [] }
    return undefined
  }
  return { competitionId, groups, configuration, read }
}
