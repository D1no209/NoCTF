import { computed, markRaw, toRefs } from 'vue'
import type { DefinitionModel } from '../../utils/game-config'
import RunnerJobEditorComponent from './RunnerJobEditor.vue'
export function useDefinitionCheckerSection(props: Readonly<{ model: DefinitionModel; disabled: boolean }>) {
  const serviceNames = computed(() => props.model.runtime?.definition.kind === 'container'
    ? props.model.runtime.definition.services.map(service => service.name) : [])
  function setReadyTimeout(value: number | null) { if (!props.disabled) props.model.readyTimeoutSeconds = value }
  return { ...toRefs(props), serviceNames, setReadyTimeout, RunnerJobEditor: markRaw(RunnerJobEditorComponent) }
}
export type DefinitionCheckerSectionViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useDefinitionCheckerSection>>
