import { markRaw, toRefs } from 'vue'

import type { DefinitionModel, GameModeValue } from '../../utils/game-config'
import { applyCtfInteraction, CtfInteraction, emptyRuntimeTemplate } from '../../utils/game-config'
import DefinitionRuntimeComponent from './DefinitionRuntime.vue'

/** Owns state, effects and commands for DefinitionRuntimeSection. */
export function useDefinitionRuntimeSection(props: Readonly<Omit<{
  model: DefinitionModel
  mode: GameModeValue
  disabled?: boolean
}, "disabled"> & Required<Pick<{
  model: DefinitionModel
  mode: GameModeValue
  disabled?: boolean
}, "disabled">>>) {
  function toggleRuntime(enabled: boolean): void {
    if (!enabled) {
      props.model.runtime = null
      return
    }
    props.model.runtime = emptyRuntimeTemplate(props.mode)
    if (props.mode === 'Ctf'
      && props.model.interactionKind === CtfInteraction.PatchVerification) {
      applyCtfInteraction(props.model, CtfInteraction.PatchVerification)
    }
  }

  const DefinitionRuntime = markRaw(DefinitionRuntimeComponent)

  return {
      ...toRefs(props),
      toggleRuntime,
      DefinitionRuntime
    }
}

export type DefinitionRuntimeSectionViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useDefinitionRuntimeSection>>>
