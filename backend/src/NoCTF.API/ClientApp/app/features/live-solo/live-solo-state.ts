import type { NoCtfapiEndpointsLiveSoloLiveSoloMatchResponse as Match, NoCtfapiEndpointsLiveSoloLiveSoloRoundResponse as Round,
  NoCtfapiEndpointsLiveSoloLiveSoloQuestionResponse as Question } from '~/api'
import type { MessageKey } from '~/locales/en'

export function matchStateKey(state: Match['state']): MessageKey {
  switch (state) {
    case 'AwaitingOpponents': return 'liveSolo.state.awaitingOpponents'
    case 'Preparing': return 'liveSolo.state.preparing'
    case 'Countdown': return 'liveSolo.state.countdown'
    case 'Running': return 'liveSolo.state.running'
    case 'Paused': return 'liveSolo.state.paused'
    case 'AwaitingAdjudication': return 'liveSolo.state.awaitingAdjudication'
    case 'Completed': return 'liveSolo.state.completed'
    case 'Canceled': return 'liveSolo.state.canceled'
    default: return 'liveSolo.state.unavailable'
  }
}

export function canJudgeLiveSolo(role: string | null | undefined): boolean {
  return ['Administrator', 'Owner', 'Manager', 'Judge'].includes(role ?? '')
}

export function canPlayLiveSolo(match: Match | null, round: Round | null): boolean {
  return match?.state === 'Running' && round?.state === 'Running' && !round.paused
}

/** A refresh may add a question but must not steal a member's selection. */
export function retainQuestionSelection(selected: string | null, questions: readonly Question[]): string | null {
  return questions.some(x => x.id === selected) ? selected : questions.find(x => x.id)?.id ?? null
}

export function questionFromWorkspaceRoute(segments: string | string[] | undefined, roundId: string | undefined,
  selected: string | null, questions: readonly Question[]): string | null {
  const values = Array.isArray(segments) ? segments : segments ? [segments] : []
  if (!values.length) return retainQuestionSelection(selected, questions)
  return values.length === 4 && values[0] === 'rounds' && values[1] === roundId && values[2] === 'questions'
    ? questions.find(x => x.id === values[3])?.id ?? null : null
}

/** Elapsed time is an estimate between REST snapshots, never an admission decision. */
export function roundRemainingSeconds(round: Round | null, sinceSnapshotMs: number): number | null {
  if (!round?.startedAt) return null
  const extra = round.state === 'Running' && !round.paused ? Math.max(0, sinceSnapshotMs) : 0
  return Math.max(0, Math.ceil(((round.limitSeconds ?? 0) * 1000 - (round.activeElapsedMilliseconds ?? 0) - extra) / 1000))
}

export function formatRoundClock(seconds: number | null): string {
  if (seconds == null) return '—'
  return `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}`
}
