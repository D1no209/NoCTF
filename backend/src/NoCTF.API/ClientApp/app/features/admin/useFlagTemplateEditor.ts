import { toRefs } from 'vue'

import type { FlagTemplateModel } from '../../utils/game-config'

/** Owns state, effects and commands for FlagTemplateEditor. */
export function useFlagTemplateEditor(props: Readonly<Omit<{
  template: FlagTemplateModel
  disabled?: boolean
}, "disabled"> & Required<Pick<{
  template: FlagTemplateModel
  disabled?: boolean
}, "disabled">>>) {

  return {
      ...toRefs(props),
      
    }
}

export type FlagTemplateEditorViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useFlagTemplateEditor>>>
