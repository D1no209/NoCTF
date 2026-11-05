import { computed, markRaw, toRefs } from 'vue'

import type { DefinitionModel } from '../../utils/game-config'
import { emptyFlagTemplate } from '../../utils/game-config'
import FlagTemplateEditorComponent from './FlagTemplateEditor.vue'

/** Owns state, effects and commands for DefinitionFlagTemplateSection. */
export function useDefinitionFlagTemplateSection(props: Readonly<Omit<{
  model: DefinitionModel
  disabled?: boolean
}, "disabled"> & Required<Pick<{
  model: DefinitionModel
  disabled?: boolean
}, "disabled">>>) {
  function toggleFlagTemplate(enabled: boolean): void {
    props.model.flagTemplate = enabled ? emptyFlagTemplate() : null
  }

  const FlagTemplateEditor = markRaw(FlagTemplateEditorComponent)
  const services = computed(() => props.model.runtime?.definition.kind === 'container' ? props.model.runtime.definition.services : [])
  function environmentVariable(serviceName: string): string {
    return props.model.flagEnvironmentTargets.find(target => target.serviceName === serviceName)?.variableName ?? ''
  }
  function setEnvironmentVariable(serviceName: string, value: string | number): void {
    if (props.disabled) return
    const variableName = String(value).trim()
    const existing = props.model.flagEnvironmentTargets.find(target => target.serviceName === serviceName)
    if (!variableName) props.model.flagEnvironmentTargets = props.model.flagEnvironmentTargets.filter(target => target.serviceName !== serviceName)
    else if (existing) existing.variableName = variableName
    else props.model.flagEnvironmentTargets.push({ serviceName, variableName })
  }

  return {
      ...toRefs(props),
        toggleFlagTemplate,
      FlagTemplateEditor, services, environmentVariable, setEnvironmentVariable
    }
}

export type DefinitionFlagTemplateSectionViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useDefinitionFlagTemplateSection>>>
