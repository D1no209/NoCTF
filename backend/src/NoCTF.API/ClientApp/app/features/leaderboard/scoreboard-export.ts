import type { ScoreboardColumn, ScoreboardMode, ScoreboardTeam } from '../../utils/scoreboard-board'
import { scoreboardSlot } from '../../utils/scoreboard'

export function buildScoreboardCsv(
  header: readonly string[],
  teams: readonly ScoreboardTeam[],
  columns: readonly ScoreboardColumn[],
  mode: ScoreboardMode | null,
  teamCells: (team: ScoreboardTeam) => readonly (string | number)[],
): string {
  const usesCurrentScore = mode === 'Ctf' || mode === 'Koh'
  const rows = teams.map(team => [
    ...teamCells(team),
    ...columns.map(column => {
      const slot = scoreboardSlot(team, column.index)
      const hasScore = slot?.scoreState === 'Settled'
        || (usesCurrentScore && slot?.scoreState === 'Provisional')
      return hasScore ? slot?.netPoints ?? 0 : ''
    }),
  ])
  const escape = (value: string | number) => `"${String(value).replaceAll('"', '""')}"`
  return '\uFEFF' + [header, ...rows].map(row => row.map(escape).join(',')).join('\r\n')
}
