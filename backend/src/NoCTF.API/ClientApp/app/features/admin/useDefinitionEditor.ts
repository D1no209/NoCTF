import { markRaw, toRefs } from 'vue'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract } from '../../api'
import type { DefinitionModel } from '../../utils/game-config'
import { setChallengeCapability } from '../../utils/game-config'
import DefinitionCheckerSectionComponent from './DefinitionCheckerSection.vue'
import DefinitionFlagInjectionSectionComponent from './DefinitionFlagInjectionSection.vue'
import DefinitionFlagTemplateSectionComponent from './DefinitionFlagTemplateSection.vue'
import DefinitionPatchSectionComponent from './DefinitionPatchSection.vue'
import DefinitionRuntimeSectionComponent from './DefinitionRuntimeSection.vue'
import QuestionnaireCapabilityEditorComponent from './QuestionnaireCapabilityEditor.vue'
import ControlCapabilityEditorComponent from './ControlCapabilityEditor.vue'

export function useDefinitionEditor(
  props: Readonly<{ modelValue: NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract; disabled: boolean; includeRuntime: boolean; challengeId?: string; savedQuestionIds?: string[] }>,
  emit: { (event: 'update:modelValue', definition: NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract): void },
) {
  const { model, parseFailed } = useDefinitionModel(() => props.modelValue, definition => emit('update:modelValue', definition))
  const options = [
    { kind: 'Break', label: 'administration.capability.break' },
    { kind: 'Fix', label: 'administration.capability.fix' },
    { kind: 'Check', label: 'administration.capability.check' },
    { kind: 'Control', label: 'administration.capability.control' },
    { kind: 'Questionnaire', label: 'administration.capability.questionnaire' },
  ] as const
  function setCapability(kind: DefinitionModel['capabilities'][number], enabled: boolean) {
    if (model.value && !props.disabled) setChallengeCapability(model.value, kind, enabled)
  }
  return {
    ...toRefs(props), model, parseFailed, options, setCapability,
    DefinitionRuntimeSection: markRaw(DefinitionRuntimeSectionComponent),
    DefinitionFlagTemplateSection: markRaw(DefinitionFlagTemplateSectionComponent),
    DefinitionFlagInjectionSection: markRaw(DefinitionFlagInjectionSectionComponent),
    DefinitionPatchSection: markRaw(DefinitionPatchSectionComponent),
    DefinitionCheckerSection: markRaw(DefinitionCheckerSectionComponent),
    QuestionnaireCapabilityEditor: markRaw(QuestionnaireCapabilityEditorComponent),
    ControlCapabilityEditor: markRaw(ControlCapabilityEditorComponent),
  }
}
export type DefinitionEditorViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useDefinitionEditor>>
