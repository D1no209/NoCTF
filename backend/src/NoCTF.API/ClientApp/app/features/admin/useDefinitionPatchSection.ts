import { proxyRefs } from 'vue'
import { toRefs } from 'vue'

import type { DefinitionModel } from '../../utils/game-config'
import { bytesToMib, HARD_MAXIMUM_PATCH_UPLOAD_BYTES, mibToBytes } from '../../utils/game-config'

/** Owns state, effects and commands for DefinitionPatchSection. */
export function useDefinitionPatchSection(props: Readonly<Omit<{
  model: DefinitionModel
  disabled?: boolean
}, "disabled"> & Required<Pick<{
  model: DefinitionModel
  disabled?: boolean
}, "disabled">>>) {

  const viewBindings = {
      ...toRefs(props),
      bytesToMib,
      HARD_MAXIMUM_PATCH_UPLOAD_BYTES,
      mibToBytes
    }
  const viewState = proxyRefs(viewBindings)

  function onUpdateModelValueModelPatchCommand(value: NonNullable<typeof viewState.model>['patchCommand']) {
    viewState.model!.patchCommand = value
  }

  function onUpdateModelValueModelPatchTimeoutSeconds(value: NonNullable<typeof viewState.model>['patchTimeoutSeconds']) {
    viewState.model!.patchTimeoutSeconds = value
  }

  function onUpdateModelValueModelReadyTimeoutSeconds(value: NonNullable<typeof viewState.model>['readyTimeoutSeconds']) {
    viewState.model!.readyTimeoutSeconds = value
  }

  function onUpdateModelValueModelMaximumPatchUploadBytes(value: Parameters<typeof mibToBytes>[0]) {
    viewState.model!.maximumPatchUploadBytes = mibToBytes(value)
  }

  return { ...viewBindings, onUpdateModelValueModelPatchCommand, onUpdateModelValueModelPatchTimeoutSeconds, onUpdateModelValueModelReadyTimeoutSeconds, onUpdateModelValueModelMaximumPatchUploadBytes }
}

export type DefinitionPatchSectionViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useDefinitionPatchSection>>>
