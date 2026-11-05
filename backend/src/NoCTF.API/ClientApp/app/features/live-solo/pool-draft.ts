import type { NoCtfapiEndpointsLiveSoloLiveSoloPoolQuestionResponse as Question,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateSummaryResponse as Template } from '../../api'
import { defaultDefinition, definitionContractToModel } from '../../utils/game-config'
import { validateChallengeTemplateDraft } from '../../lib/challenge-template-validation'
import { supportsCompetitionCapabilities } from '../admin/challenge-competition-placements'

export function poolDraft(question?: Question) {
  return { title: question?.title ?? '', direction: question?.direction ?? 'Misc', description: question?.description ?? '',
    removedAttachmentIds: [] as string[],
    completionKind: question?.completionKind ?? 'Break',
    flags: question && question.canEditContent !== false && question.definition?.capabilities?.some(capability => capability.kind === 'Break')
      && !question.flags?.some(x => x.specificationKind != null)
      ? (question.flags ?? []).map(x => ({ flag: x.flag ?? '', matchKind: x.matchKind ?? 'Exact' })) : null,
    definition: question?.definition ? JSON.parse(JSON.stringify(question.definition)) as NonNullable<Question['definition']> : defaultDefinition() }
}
export function poolDraftErrors(draft: ReturnType<typeof poolDraft>) {
  return validateChallengeTemplateDraft({ title: draft.title, direction: draft.direction,
    definition: definitionContractToModel(draft.definition) })
}
export function deploymentCandidates(rows: Template[]) { return rows.filter(x => x.id && !x.deletedAt && supportsCompetitionCapabilities('LiveSolo', x.capabilities ?? [])) }
