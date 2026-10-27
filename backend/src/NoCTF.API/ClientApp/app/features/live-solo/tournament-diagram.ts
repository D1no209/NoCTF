import type { NoCtfapiEndpointsLiveSoloLiveSoloBracketResponse as Bracket,
  NoCtfapiEndpointsLiveSoloLiveSoloBracketMatchResponse as MatchRow } from '../../api'
import type { MessageKey } from '../../locales/en'
import { matchStateKey } from './live-solo-state'

export const tournamentFormatKeys = { SingleElimination: 'liveSolo.settings.single', DoubleElimination: 'liveSolo.settings.double', Swiss: 'liveSolo.tournament.swiss' } as const
export const tournamentLaneKeys = { Winners: 'liveSolo.settings.winners', Losers: 'liveSolo.settings.losers',
  GrandFinal: 'liveSolo.settings.grandFinal', ResetFinal: 'liveSolo.settings.resetFinal', Swiss: 'liveSolo.tournament.swiss' } as const
export type TournamentNode = { id: string; row: MatchRow; x: number; y: number; number: number; stateKey: MessageKey;
  showScore: boolean;
  sides: { key: 'Left' | 'Right'; teamId?: string | null; name?: string | null; avatarUrl?: string | null; initials: string;
    score: number; winner: boolean; source?: string; sourceNumber?: number; seed?: number | null }[] }
export type TournamentHeading = { id: string; x: number; y: number; laneKey: MessageKey; round: number; wins?: number; losses?: number }
type TournamentEdge = { id: string; from: string; to: string; loser: boolean; path: string; teamIds?: string[] }

