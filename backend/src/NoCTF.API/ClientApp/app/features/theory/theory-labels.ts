import { t } from '~/utils/i18n'
import type { NoCtfDomainTheoryQuestionsTheoryQuestionKind, NoCtfDomainTheoryActivitiesTheoryScoringMode } from '~/api'

export function questionKindLabel(kind?: NoCtfDomainTheoryQuestionsTheoryQuestionKind) {
  return t((['theory.single', 'theory.multiple', 'theory.boolean'] as const)[kind ?? 0])
}
export function scoringLabel(mode?: NoCtfDomainTheoryActivitiesTheoryScoringMode) {
  return t((['theory.highest', 'theory.latest', 'theory.cumulative'] as const)[mode ?? 0])
}
export function activityStateLabel(state?: number) {
  return t((['theory.draft', 'theory.published', 'theory.finished', 'theory.voided'] as const)[state ?? 0] ?? 'theory.draft')
}
export function questionStateLabel(state?: number) {
  return t((['theory.draft', 'theory.available', 'theory.retired'] as const)[state ?? 0] ?? 'theory.draft')
}
export function localDateTime(value: string) {
  const date = new Date(value)
  return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16)
}
