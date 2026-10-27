import type { NoCtfapiEndpointsLiveSoloLiveSoloQuestionGroupResponse as Group } from '../../api'
import { message, type UiMessage } from '../../utils/i18n'
export interface GroupDraft { id?: string; expectedStamp?: string; name: string; reserve: boolean; limitSeconds: number | null;
  questions: { competitionChallengeId: string; openOffsetSeconds: number | null }[] }
export function groupDraft(group?: Group): GroupDraft {
  return { id: group?.id, expectedStamp: group?.concurrencyStamp, name: group?.name ?? '', reserve: group?.reserve ?? false,
    limitSeconds: group?.limitSeconds ?? null, questions: (group?.questions ?? []).map(row => ({ competitionChallengeId: row.competitionChallengeId ?? '', openOffsetSeconds: row.openOffsetSeconds ?? null })) }
}
export function groupOffsets(draft: GroupDraft, interval: number) { return draft.questions.map((row,index) => row.openOffsetSeconds ?? index * interval) }
export interface GroupValidationError { field: 'name' | 'questions' | 'limit' | 'offset'; message: UiMessage; index?: number }
export function groupValidationError(draft: GroupDraft, interval: number, limit: number): GroupValidationError | null {
  const offsets = groupOffsets(draft, interval), effectiveLimit = draft.limitSeconds ?? limit
  if (!draft.name.trim() || draft.name.trim().length > 160) return { field: 'name', message: message('liveSolo.groups.validation.name') }
  if (draft.questions.length === 0 || draft.questions.length > 64 || draft.questions.some(row => !row.competitionChallengeId)
    || new Set(draft.questions.map(row => row.competitionChallengeId)).size !== draft.questions.length)
    return { field: 'questions', message: message('liveSolo.groups.validation.questions') }
  if (!Number.isInteger(effectiveLimit) || effectiveLimit < 1 || effectiveLimit > 86400)
    return { field: 'limit', message: message('liveSolo.groups.validation.limit') }
  for (const [index, offset] of offsets.entries()) {
    const position = index + 1
    if (!Number.isInteger(offset) || offset < 0) return { field: 'offset', index, message: message('liveSolo.groups.validation.offsetValue', { position }) }
    if (index === 0 && offset !== 0) return { field: 'offset', index, message: message('liveSolo.groups.validation.firstOffset') }
    if (offset >= effectiveLimit) return { field: 'offset', index, message: message('liveSolo.groups.validation.offsetLimit', { position, offset, limit: effectiveLimit }) }
    if (index > 0 && offset <= offsets[index - 1]!) return { field: 'offset', index, message: message('liveSolo.groups.validation.offsetOrder', { position, offset, previous: offsets[index - 1]! }) }
  }
  return null
}
export function validGroup(draft: GroupDraft, interval: number, limit: number): boolean { return groupValidationError(draft, interval, limit) === null }
export function moveGroupQuestion(draft: GroupDraft, index: number, direction: -1 | 1) {
  const target = index + direction; if (target < 0 || target >= draft.questions.length) return draft.questions
  const questions = [...draft.questions]; [questions[index], questions[target]] = [questions[target]!, questions[index]!]; return questions
}
