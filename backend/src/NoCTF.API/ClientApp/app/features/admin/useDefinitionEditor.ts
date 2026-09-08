import { markRaw, toRefs } from 'vue'

import type { GameModeValue } from '../../utils/game-config'
import DefinitionCheckerSectionComponent from './DefinitionCheckerSection.vue'
import DefinitionFlagInjectionSectionComponent from './DefinitionFlagInjectionSection.vue'
import DefinitionPatchSectionComponent from './DefinitionPatchSection.vue'
import DefinitionRuntimeSectionComponent from './DefinitionRuntimeSection.vue'

type Events = { 'update:modelValue': [json: string] }

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

  return {
      ...toRefs(props),
      model,
      parseFailed,
      DefinitionCheckerSection,
      DefinitionFlagInjectionSection,
      DefinitionPatchSection,
      DefinitionRuntimeSection
    }
}

export type DefinitionEditorViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useDefinitionEditor>>>
