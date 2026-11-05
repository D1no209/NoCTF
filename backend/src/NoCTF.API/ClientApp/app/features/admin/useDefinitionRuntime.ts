import { computed, markRaw, toRefs } from 'vue'
import type { DefinitionModel, RuntimeTemplateModel, UrlBindingModel } from '../../utils/game-config'
import { bytesToMib, coresToMillicores, emptyContainerDefinition, emptyOvaDefinition, mibToBytes, cpuMillicoresToCores, protocolInput, runtimeEgressPolicyProtocols } from '../../utils/game-config'
import DefinitionContainerComponent from './DefinitionContainer.vue'
import UrlBindingListComponent from './UrlBindingList.vue'

export function useDefinitionRuntime(props: Readonly<{
  runtime: RuntimeTemplateModel; model: DefinitionModel; disabled: boolean
}>) {
  const requiresContainer = computed(() => props.model.capabilities.includes('Fix') || props.model.capabilities.includes('Check'))
  const hasServices = computed(() => props.runtime.definition.kind === 'container')
  const serviceNames = computed(() => props.runtime.definition.kind === 'container' ? props.runtime.definition.services.map(service => service.name) : [])
  const kindOptions = computed(() => requiresContainer.value
    ? [{ value: 'container', label: translate('runtime.label.runtimeServices') }]
    : [{ value: 'container', label: translate('runtime.label.runtimeServices') }, { value: 'ova', label: translate('runtime.label.virtualMachine') }])
  const exposureOptions = computed<{ value: UrlBindingModel['exposure'], label: string }[]>(() =>
    [{ value: 'OwnerOnly', label: 'runtime.urlBinding.description.visibleTeamItself' }, { value: 'Participants', label: 'runtime.label.visibleContestants' }])
  const egressPolicyOptions = computed<{ value: RuntimeTemplateModel['egressPolicy'], label: string }[]>(() => [
    { value: 'Isolated', label: translate('runtime.label.egressPolicy.isolated') },
    { value: 'InternetOnly', label: translate('runtime.label.egressPolicy.internetOnly') },
  ])
  function switchKind(kind: string): void {
    if (props.disabled || kind === props.runtime.definition.kind || kind === 'ova' && requiresContainer.value) return
    props.runtime.definition = kind === 'ova' ? emptyOvaDefinition() : emptyContainerDefinition()
  }
  function onUpdateModelValueRuntimeOperationTimeoutSeconds(value: number | null): void { props.runtime.operationTimeoutSeconds = value }
  function onUpdateModelValueRuntimeEgressPolicy(value: unknown): void {
    props.runtime.egressPolicy = protocolInput(value, runtimeEgressPolicyProtocols, props.runtime.egressPolicy)
  }
  function onUpdateModelValueRuntimeUrlBindings(value: RuntimeTemplateModel['urlBindings']): void { props.runtime.urlBindings = value }
  function onUpdateModelValueRuntimeLimitsMemoryBytes(value: number | null): void { props.runtime.limits.memoryBytes = mibToBytes(value) }
  function onUpdateModelValueRuntimeLimitsCpuMillicores(value: number | null): void { props.runtime.limits.cpuMillicores = coresToMillicores(value) }
  function onUpdateModelValueRuntimeLimitsPidsLimit(value: number | null): void { props.runtime.limits.pidsLimit = value }
  return { ...toRefs(props), hasServices, serviceNames,
    kindOptions, switchKind, egressPolicyOptions, exposureOptions,
    bytesToMib, cpuMillicoresToCores, onUpdateModelValueRuntimeEgressPolicy,
    DefinitionContainer: markRaw(DefinitionContainerComponent), UrlBindingList: markRaw(UrlBindingListComponent),
    onUpdateModelValueRuntimeOperationTimeoutSeconds,
    onUpdateModelValueRuntimeUrlBindings, onUpdateModelValueRuntimeLimitsMemoryBytes, onUpdateModelValueRuntimeLimitsCpuMillicores, onUpdateModelValueRuntimeLimitsPidsLimit }
}
export type DefinitionRuntimeViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useDefinitionRuntime>>
