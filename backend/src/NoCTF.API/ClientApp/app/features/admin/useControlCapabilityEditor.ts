import { computed, markRaw, toRefs } from 'vue'
import type { DefinitionModel } from '../../utils/game-config'
import RunnerJobEditorComponent from './RunnerJobEditor.vue'
export function useControlCapabilityEditor(props: Readonly<{ model: DefinitionModel; disabled: boolean }>) {
  const serviceNames = computed(() => props.model.runtime?.definition.kind === 'container'
    ? props.model.runtime.definition.services.map(service => service.name) : [])
  return { ...toRefs(props), serviceNames, RunnerJobEditor: markRaw(RunnerJobEditorComponent) }
}
export type ControlCapabilityEditorViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useControlCapabilityEditor>>
