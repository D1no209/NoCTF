import { markRaw, toRefs } from 'vue'

import type { DefinitionModel, GameModeValue } from '../../utils/game-config'
import { emptyFlagTemplate, FlagSource } from '../../utils/game-config'
import FlagTemplateEditorComponent from './FlagTemplateEditor.vue'

/** Owns state, effects and commands for DefinitionFlagTemplateSection. */
export function useDefinitionFlagTemplateSection(props: Readonly<Omit<{
  model: DefinitionModel
  mode: GameModeValue
  disabled?: boolean
}, "disabled"> & Required<Pick<{
  model: DefinitionModel
  mode: GameModeValue
  disabled?: boolean
}, "disabled">>>) {
  function toggleFlagTemplate(enabled: boolean): void {
    props.model.flagTemplate = enabled ? emptyFlagTemplate() : null
  }

  const FlagTemplateEditor = markRaw(FlagTemplateEditorComponent)

  return {
      ...toRefs(props),
      FlagSource,
      toggleFlagTemplate,
      FlagTemplateEditor
    }
}

export type DefinitionFlagTemplateSectionViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useDefinitionFlagTemplateSection>>>
