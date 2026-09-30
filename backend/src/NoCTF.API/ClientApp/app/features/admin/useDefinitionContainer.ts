import { computed, ref, toRefs, useId, watch } from 'vue'
import type { ContainerDefinitionModel, DefinitionModel, GameModeValue, RuntimeServiceModel } from '../../utils/game-config'
import { emptyRuntimeService, FlagSource, runtimeServiceIsReferenced, renameRuntimeService } from '../../utils/game-config'
import { runtimeTopology } from './runtime-topology'

export function useDefinitionContainer(props: Readonly<{
  definition: ContainerDefinitionModel; model: DefinitionModel; mode: GameModeValue;
  flagSource: number; disabled: boolean; singleServiceOnly: boolean
}>) {
  const showFlagEnvironmentVariable = computed(() => props.flagSource === FlagSource.PerTeam)
  const selectedServiceIndex = ref(0)
  const fieldId = useId()
  const serviceNameInputId = `${fieldId}-service-name`
  const serviceImageInputId = `${fieldId}-service-image`
  const selectedService = computed(() => props.definition.services[selectedServiceIndex.value])
  const selectedServiceId = computed(() => `service-${selectedServiceIndex.value}`)
  const topology = computed(() => runtimeTopology(props.definition, props.model.runtime?.urlBindings ?? [],
    props.model.runtime?.controlCheckUrlBinding ?? null, {
      unnamedService: index => translate('ui.runtimeUnnamedService', { index }),
      missingImage: translate('ui.runtimeImageNotSet'),
      missingService: translate('ui.runtimeServiceNotFound'),
      controlEntry: translate('ui.controlCheckEntry'),
      noEntries: translate('ui.runtimeNoAccessEntries'),
    }))
  const advancedOpen = computed(() => {
    const service = selectedService.value
    return !!service && (service.memoryMiB !== null || service.cpuCores !== null
      || service.command.length > 0 || service.arguments.length > 0
      || Object.keys(service.environment).length > 0 || service.internalPorts.length > 0
      || !!service.flagEnvironmentVariableName)
  })
  function selectService(id: string): void {
    const index = props.definition.services.findIndex((_, ordinal) => `service-${ordinal}` === id)
    if (index >= 0) selectedServiceIndex.value = index
  }
  watch(() => props.definition.services.length, count => {
    selectedServiceIndex.value = Math.max(0, Math.min(selectedServiceIndex.value, count - 1))
  })
  function addService(): void {
    if (props.disabled || props.singleServiceOnly || props.definition.services.length >= 64) return
    let ordinal = 1
    while (props.definition.services.some(service => service.name === `service-${ordinal}`)) ordinal++
    props.definition.services.push(emptyRuntimeService(`service-${ordinal}`))
    selectedServiceIndex.value = props.definition.services.length - 1
  }
  function canRemove(index: number): boolean {
    const service = props.definition.services[index]
    return !!service && props.definition.services.length > 1 && !runtimeServiceIsReferenced(props.model, service.name)
  }
  function removeService(index: number): void {
    if (!props.disabled && canRemove(index)) props.definition.services.splice(index, 1)
  }
  function renameService(index: number, name: string): void {
    const service = props.definition.services[index]
    if (!props.disabled && service && !props.definition.services.some((other, ordinal) => ordinal !== index && other.name === name))
      renameRuntimeService(props.model, service.name, name)
  }
  function updateService<K extends keyof RuntimeServiceModel>(index: number, key: K, value: RuntimeServiceModel[K]): void {
    const service = props.definition.services[index]
    if (!props.disabled && service) service[key] = value
  }
  return { ...toRefs(props), showFlagEnvironmentVariable, serviceNameInputId, serviceImageInputId, selectedServiceIndex, selectedService, selectedServiceId,
    topology, advancedOpen, selectService, addService, canRemove, removeService, renameService, updateService }
}
export type DefinitionContainerViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useDefinitionContainer>>
