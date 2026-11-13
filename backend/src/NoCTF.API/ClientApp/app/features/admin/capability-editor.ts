import type { DefinitionModel } from '../../utils/game-config'
import { definitionModelToContract } from '../../utils/game-config'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateContentPatchRequest } from '../../api'

export type CapabilityEditorSection = 'all' | 'basic' | 'flags' | 'fix' | 'check' | 'control' | 'questionnaire'
export const capabilityEditorOptions = [
  { kind: 'Break', label: 'administration.capability.break', section: 'flags', tabLabel: 'administration.capability.flagConfiguration' },
  { kind: 'Fix', label: 'administration.capability.fix', section: 'fix', tabLabel: 'administration.capability.fixConfiguration' },
  { kind: 'Check', label: 'administration.capability.check', section: 'check', tabLabel: 'administration.capability.checkConfiguration' },
  { kind: 'Control', label: 'administration.capability.control', section: 'control', tabLabel: 'administration.capability.controlConfiguration' },
  { kind: 'Questionnaire', label: 'administration.capability.questionnaire', section: 'questionnaire', tabLabel: 'administration.capability.questionnaireConfiguration' },
] as const

export function enabledCapabilitySections(model: DefinitionModel | null) {
  return capabilityEditorOptions.filter(option => model?.capabilities.includes(option.kind)).map(option => ({ ...option,
    configurationRequired: option.kind === 'Control' && !model?.control?.job.image.trim(),
  }))
}

export function challengeTemplateDraftContent(
  metadata: Omit<NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateContentPatchRequest, 'definition'>,
  definition: DefinitionModel,
): NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateContentPatchRequest {
  return { ...metadata, title: metadata.title.trim(), direction: metadata.direction.trim(),
    description: metadata.description?.trim() || null, definition: definitionModelToContract(definition) }
}
