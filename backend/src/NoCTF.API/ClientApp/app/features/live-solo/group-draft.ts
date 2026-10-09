import type { NoCtfapiEndpointsLiveSoloLiveSoloQuestionGroupResponse as Group, NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateSummaryResponse as Template } from '../../api'
export interface GroupDraft { id?: string; expectedStamp?: string; name: string; reserve: boolean; limitSeconds: number | null;
  questions: { competitionChallengeId: string; openOffsetSeconds: number | null }[] }
export function groupDraft(group?: Group): GroupDraft {
  return { id: group?.id, expectedStamp: group?.concurrencyStamp, name: group?.name ?? '', reserve: group?.reserve ?? false,
    limitSeconds: group?.limitSeconds ?? null, questions: (group?.questions ?? []).map(row => ({ competitionChallengeId: row.competitionChallengeId ?? '', openOffsetSeconds: row.openOffsetSeconds ?? null })) }
}
export function groupOffsets(draft: GroupDraft, interval: number) { return draft.questions.map((row,index) => row.openOffsetSeconds ?? index * interval) }
export function validGroup(draft: GroupDraft, interval: number, limit: number): boolean {
  const offsets = groupOffsets(draft, interval), effectiveLimit = draft.limitSeconds ?? limit
  return !!draft.name.trim() && draft.name.trim().length <= 160 && draft.questions.length > 0 && draft.questions.length <= 64
    && new Set(draft.questions.map(row => row.competitionChallengeId)).size === draft.questions.length
    && draft.questions.every(row => !!row.competitionChallengeId) && Number.isInteger(effectiveLimit) && effectiveLimit >= 1 && effectiveLimit <= 86400
    && offsets.every((value,index) => Number.isInteger(value) && value >= 0 && value < effectiveLimit && (index === 0 ? value === 0 : value > offsets[index-1]!))
}
export function copyCandidates(rows: Template[]) { return rows.filter(row => row.id && !row.deletedAt && row.mode === 'Ctf' && row.interactionKind === 'FlagSubmission') }
export function moveGroupQuestion(draft: GroupDraft, index: number, direction: -1 | 1) {
  const target = index + direction; if (target < 0 || target >= draft.questions.length) return draft.questions
  const questions = [...draft.questions]; [questions[index], questions[target]] = [questions[target]!, questions[index]!]; return questions
}
