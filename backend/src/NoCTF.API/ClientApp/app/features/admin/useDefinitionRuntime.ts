import { computed, markRaw, toRefs, watch } from 'vue'
import type { DefinitionModel, GameModeValue, RuntimeTemplateModel, UrlBindingModel } from '../../utils/game-config'
import { bytesToMib, coresToMillicores, CtfInteraction, emptyContainerDefinition, emptyOvaDefinition, mibToBytes, cpuMillicoresToCores, protocolValue, runtimeEgressPolicyProtocols, runtimeFlagSourceProtocols } from '../../utils/game-config'
import DefinitionContainerComponent from './DefinitionContainer.vue'
import UrlBindingListComponent from './UrlBindingList.vue'

export function useDefinitionRuntime(props: Readonly<{
  runtime: RuntimeTemplateModel; model: DefinitionModel; mode: GameModeValue; interactionKind: number; disabled: boolean
}>) {
  const isPatchVerification = computed(() => props.mode === 'Ctf' && props.interactionKind === CtfInteraction.PatchVerification)
  const singleServiceOnly = computed(() => props.mode === 'Awdp' || isPatchVerification.value)
  const hasServices = computed(() => props.runtime.definition.kind === 'container')
  const serviceNames = computed(() => props.runtime.definition.kind === 'container' ? props.runtime.definition.services.map(service => service.name) : [])
  const showDynamicFlagInjection = computed(() => (props.mode === 'Ctf' || props.mode === 'LiveSolo') && !isPatchVerification.value)
  const dynamicFlagInjection = computed(() => props.runtime.flagSource === 'PerTeam')
  const kindOptions = computed(() => singleServiceOnly.value || props.mode === 'LiveSolo'
    ? [{ value: 'container', label: translate('runtime.label.runtimeServices') }]
    : [{ value: 'container', label: translate('runtime.label.runtimeServices') }, { value: 'ova', label: translate('runtime.label.virtualMachine') }])
  const exposureOptions = computed<{ value: UrlBindingModel['exposure'], label: string }[]>(() => props.mode === 'Ctf' || props.mode === 'Awdp' || props.mode === 'LiveSolo'
    ? [{ value: 'OwnerOnly', label: 'runtime.urlBinding.description.visibleTeamItself' }]
    : [{ value: 'OwnerOnly', label: 'runtime.urlBinding.description.visibleTeamItself' }, { value: 'Participants', label: 'runtime.label.visibleContestants' }])
  /** 控制检查入口固定为平台可见:单一选项,界面锁定。 */
  const controlExposureOptions = computed<{ value: UrlBindingModel['exposure'], label: string }[]>(() => [
    { value: 'Participants', label: 'runtime.label.platformCheckUsage' },
  ])
  const egressPolicyOptions = computed<{ value: RuntimeTemplateModel['egressPolicy'], label: string }[]>(() => [
    { value: 'Isolated', label: translate('runtime.label.egressPolicy.isolated') },
    { value: 'InternetOnly', label: translate('runtime.label.egressPolicy.internetOnly') },
  ])
  const flagSourceOptions = computed<{ value: RuntimeTemplateModel['flagSource'], label: string }[]>(() => [
    { value: 'Static', label: translate('runtime.label.staticFlagTemplatePreset') },
    { value: 'PerTeam', label: translate('runtime.definitionRuntime.label.independentFlagTeam') },
    { value: 'AwdRotation', label: translate('runtime.label.alternateRoundAwd') },
  ])
  function synchronizeFlagInjection(): void {
    if (props.runtime.definition.kind !== 'container') return
    if (props.runtime.flagSource !== 'PerTeam')
      for (const service of props.runtime.definition.services) service.flagEnvironmentVariableName = ''
    else if (!props.runtime.definition.services.some(service => service.flagEnvironmentVariableName.trim()))
      props.runtime.definition.services[0]!.flagEnvironmentVariableName = 'FLAG'
  }
  function switchKind(kind: string): void {
    if (kind === props.runtime.definition.kind || kind === 'ova' && props.mode === 'LiveSolo') return
    props.runtime.definition = kind === 'ova' ? emptyOvaDefinition() : emptyContainerDefinition()
    synchronizeFlagInjection()
  }
  function setDynamicFlagInjection(enabled: boolean): void {
    props.runtime.flagSource = enabled ? 'PerTeam' : 'Static'
    synchronizeFlagInjection()
  }
  const controlBindingList = computed({
    get: () => props.runtime.controlCheckUrlBinding ? [props.runtime.controlCheckUrlBinding] : [],
    set: (list) => { props.runtime.controlCheckUrlBinding = list[0] ?? null },
  })
  watch([() => props.mode, () => props.interactionKind], ([mode]) => {
    props.runtime.allocation = mode === 'Koh' ? 'Shared' : 'PerTeam'
    if (mode !== 'Koh') props.runtime.controlCheckUrlBinding = null
    if (mode === 'Awd') props.runtime.flagSource = 'AwdRotation'
    if (mode === 'Awdp') props.runtime.flagSource = 'PerTeam'
    if (isPatchVerification.value) props.runtime.flagSource = 'Static'
    synchronizeFlagInjection()
  }, { immediate: true })
  function onUpdateModelValueRuntimeTtlSeconds(value: number | null): void { props.runtime.ttlSeconds = value }
  function onUpdateModelValueRuntimeOperationTimeoutSeconds(value: number | null): void { props.runtime.operationTimeoutSeconds = value }
  function onUpdateModelValueRuntimeFlagSource(value: unknown): void {
    props.runtime.flagSource = protocolValue(value, runtimeFlagSourceProtocols, 'Static', 'runtime flag source')
    synchronizeFlagInjection()
  }
  function onUpdateModelValueRuntimeEgressPolicy(value: unknown): void {
    props.runtime.egressPolicy = protocolValue(value, runtimeEgressPolicyProtocols, 'Isolated', 'runtime egress policy')
  }
  function onUpdateModelValueRuntimeUrlBindings(value: RuntimeTemplateModel['urlBindings']): void { props.runtime.urlBindings = value }
  function onUpdateModelValueRuntimeLimitsMemoryBytes(value: number | null): void { props.runtime.limits.memoryBytes = mibToBytes(value) }
  function onUpdateModelValueRuntimeLimitsCpuMillicores(value: number | null): void { props.runtime.limits.cpuMillicores = coresToMillicores(value) }
  function onUpdateModelValueRuntimeLimitsPidsLimit(value: number | null): void { props.runtime.limits.pidsLimit = value }
  return { ...toRefs(props), singleServiceOnly, hasServices, serviceNames, showDynamicFlagInjection,
    dynamicFlagInjection, kindOptions, switchKind, flagSourceOptions, egressPolicyOptions, exposureOptions, controlExposureOptions, controlBindingList,
    bytesToMib, cpuMillicoresToCores, setDynamicFlagInjection, onUpdateModelValueRuntimeEgressPolicy,
    DefinitionContainer: markRaw(DefinitionContainerComponent), UrlBindingList: markRaw(UrlBindingListComponent),
    onUpdateModelValueRuntimeTtlSeconds, onUpdateModelValueRuntimeOperationTimeoutSeconds, onUpdateModelValueRuntimeFlagSource,
    onUpdateModelValueRuntimeUrlBindings, onUpdateModelValueRuntimeLimitsMemoryBytes, onUpdateModelValueRuntimeLimitsCpuMillicores, onUpdateModelValueRuntimeLimitsPidsLimit }
}
export type DefinitionRuntimeViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useDefinitionRuntime>>
