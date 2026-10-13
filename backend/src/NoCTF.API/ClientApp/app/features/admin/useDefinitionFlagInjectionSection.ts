import { computed, toRefs } from 'vue'
import type { DefinitionModel } from '../../utils/game-config'
import { createAwdFlagStore, AWD_HISTORY_PRESERVING_COMMAND } from '../../utils/awd-flag-stores'

/** Owns the AWD template's stable Flag stores and their delivery contract. */
export function useDefinitionFlagInjectionSection(props: Readonly<{ model: DefinitionModel; disabled: boolean }>) {
  const serviceNames = computed(() => props.model.runtime?.definition.kind === 'container'
    ? props.model.runtime.definition.services.map(service => service.name) : [])
  const canAddStore = computed(() => !props.disabled && props.model.awdFlagStores.length < 64)
  function addStore() {
    if (!canAddStore.value) return
    const names = new Set(props.model.awdFlagStores.map(store => store.name))
    let name = 'main'
    for (let n = 2; names.has(name); n++) name = `store-${n}`
    const store = createAwdFlagStore(name, serviceNames.value[0] ?? null)
    if (props.model.awdFlagStores.length === 0 && props.model.flagInjection) {
      Object.assign(store, { injectionCommand: props.model.flagInjection.command,
        injectionTimeoutSeconds: props.model.flagInjection.timeoutSeconds ?? 30,
        targetServiceName: props.model.flagInjection.serviceName || store.targetServiceName })
    }
    props.model.awdFlagStores.push(store)
    props.model.flagInjection = null
  }
  function removeStore(index: number) {
    if (props.disabled || props.model.awdFlagStores.length <= 1) return
    props.model.awdFlagStores.splice(index, 1)
  }
  function setDeliveryMode(index: number, mode: unknown) {
    const store = props.model.awdFlagStores[index]
    if (props.disabled || !store || (mode !== 'RunnerCommand' && mode !== 'BusinessChecker')) return
    store.deliveryMode = mode
    store.injectionCommand = mode === 'BusinessChecker' ? null : AWD_HISTORY_PRESERVING_COMMAND
  }
  function setTimeoutSeconds(index: number, value: number | null) {
    const store = props.model.awdFlagStores[index]
    if (!props.disabled && store) store.injectionTimeoutSeconds = value ?? 30
  }
  function setCommand(index: number, value: string | number) {
    const store = props.model.awdFlagStores[index]
    if (!props.disabled && store) store.injectionCommand = String(value)
  }
  function useFileTemplate(index: number) {
    const store = props.model.awdFlagStores[index]
    if (!props.disabled && store) {
      store.injectionCommand = AWD_HISTORY_PRESERVING_COMMAND
      store.preservesHistoricalFlags = true
    }
  }
  return { ...toRefs(props), serviceNames, canAddStore, addStore, removeStore, setDeliveryMode, setTimeoutSeconds, setCommand, useFileTemplate }
}

export type DefinitionFlagInjectionSectionViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useDefinitionFlagInjectionSection>>
