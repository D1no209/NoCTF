import { computed, markRaw, toRefs } from 'vue'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract } from '../../api'
import type { DefinitionModel } from '../../utils/game-config'
import { defaultDefinition, setChallengeCapability } from '../../utils/game-config'
import { capabilityEditorOptions, type CapabilityEditorSection } from './capability-editor'
import DefinitionCheckerSectionComponent from './DefinitionCheckerSection.vue'
import DefinitionFlagInjectionSectionComponent from './DefinitionFlagInjectionSection.vue'
import DefinitionFlagTemplateSectionComponent from './DefinitionFlagTemplateSection.vue'
import DefinitionPatchSectionComponent from './DefinitionPatchSection.vue'
import DefinitionRuntimeSectionComponent from './DefinitionRuntimeSection.vue'
import QuestionnaireCapabilityEditorComponent from './QuestionnaireCapabilityEditor.vue'
import ControlCapabilityEditorComponent from './ControlCapabilityEditor.vue'

export function useDefinitionEditor(
  props: Readonly<{ modelValue?: NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract; draftModel?: DefinitionModel | null; section: CapabilityEditorSection; disabled: boolean; includeRuntime: boolean; challengeId?: string; savedQuestionIds?: string[] }>,
  emit: { (event: 'update:modelValue', definition: NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract): void },
) {
  const owned = props.draftModel === undefined
    ? useDefinitionModel(() => props.modelValue ?? defaultDefinition(), definition => emit('update:modelValue', definition)) : null
  const model = computed(() => props.draftModel !== undefined ? props.draftModel : owned?.model.value ?? null)
  const parseFailed = computed(() => owned?.parseFailed.value ?? false)
  const options = capabilityEditorOptions
  const visible = (section: CapabilityEditorSection) => props.section === 'all' || props.section === section
  function setCapability(kind: DefinitionModel['capabilities'][number], enabled: boolean) {
    if (model.value && !props.disabled) setChallengeCapability(model.value, kind, enabled)
  }
  return {
    ...toRefs(props), model, parseFailed, options, setCapability, visible,
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
