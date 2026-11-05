import { computed, toRefs } from 'vue'
import type { DefinitionModel } from '../../utils/game-config'
export function useControlCapabilityEditor(props: Readonly<{ model: DefinitionModel; disabled: boolean }>) {
  const serviceNames = computed(() => props.model.runtime?.definition.kind === 'container'
    ? props.model.runtime.definition.services.map(service => service.name) : [])
  function setPort(value: number | null) { if (!props.disabled && props.model.control) props.model.control.containerPort = value }
  function setTimeout(value: number | null) { if (!props.disabled && props.model.control) props.model.control.timeoutSeconds = value ?? 10 }
  return { ...toRefs(props), serviceNames, setPort, setTimeout }
}
export type ControlCapabilityEditorViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useControlCapabilityEditor>>
