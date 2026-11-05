import { markRaw, toRefs } from 'vue'

import type { DefinitionModel } from '../../utils/game-config'
import { emptyRuntimeTemplate } from '../../utils/game-config'
import DefinitionRuntimeComponent from './DefinitionRuntime.vue'

/** Owns state, effects and commands for DefinitionRuntimeSection. */
export function useDefinitionRuntimeSection(props: Readonly<Omit<{
  model: DefinitionModel
  disabled?: boolean
}, "disabled"> & Required<Pick<{
  model: DefinitionModel
  disabled?: boolean
}, "disabled">>>) {
  function toggleRuntime(enabled: boolean): void {
    if (!enabled) {
      props.model.runtime = null
      return
    }
    props.model.runtime = emptyRuntimeTemplate()
  }

  const DefinitionRuntime = markRaw(DefinitionRuntimeComponent)

  return {
      ...toRefs(props),
      toggleRuntime,
      DefinitionRuntime
    }
}

export type DefinitionRuntimeSectionViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useDefinitionRuntimeSection>>>
