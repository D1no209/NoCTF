import { computed, markRaw, toRefs } from 'vue'
import type { DefinitionModel } from '../../utils/game-config'
import RunnerJobEditorComponent from './RunnerJobEditor.vue'
export function useDefinitionCheckerSection(props: Readonly<{ model: DefinitionModel; disabled: boolean }>) {
  const serviceNames = computed(() => props.model.runtime?.definition.kind === 'container'
    ? props.model.runtime.definition.services.map(service => service.name) : [])
  const requirements = [
    { kind: 'Connection', label: 'administration.capability.checkConnection' },
    { kind: 'Functionality', label: 'administration.capability.checkFunctionality' },
    { kind: 'Vulnerability', label: 'administration.capability.checkVulnerability' },
    { kind: 'FlagWrite', label: 'administration.capability.checkFlagWrite' },
    { kind: 'CurrentFlagRead', label: 'administration.capability.checkCurrentFlag' },
    { kind: 'HistoricalFlagRead', label: 'administration.capability.checkHistoricalFlag' },
  ] as const
  function setRequirement(kind: DefinitionModel['checkRequirements'][number], enabled: boolean) {
    if (props.disabled) return
    props.model.checkRequirements = enabled ? [...new Set([...props.model.checkRequirements, kind])]
      : props.model.checkRequirements.filter(item => item !== kind)
  }
  function setReadyTimeout(value: number | null) { if (!props.disabled) props.model.readyTimeoutSeconds = value }
  return { ...toRefs(props), serviceNames, requirements, setRequirement, setReadyTimeout, RunnerJobEditor: markRaw(RunnerJobEditorComponent) }
}
export type DefinitionCheckerSectionViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useDefinitionCheckerSection>>
