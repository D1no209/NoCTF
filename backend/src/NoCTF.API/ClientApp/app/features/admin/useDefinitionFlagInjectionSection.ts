import { toRefs } from 'vue'

import type { DefinitionModel } from '../../utils/game-config'

/** Owns state, effects and commands for DefinitionFlagInjectionSection. */
export function useDefinitionFlagInjectionSection(props: Readonly<Omit<{
  model: DefinitionModel
  disabled?: boolean
}, "disabled"> & Required<Pick<{
  model: DefinitionModel
  disabled?: boolean
}, "disabled">>>) {
  const hasServices = computed(() => props.model.runtime?.definition.kind === 'container')

  function toggleFlagInjection(enabled: boolean): void {
    props.model.flagInjection = enabled ? { command: '', timeoutSeconds: null, serviceName: '' } : null
  }

  const viewBindings = {
      ...toRefs(props),
      hasServices,
      serviceNames: computed(() => props.model.runtime?.definition.kind === 'container' ? props.model.runtime.definition.services.map(service => service.name) : []),
      toggleFlagInjection
    }
  function onUpdateModelValueTimeoutSeconds(value: number | null) {
    if (props.model.flagInjection) props.model.flagInjection.timeoutSeconds = value
  }

  return { ...viewBindings, onUpdateModelValueTimeoutSeconds }
}

export type DefinitionFlagInjectionSectionViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useDefinitionFlagInjectionSection>>>
