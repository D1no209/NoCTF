import type { NoCtfapiEndpointsLiveSoloLiveSoloPoolQuestionResponse as Question,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateSummaryResponse as Template } from '../../api'
import { defaultDefinition, definitionContractToModel } from '../../utils/game-config'
import { validateChallengeTemplateDraft } from '../../lib/challenge-template-validation'

export function poolDraft(question?: Question) {
  return { title: question?.title ?? '', direction: question?.direction ?? 'Misc', description: question?.description ?? '',
    removedAttachmentIds: [] as string[],
    flags: question && !question.flags?.some(x => x.specificationKind != null) && question.definition?.runtime?.flagSource !== 'PerTeam'
      ? (question.flags ?? []).map(x => ({ flag: x.flag ?? '', matchKind: x.matchKind ?? 'Exact' })) : null,
    definition: question?.definition ? JSON.parse(JSON.stringify(question.definition)) as NonNullable<Question['definition']> : defaultDefinition('LiveSolo') }
}
export function poolDraftErrors(draft: ReturnType<typeof poolDraft>) {
  return validateChallengeTemplateDraft({ mode: 'LiveSolo', title: draft.title, direction: draft.direction,
    definition: definitionContractToModel(draft.definition, 'LiveSolo') })
}
export function deploymentCandidates(rows: Template[]) { return rows.filter(x => x.id && !x.deletedAt && x.mode === 'LiveSolo') }
