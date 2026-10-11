import type { NoCtfapiEndpointsLiveSoloLiveSoloMatchResponse as Match, NoCtfapiEndpointsLiveSoloLiveSoloRoundResponse as Round } from '~/api'
export const endReasonKeys = {
  Unrecorded: 'liveSolo.result.reason.Unrecorded', CorrectAnswer: 'liveSolo.result.reason.CorrectAnswer', RequiredWinsReached: 'liveSolo.result.reason.RequiredWinsReached',
  Forfeit: 'liveSolo.result.reason.Forfeit', TimeLimit: 'liveSolo.result.reason.TimeLimit', Voided: 'liveSolo.result.reason.Voided', EligibilityChanged: 'liveSolo.result.reason.EligibilityChanged',
  SelectionChanged: 'liveSolo.result.reason.SelectionChanged', ResultCorrected: 'liveSolo.result.reason.ResultCorrected', DownstreamReplaced: 'liveSolo.result.reason.DownstreamReplaced', Bye: 'liveSolo.result.reason.Bye',
} as const
export function resultPresentation(match: Match, round: Round | null, kind: 'Match' | 'Round') {
  const result = kind === 'Match' ? match : round
  if (!result || (result.resultRevision ?? 0) < 1) return null
  const winner = result.winnerTeamId
  const winnerName = winner === match.leftTeamId ? match.leftTeamName : winner === match.rightTeamId ? match.rightTeamName : null
  const winnerAvatar = winner === match.leftTeamId ? match.leftTeamId : winner === match.rightTeamId ? match.rightTeamId : null
  const left = result.resultLeftWins, right = result.resultRightWins
  return { kind, winnerName, winnerAvatar, roundNumber: round?.number, revision: result.resultRevision,
    score: left != null && right != null ? `${left} : ${right}` : null, reasonKey: endReasonKeys[result.endReason ?? 'Unrecorded'],
    corrected: result.endReason === 'ResultCorrected' || result.endReason === 'DownstreamReplaced',
    endedAt: kind === 'Match' ? match.resultUpdatedAt : round?.endedAt }
}