export function tournamentDiagram(bracket: Bracket, stageNumber: number) {
  const rows = (bracket.matches ?? []).filter(row => row.match?.id && (row.tournamentStage ?? 1) === stageNumber)
  const numbers = new Map((bracket.matches ?? []).map((row, i) => [row.match?.id, i + 1]))
  const headings: TournamentHeading[] = [], nodes: TournamentNode[] = []
  const width = 304, height = 96, column = 352, spacing = 112
  const swiss = rows.some(row => row.lane === 'Swiss')
  function add(row: MatchRow, x: number, y: number) {
    const match = row.match!, sides = (['Left', 'Right'] as const).map(side => {
      const source = row.sources?.find(s => s.side === side), teamId = side === 'Left' ? match.leftTeamId : match.rightTeamId
      const name = side === 'Left' ? match.leftTeamName : match.rightTeamName
      return { key: side, teamId, name, initials: Array.from(name ?? '').slice(0, 2).join(''),
        avatarUrl: side === 'Left' ? match.leftAvatarUrl : match.rightAvatarUrl,
        score: (side === 'Left' ? match.resultLeftWins ?? match.leftWins : match.resultRightWins ?? match.rightWins) ?? 0,
        winner: match.state === 'Completed' && !match.pendingCorrectionId && !!teamId && teamId === match.winnerTeamId, source: source?.source,
        sourceNumber: source?.sourceMatchId ? numbers.get(source.sourceMatchId) : undefined, seed: source?.seed }
    })
    const stateKey = match.endReason === 'Forfeit' ? 'liveSolo.tournament.forfeit'
      : match.endReason === 'Bye' && !row.conditional ? 'liveSolo.bracket.bye'
        : row.conditional && match.state === 'Canceled' ? 'liveSolo.tournament.resetNotNeeded' : matchStateKey(match.state)
    nodes.push({ id: match.id!, row, x, y, number: numbers.get(match.id) ?? 0, stateKey, sides,
      showScore: match.endReason !== 'Bye' && !!match.leftTeamId && !!match.rightTeamId })
  }
  if (swiss) {
    const rounds = [...new Set(rows.map(row => row.stage ?? 1))].sort((a, b) => a - b)
    for (const [index, round] of rounds.entries()) {
      const grouped = new Map<string, MatchRow[]>()
      for (const row of rows.filter(row => row.stage === round)) {
        const key = `${row.swissWins ?? 0}:${row.swissLosses ?? 0}`
        grouped.set(key, [...(grouped.get(key) ?? []), row])
      }
      let y = 0
      for (const group of [...grouped.values()].sort((a, b) => (b[0]?.swissWins ?? 0) - (a[0]?.swissWins ?? 0))) {
        headings.push({ id: `${round}:${y}`, x: index * column, y, laneKey: tournamentLaneKeys.Swiss,
          round, wins: group[0]?.swissWins ?? 0, losses: group[0]?.swissLosses ?? 0 })
        y += 46
        for (const row of group) { add(row, index * column, y); y += spacing }
        y += 20
      }
    }
  } else {
    let baseY = 0, lastColumn = 0
    for (const lane of ['Winners', 'Losers'] as const) {
      const laneRows = rows.filter(row => row.lane === lane), rounds = [...new Set(laneRows.map(row => row.stage ?? 1))].sort((a, b) => a - b)
      const maxCount = Math.max(1, ...rounds.map(round => laneRows.filter(row => row.stage === round).length))
      for (const [index, round] of rounds.entries()) {
        const matches = laneRows.filter(row => row.stage === round).sort((a, b) => (a.position ?? 0) - (b.position ?? 0))
        headings.push({ id: `${lane}:${round}`, x: index * column, y: baseY, laneKey: tournamentLaneKeys[lane], round })
        for (const [position, row] of matches.entries()) add(row, index * column,
          baseY + 46 + ((position + 0.5) * maxCount / matches.length - 0.5) * spacing)
      }
      lastColumn = Math.max(lastColumn, rounds.length)
      if (laneRows.length) baseY += maxCount * spacing + 100
    }
    for (const [index, lane] of (['GrandFinal', 'ResetFinal'] as const).entries()) {
      const row = rows.find(row => row.lane === lane)
      if (!row) continue
      const x = (lastColumn + index) * column, y = Math.max(46, baseY / 2 - height / 2)
      headings.push({ id: lane, x, y: y - 46, laneKey: tournamentLaneKeys[lane], round: 1 }); add(row, x, y)
    }
  }
  const directory = new Map(nodes.map(node => [node.id, node]))
  function connection(from: TournamentNode, to: TournamentNode) {
    const x1 = from.x + width, y1 = from.y + height / 2, x2 = to.x, y2 = to.y + height / 2
    const bend = Math.max(24, Math.abs(x2 - x1) / 2)
    return `M${x1},${y1} C${x1 + bend},${y1} ${x2 - bend},${y2} ${x2},${y2}`
  }
  const edges: TournamentEdge[] = nodes.flatMap(node => (node.row.sources ?? []).flatMap(source => {
    const from = source.sourceMatchId ? directory.get(source.sourceMatchId) : undefined
    if (!from) return []
    return [{ id: `${from.id}:${node.id}:${source.side}`, from: from.id, to: node.id, loser: source.source === 'Loser',
      path: connection(from, node) }]
  }))
  if (swiss) {
    const previous = new Map<string, TournamentNode>(), routes = new Map<string, TournamentEdge>()
    for (const node of nodes) for (const side of node.sides) {
      if (!side.teamId) continue
      const from = previous.get(side.teamId)
      if (from && from.x < node.x) {
        const id = `${from.id}:${node.id}`, existing = routes.get(id)
        if (existing) existing.teamIds!.push(side.teamId)
        else routes.set(id, { id, from: from.id, to: node.id, loser: false, path: connection(from, node), teamIds: [side.teamId] })
      }
      previous.set(side.teamId, node)
    }
    edges.push(...routes.values())
  }
  return { nodes, edges, headings, width: Math.max(600, ...nodes.map(node => node.x + width)) + 32,
    height: Math.max(260, ...nodes.map(node => node.y + height)) + 32 }
}
