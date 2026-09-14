import { markRaw, toRefs } from 'vue'

import type { GameModeValue } from '../../utils/game-config'
import { applyCtfInteraction, CtfInteraction } from '../../utils/game-config'
import DefinitionCheckerSectionComponent from './DefinitionCheckerSection.vue'
import DefinitionFlagInjectionSectionComponent from './DefinitionFlagInjectionSection.vue'
import DefinitionPatchSectionComponent from './DefinitionPatchSection.vue'
import DefinitionRuntimeSectionComponent from './DefinitionRuntimeSection.vue'

/** Owns state, effects and commands for DefinitionEditor. */
export function useDefinitionEditor(props: Readonly<Omit<{
  /** definitionJson 字符串(v-model)。 */
  modelValue: string
  mode: GameModeValue
  disabled?: boolean
}, "disabled"> & Required<Pick<{
  /** definitionJson 字符串(v-model)。 */
  modelValue: string
  mode: GameModeValue
  disabled?: boolean
}, "disabled">>>,
emit: { (event: "update:modelValue", ...args: [json: string]): void }) {
  const { model, parseFailed } = useDefinitionModel(
    () => props.modelValue,
    () => props.mode,
    json => emit('update:modelValue', json),
  )

  const DefinitionCheckerSection = markRaw(DefinitionCheckerSectionComponent)

  const DefinitionFlagInjectionSection = markRaw(DefinitionFlagInjectionSectionComponent)

  const DefinitionPatchSection = markRaw(DefinitionPatchSectionComponent)

  const DefinitionRuntimeSection = markRaw(DefinitionRuntimeSectionComponent)

  const { configuration } = usePlatform()

  const patchVerificationEnabled = computed(() =>
    configuration.value?.experimentalFeatures?.ctfPatchVerificationEnabled === true,
  )

  const showInteractionKind = computed(() => props.mode === 'Ctf'
    && (patchVerificationEnabled.value
      || model.value?.interactionKind === CtfInteraction.PatchVerification),
  )

  function setInteractionKind(value: unknown): void {
    if (!model.value || props.disabled || typeof value !== 'string') return
    const interactionKind = value === 'PatchVerification'
      ? CtfInteraction.PatchVerification
      : CtfInteraction.FlagSubmission
    if (interactionKind === CtfInteraction.PatchVerification
      && !patchVerificationEnabled.value) return
    applyCtfInteraction(model.value, interactionKind)
  }

  return {
      ...toRefs(props),
      model,
      parseFailed,
      DefinitionCheckerSection,
      DefinitionFlagInjectionSection,
      DefinitionPatchSection,
      DefinitionRuntimeSection,
      CtfInteraction,
      patchVerificationEnabled,
      showInteractionKind,
      setInteractionKind
    }
}

export type DefinitionEditorViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useDefinitionEditor>>>
