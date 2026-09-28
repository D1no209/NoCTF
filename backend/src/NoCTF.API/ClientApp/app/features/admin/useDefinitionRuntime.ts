import { proxyRefs } from 'vue'
import { markRaw, toRefs } from 'vue'

import type { GameModeValue, RuntimeTemplateModel } from '../../utils/game-config'
import { bytesToMib, coresToNanoCpus, CtfInteraction, DEFAULT_RUNTIME_MEMORY_BYTES, DEFAULT_RUNTIME_NANO_CPUS, DEFAULT_RUNTIME_OPERATION_TIMEOUT_SECONDS, DEFAULT_RUNTIME_PIDS_LIMIT, DEFAULT_RUNTIME_TTL_SECONDS, emptyContainerDefinition, emptyComposeDefinition, emptyOvaDefinition, FlagSource, mibToBytes, nanoCpusToCores, RuntimeAllocation, UrlExposure } from '../../utils/game-config'
import DefinitionComposeComponent from './DefinitionCompose.vue'
import DefinitionContainerComponent from './DefinitionContainer.vue'
import UrlBindingListComponent from './UrlBindingList.vue'

/** Owns state, effects and commands for DefinitionRuntime. */
export function useDefinitionRuntime(props: Readonly<Omit<{
  runtime: RuntimeTemplateModel
  mode: GameModeValue
  interactionKind: number
  disabled?: boolean
}, "disabled"> & Required<Pick<{
  runtime: RuntimeTemplateModel
  mode: GameModeValue
  interactionKind: number
  disabled?: boolean
}, "disabled">>>) {
  const isCompose = computed(() => props.runtime.definition.kind === 'compose')
  const isPatchVerification = computed(() =>
    props.mode === 'Ctf'
    && props.interactionKind === CtfInteraction.PatchVerification,
  )
  const showDynamicFlagInjection = computed(() =>
    props.mode === 'Ctf' && !isPatchVerification.value,
  )
  const dynamicFlagInjection = computed(() =>
    props.runtime.flagSource === FlagSource.PerTeam,
  )

  const kindOptions = computed(() =>
    props.mode === 'Awdp' || isPatchVerification.value
      ? [{ value: 'container', label: translate("ui.singleContainer") }]
      : [
          { value: 'container', label: translate("ui.singleContainer") },
          { value: 'compose', label: 'Docker Compose' },
          { value: 'ova', label: 'OVA VM' },
        ],
  )

  function switchKind(kind: string): void {
    if (kind === props.runtime.definition.kind) return
    props.runtime.definition = kind === 'compose'
      ? emptyComposeDefinition()
      : kind === 'ova'
        ? emptyOvaDefinition()
        : emptyContainerDefinition(
          props.mode === 'Awdp'
          || props.mode === 'Ctf' && !isPatchVerification.value,
        )
    synchronizeFlagInjection(props.runtime)
  }

  const flagSourceOptions = computed(() => {
    const all = [
      { value: FlagSource.Static, label: translate("ui.staticFlagTemplatePreset") },
      { value: FlagSource.PerTeam, label: translate("ui.independentFlagForEachTeam") },
      { value: FlagSource.AwdRotation, label: translate("ui.alternateByRoundAwd") },
    ]
    return props.mode === 'Ctf' || props.mode === 'Awdp' ? all.slice(0, 2) : all
  })

  const exposureOptions = computed(() => {
    if (props.mode === 'Ctf' || props.mode === 'Awdp') {
      return [{ value: UrlExposure.OwnerOnly, label: translate("ui.onlyVisibleToTheTeamItself") }]
    }
    return [
      { value: UrlExposure.OwnerOnly, label: translate("ui.onlyVisibleToTheTeamItself") },
      { value: UrlExposure.Participants, label: translate("ui.visibleToAllContestants") },
    ]
  })

  const controlBindingList = computed({
    get: () => (props.runtime.controlCheckUrlBinding ? [props.runtime.controlCheckUrlBinding] : []),
    set: (list) => {
      props.runtime.controlCheckUrlBinding = list[0] ?? null
    },
  })

  const hasCustomRuntimePolicy = computed(() =>
    props.runtime.limits.memoryBytes !== DEFAULT_RUNTIME_MEMORY_BYTES
    || props.runtime.limits.nanoCpus !== DEFAULT_RUNTIME_NANO_CPUS
    || props.runtime.limits.pidsLimit !== DEFAULT_RUNTIME_PIDS_LIMIT
    || props.runtime.ttlSeconds !== DEFAULT_RUNTIME_TTL_SECONDS
    || props.runtime.operationTimeoutSeconds !== DEFAULT_RUNTIME_OPERATION_TIMEOUT_SECONDS,
  )

  watch(
    [() => props.mode, () => props.interactionKind],
    ([mode, interactionKind]) => {
      const runtime = props.runtime
      runtime.allocation = mode === 'Koh' ? RuntimeAllocation.Shared : RuntimeAllocation.PerTeam
      if (mode !== 'Koh') runtime.controlCheckUrlBinding = null
      if (mode === 'Ctf') {
        const patchVerification = interactionKind === CtfInteraction.PatchVerification
        if (patchVerification && runtime.definition.kind === 'compose')
          runtime.definition = emptyContainerDefinition(false)
        if (patchVerification) {
          runtime.flagSource = FlagSource.Static
        }
        else if (runtime.flagSource !== FlagSource.Static
          && runtime.flagSource !== FlagSource.PerTeam) {
          runtime.flagSource = FlagSource.PerTeam
        }
        synchronizeFlagInjection(runtime)
        for (const binding of runtime.urlBindings) binding.exposure = UrlExposure.OwnerOnly
      }
      if (mode === 'Awdp') {
        runtime.flagSource = FlagSource.PerTeam
        if (runtime.definition.kind === 'container'
          && !runtime.definition.flagEnvironmentVariableName.trim()) {
          runtime.definition.flagEnvironmentVariableName = 'FLAG'
        }
        for (const binding of runtime.urlBindings) binding.exposure = UrlExposure.OwnerOnly
        if (runtime.definition.kind === 'compose') {
          runtime.definition = emptyContainerDefinition(true)
        }
      }
    },
    { immediate: true },
  )

  const DefinitionCompose = markRaw(DefinitionComposeComponent)

  const DefinitionContainer = markRaw(DefinitionContainerComponent)

  const UrlBindingList = markRaw(UrlBindingListComponent)

  const viewBindings = {
      ...toRefs(props),
      bytesToMib,
      coresToNanoCpus,
      mibToBytes,
      nanoCpusToCores,
      RuntimeAllocation,
      UrlExposure,
      isCompose,
      isPatchVerification,
      showDynamicFlagInjection,
      dynamicFlagInjection,
      kindOptions,
      switchKind,
      flagSourceOptions,
      exposureOptions,
      controlBindingList,
      hasCustomRuntimePolicy,
      DefinitionCompose,
      DefinitionContainer,
      UrlBindingList
    }
  const viewState = proxyRefs(viewBindings)

  function onUpdateModelValueRuntimeLimitsMemoryBytes(value: Parameters<typeof mibToBytes>[0]) {
    viewState.runtime!.limits!.memoryBytes = mibToBytes(value)
  }

  function onUpdateModelValueRuntimeLimitsNanoCpus(value: Parameters<typeof coresToNanoCpus>[0]) {
    viewState.runtime!.limits!.nanoCpus = coresToNanoCpus(value)
  }

  function onUpdateModelValueRuntimeLimitsPidsLimit(value: NonNullable<NonNullable<typeof viewState.runtime>['limits']>['pidsLimit']) {
    viewState.runtime!.limits!.pidsLimit = value
  }

  function onUpdateModelValueRuntimeTtlSeconds(value: NonNullable<typeof viewState.runtime>['ttlSeconds']) {
    viewState.runtime!.ttlSeconds = value
  }

  function onUpdateModelValueRuntimeOperationTimeoutSeconds(value: NonNullable<typeof viewState.runtime>['operationTimeoutSeconds']) {
    viewState.runtime!.operationTimeoutSeconds = value
  }

  function onUpdateModelValueRuntimeFlagSource(value: Parameters<typeof Number>[0]) {
    viewState.runtime!.flagSource = Number(value)
  }

  function setDynamicFlagInjection(enabled: boolean): void {
    props.runtime.flagSource = enabled ? FlagSource.PerTeam : FlagSource.Static
    synchronizeFlagInjection(props.runtime)
  }

  function onUpdateModelValueRuntimeUrlBindings(value: NonNullable<typeof viewState.runtime>['urlBindings']) {
    viewState.runtime!.urlBindings = value
  }

  return { ...viewBindings, onUpdateModelValueRuntimeLimitsMemoryBytes, onUpdateModelValueRuntimeLimitsNanoCpus, onUpdateModelValueRuntimeLimitsPidsLimit, onUpdateModelValueRuntimeTtlSeconds, onUpdateModelValueRuntimeOperationTimeoutSeconds, onUpdateModelValueRuntimeFlagSource, setDynamicFlagInjection, onUpdateModelValueRuntimeUrlBindings }
}

function synchronizeFlagInjection(runtime: RuntimeTemplateModel): void {
  const enabled = runtime.flagSource === FlagSource.PerTeam
  if (runtime.definition.kind === 'container') {
    runtime.definition.flagEnvironmentVariableName = enabled
      ? runtime.definition.flagEnvironmentVariableName.trim() || 'FLAG'
      : ''
    return
  }
  if (runtime.definition.kind === 'compose' && !enabled)
    runtime.definition.flagEnvironmentVariables = {}
}

export type DefinitionRuntimeViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useDefinitionRuntime>>>
