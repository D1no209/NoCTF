import { computed, markRaw, toRefs, watch } from 'vue'
import type { DefinitionModel, GameModeValue, RuntimeTemplateModel } from '../../utils/game-config'
import { bytesToMib, coresToMillicores, CtfInteraction, emptyContainerDefinition, emptyOvaDefinition, FlagSource, mibToBytes, cpuMillicoresToCores, RuntimeAllocation, UrlExposure } from '../../utils/game-config'
import DefinitionContainerComponent from './DefinitionContainer.vue'
import UrlBindingListComponent from './UrlBindingList.vue'

export function useDefinitionRuntime(props: Readonly<{
  runtime: RuntimeTemplateModel; model: DefinitionModel; mode: GameModeValue; interactionKind: number; disabled: boolean
}>) {
  const isPatchVerification = computed(() => props.mode === 'Ctf' && props.interactionKind === CtfInteraction.PatchVerification)
  const singleServiceOnly = computed(() => props.mode === 'Awdp' || isPatchVerification.value)
  const hasServices = computed(() => props.runtime.definition.kind === 'container')
  const serviceNames = computed(() => props.runtime.definition.kind === 'container' ? props.runtime.definition.services.map(service => service.name) : [])
  const showDynamicFlagInjection = computed(() => props.mode === 'Ctf' && !isPatchVerification.value)
  const dynamicFlagInjection = computed(() => props.runtime.flagSource === FlagSource.PerTeam)
  const kindOptions = computed(() => singleServiceOnly.value
    ? [{ value: 'container', label: translate('ui.runtimeServices') }]
    : [{ value: 'container', label: translate('ui.runtimeServices') }, { value: 'ova', label: translate('ui.virtualMachine') }])
  const exposureOptions = computed(() => props.mode === 'Ctf' || props.mode === 'Awdp'
    ? [{ value: UrlExposure.OwnerOnly, label: 'ui.onlyVisibleToTheTeamItself' }]
    : [{ value: UrlExposure.OwnerOnly, label: 'ui.onlyVisibleToTheTeamItself' }, { value: UrlExposure.Participants, label: 'ui.visibleToAllContestants' }])
  const flagSourceOptions = computed(() => [
    { value: FlagSource.Static, label: translate('ui.staticFlagTemplatePreset') },
    { value: FlagSource.PerTeam, label: translate('ui.independentFlagForEachTeam') },
    { value: FlagSource.AwdRotation, label: translate('ui.alternateByRoundAwd') },
  ])
  function synchronizeFlagInjection(): void {
    if (props.runtime.definition.kind !== 'container') return
    if (props.runtime.flagSource !== FlagSource.PerTeam)
      for (const service of props.runtime.definition.services) service.flagEnvironmentVariableName = ''
    else if (!props.runtime.definition.services.some(service => service.flagEnvironmentVariableName.trim()))
      props.runtime.definition.services[0]!.flagEnvironmentVariableName = 'FLAG'
  }
  function switchKind(kind: string): void {
    if (kind === props.runtime.definition.kind) return
    props.runtime.definition = kind === 'ova' ? emptyOvaDefinition() : emptyContainerDefinition()
    synchronizeFlagInjection()
  }
  function setDynamicFlagInjection(enabled: boolean): void {
    props.runtime.flagSource = enabled ? FlagSource.PerTeam : FlagSource.Static
    synchronizeFlagInjection()
  }
  const controlBindingList = computed({
    get: () => props.runtime.controlCheckUrlBinding ? [props.runtime.controlCheckUrlBinding] : [],
    set: (list) => { props.runtime.controlCheckUrlBinding = list[0] ?? null },
  })
  watch([() => props.mode, () => props.interactionKind], ([mode]) => {
    props.runtime.allocation = mode === 'Koh' ? RuntimeAllocation.Shared : RuntimeAllocation.PerTeam
    if (mode !== 'Koh') props.runtime.controlCheckUrlBinding = null
    if (mode === 'Awd') props.runtime.flagSource = FlagSource.AwdRotation
    if (mode === 'Awdp') props.runtime.flagSource = FlagSource.PerTeam
    if (isPatchVerification.value) props.runtime.flagSource = FlagSource.Static
    synchronizeFlagInjection()
  }, { immediate: true })
  function onUpdateModelValueRuntimeTtlSeconds(value: number | null): void { props.runtime.ttlSeconds = value }
  function onUpdateModelValueRuntimeOperationTimeoutSeconds(value: number | null): void { props.runtime.operationTimeoutSeconds = value }
  function onUpdateModelValueRuntimeFlagSource(value: unknown): void { props.runtime.flagSource = Number(value); synchronizeFlagInjection() }
  function onUpdateModelValueRuntimeUrlBindings(value: RuntimeTemplateModel['urlBindings']): void { props.runtime.urlBindings = value }
  function onUpdateModelValueRuntimeLimitsMemoryBytes(value: number | null): void { props.runtime.limits.memoryBytes = mibToBytes(value) }
  function onUpdateModelValueRuntimeLimitsCpuMillicores(value: number | null): void { props.runtime.limits.cpuMillicores = coresToMillicores(value) }
  function onUpdateModelValueRuntimeLimitsPidsLimit(value: number | null): void { props.runtime.limits.pidsLimit = value }
  return { ...toRefs(props), RuntimeAllocation, UrlExposure, singleServiceOnly, hasServices, serviceNames, showDynamicFlagInjection,
    dynamicFlagInjection, kindOptions, switchKind, flagSourceOptions, exposureOptions, controlBindingList,
    bytesToMib, cpuMillicoresToCores, setDynamicFlagInjection,
    DefinitionContainer: markRaw(DefinitionContainerComponent), UrlBindingList: markRaw(UrlBindingListComponent),
    onUpdateModelValueRuntimeTtlSeconds, onUpdateModelValueRuntimeOperationTimeoutSeconds, onUpdateModelValueRuntimeFlagSource,
    onUpdateModelValueRuntimeUrlBindings, onUpdateModelValueRuntimeLimitsMemoryBytes, onUpdateModelValueRuntimeLimitsCpuMillicores, onUpdateModelValueRuntimeLimitsPidsLimit }
}
export type DefinitionRuntimeViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useDefinitionRuntime>>
