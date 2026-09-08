import { proxyRefs } from 'vue'
import { toRefs } from 'vue'

import type { ContainerDefinitionModel, GameModeValue } from '../../utils/game-config'
import { FlagSource } from '../../utils/game-config'

/** Owns state, effects and commands for DefinitionContainer. */
export function useDefinitionContainer(props: Readonly<Omit<{
  definition: ContainerDefinitionModel
  mode: GameModeValue
  /** 运行时的 Flag 来源;静态 Flag 时不能配置注入环境变量。 */
  flagSource?: number
  disabled?: boolean
}, "flagSource" | "disabled"> & Required<Pick<{
  definition: ContainerDefinitionModel
  mode: GameModeValue
  /** 运行时的 Flag 来源;静态 Flag 时不能配置注入环境变量。 */
  flagSource?: number
  disabled?: boolean
}, "flagSource" | "disabled">>>) {
  const isAwdp = computed(() => props.mode === 'Awdp')

  const flagEnvDisabled = computed(() => props.disabled || props.flagSource === FlagSource.Static)

  const hasMetadata = computed(() =>
    Object.keys(props.definition.environment).length > 0 || Object.keys(props.definition.labels).length > 0,
  )

  const hasSecurity = computed(() => {
    const security = props.definition.security
    return security.noNewPrivileges || security.readonlyRootfs || security.runAsNonRoot
      || security.capDrop.length > 0 || security.capAdd.length > 0
  })

  const viewBindings = {
      ...toRefs(props),
      FlagSource,
      isAwdp,
      flagEnvDisabled,
      hasMetadata,
      hasSecurity
    }
  const viewState = proxyRefs(viewBindings)

  function onUpdateModelValueDefinitionCommand(value: NonNullable<typeof viewState.definition>['command']) {
    viewState.definition!.command = value
  }

  function onUpdateModelValueDefinitionContainerPorts(value: NonNullable<typeof viewState.definition>['containerPorts']) {
    viewState.definition!.containerPorts = value
  }

  function onUpdateModelValueDefinitionInternalPorts(value: NonNullable<typeof viewState.definition>['internalPorts']) {
    viewState.definition!.internalPorts = value
  }

  function onUpdateModelValueDefinitionEnvironment(value: NonNullable<typeof viewState.definition>['environment']) {
    viewState.definition!.environment = value
  }

  function onUpdateModelValueDefinitionLabels(value: NonNullable<typeof viewState.definition>['labels']) {
    viewState.definition!.labels = value
  }

  function onUpdateModelValueDefinitionSecurityCapDrop(value: NonNullable<NonNullable<typeof viewState.definition>['security']>['capDrop']) {
    viewState.definition!.security!.capDrop = value
  }

  function onUpdateModelValueDefinitionSecurityCapAdd(value: NonNullable<NonNullable<typeof viewState.definition>['security']>['capAdd']) {
    viewState.definition!.security!.capAdd = value
  }

  return { ...viewBindings, onUpdateModelValueDefinitionCommand, onUpdateModelValueDefinitionContainerPorts, onUpdateModelValueDefinitionInternalPorts, onUpdateModelValueDefinitionEnvironment, onUpdateModelValueDefinitionLabels, onUpdateModelValueDefinitionSecurityCapDrop, onUpdateModelValueDefinitionSecurityCapAdd }
}

export type DefinitionContainerViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useDefinitionContainer>>>
