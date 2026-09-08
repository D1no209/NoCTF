type CollectionValue<T> = T extends readonly (infer Item)[] ? Item : T extends Record<string, infer Item> ? Item : never
import { proxyRefs } from 'vue'
import { toRefs } from 'vue'

import { Plus, X } from '@lucide/vue'
import type { ComposeDefinitionModel } from '../../utils/game-config'
import { bytesToMib, coresToNanoCpus, mibToBytes, nanoCpusToCores } from '../../utils/game-config'

/** Owns state, effects and commands for DefinitionCompose. */
export function useDefinitionCompose(props: Readonly<Omit<{
  definition: ComposeDefinitionModel
  disabled?: boolean
}, "disabled"> & Required<Pick<{
  definition: ComposeDefinitionModel
  disabled?: boolean
}, "disabled">>>) {
  function addService(definition: ComposeDefinitionModel): void {
    definition.serviceResources.push({ service: '', memoryBytes: null, nanoCpus: null, pidsLimit: null })
  }

  const hasMetadata = computed(() =>
    Object.keys(props.definition.environment).length > 0
    || Object.keys(props.definition.labels).length > 0
    || Object.keys(props.definition.flagEnvironmentVariables).length > 0,
  )

  const viewBindings = {
      ...toRefs(props),
      Plus,
      X,
      bytesToMib,
      coresToNanoCpus,
      mibToBytes,
      nanoCpusToCores,
      addService,
      hasMetadata
    }
  const viewState = proxyRefs(viewBindings)

  function onUpdateModelValueResourceMemoryBytes(resource: CollectionValue<NonNullable<typeof viewState.definition>['serviceResources']>, value: Parameters<typeof mibToBytes>[0]) {
    resource.memoryBytes = mibToBytes(value)
  }

  function onUpdateModelValueResourceNanoCpus(resource: CollectionValue<NonNullable<typeof viewState.definition>['serviceResources']>, value: Parameters<typeof coresToNanoCpus>[0]) {
    resource.nanoCpus = coresToNanoCpus(value)
  }

  function onUpdateModelValueResourcePidsLimit(resource: CollectionValue<NonNullable<typeof viewState.definition>['serviceResources']>, value: NonNullable<CollectionValue<NonNullable<typeof viewState.definition>['serviceResources']>>['pidsLimit']) {
    resource.pidsLimit = value
  }

  function onUpdateModelValueDefinitionEnvironment(value: NonNullable<typeof viewState.definition>['environment']) {
    viewState.definition!.environment = value
  }

  function onUpdateModelValueDefinitionLabels(value: NonNullable<typeof viewState.definition>['labels']) {
    viewState.definition!.labels = value
  }

  function onUpdateModelValueDefinitionFlagEnvironmentVariables(value: NonNullable<typeof viewState.definition>['flagEnvironmentVariables']) {
    viewState.definition!.flagEnvironmentVariables = value
  }

  return { ...viewBindings, onUpdateModelValueResourceMemoryBytes, onUpdateModelValueResourceNanoCpus, onUpdateModelValueResourcePidsLimit, onUpdateModelValueDefinitionEnvironment, onUpdateModelValueDefinitionLabels, onUpdateModelValueDefinitionFlagEnvironmentVariables }
}

export type DefinitionComposeViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useDefinitionCompose>>>
