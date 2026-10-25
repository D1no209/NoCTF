import type { NoCtfapiEndpointsLiveSoloLiveSoloBracketResponse as Bracket,
  NoCtfapiEndpointsLiveSoloLiveSoloBracketMatchResponse as MatchRow } from '../../api'
import type { MessageKey } from '../../locales/en'
import { matchStateKey } from './live-solo-state'

export const tournamentFormatKeys = { SingleElimination: 'liveSolo.settings.single', DoubleElimination: 'liveSolo.settings.double', Swiss: 'liveSolo.tournament.swiss' } as const
export const tournamentLaneKeys = { Winners: 'liveSolo.settings.winners', Losers: 'liveSolo.settings.losers',
  GrandFinal: 'liveSolo.settings.grandFinal', ResetFinal: 'liveSolo.settings.resetFinal', Swiss: 'liveSolo.tournament.swiss' } as const
export type TournamentNode = { id: string; row: MatchRow; x: number; y: number; number: number; stateKey: MessageKey;
  sides: { key: 'Left' | 'Right'; teamId?: string | null; name?: string | null; score: number; winner: boolean; source?: string; sourceNumber?: number; seed?: number | null }[] }
export type TournamentHeading = { id: string; x: number; y: number; laneKey: MessageKey; round: number; wins?: number; losses?: number }

export function tournamentDiagram(bracket: Bracket, stageNumber: number) {
  const rows = (bracket.matches ?? []).filter(row => row.match?.id && (row.tournamentStage ?? 1) === stageNumber)
  const numbers = new Map((bracket.matches ?? []).map((row, i) => [row.match?.id, i + 1]))
  const headings: TournamentHeading[] = [], nodes: TournamentNode[] = []
  const width = 248, height = 96, column = 324, spacing = 116
  const swiss = rows.some(row => row.lane === 'Swiss')
  function add(row: MatchRow, x: number, y: number) {
    const match = row.match!, sides = (['Left', 'Right'] as const).map(side => {
      const source = row.sources?.find(s => s.side === side), teamId = side === 'Left' ? match.leftTeamId : match.rightTeamId
      return { key: side, teamId, name: side === 'Left' ? match.leftTeamName : match.rightTeamName,
        score: (side === 'Left' ? match.resultLeftWins ?? match.leftWins : match.resultRightWins ?? match.rightWins) ?? 0,
        winner: !!teamId && teamId === match.winnerTeamId, source: source?.source,
        sourceNumber: source?.sourceMatchId ? numbers.get(source.sourceMatchId) : undefined, seed: source?.seed }
    })
    const stateKey = match.endReason === 'Forfeit' ? 'liveSolo.tournament.forfeit'
      : match.endReason === 'Bye' && !row.conditional ? 'liveSolo.bracket.bye'
        : row.conditional && match.state === 'Canceled' ? 'liveSolo.tournament.resetNotNeeded' : matchStateKey(match.state)
    nodes.push({ id: match.id!, row, x, y, number: numbers.get(match.id) ?? 0, stateKey, sides })
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
  const edges = nodes.flatMap(node => (node.row.sources ?? []).flatMap(source => {
    const from = source.sourceMatchId ? directory.get(source.sourceMatchId) : undefined
    if (!from) return []
    const x1 = from.x + width, y1 = from.y + height / 2, x2 = node.x, y2 = node.y + (source.side === 'Left' ? 48 : 84)
    const bend = Math.max(36, Math.abs(x2 - x1) / 2)
    return [{ id: `${from.id}:${node.id}:${source.side}`, from: from.id, to: node.id, loser: source.source === 'Loser',
      path: `M${x1},${y1} C${x1 + bend},${y1} ${x2 - bend},${y2} ${x2},${y2}` }]
  }))
  return { nodes, edges, headings, width: Math.max(600, ...nodes.map(node => node.x + width)) + 32,
    height: Math.max(260, ...nodes.map(node => node.y + height)) + 32 }
}
