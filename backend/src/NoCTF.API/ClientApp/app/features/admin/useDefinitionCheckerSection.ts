import { markRaw, toRefs } from 'vue'

import type { DefinitionModel, GameModeValue } from '../../utils/game-config'
import { emptyRunnerJob } from '../../utils/game-config'
import RunnerJobEditorComponent from './RunnerJobEditor.vue'

/** Owns state, effects and commands for DefinitionCheckerSection. */
export function useDefinitionCheckerSection(props: Readonly<Omit<{
  model: DefinitionModel
  mode: GameModeValue
  disabled?: boolean
}, "disabled"> & Required<Pick<{
  model: DefinitionModel
  mode: GameModeValue
  disabled?: boolean
}, "disabled">>>) {
  const hasServices = computed(() => props.model.runtime?.definition.kind === 'container')

  function toggleChecker(enabled: boolean): void {
    props.model.checker = enabled ? { job: emptyRunnerJob(), targetServiceName: '' } : null
  }

  function toggleCheckerJob(enabled: boolean): void {
    props.model.checkerJob = enabled ? emptyRunnerJob() : null
    if (!enabled) props.model.checkerFixInput = false
  }

  const RunnerJobEditor = markRaw(RunnerJobEditorComponent)

  return {
      ...toRefs(props),
      hasServices,
      serviceNames: computed(() => props.model.runtime?.definition.kind === 'container' ? props.model.runtime.definition.services.map(service => service.name) : []),
      toggleChecker,
      toggleCheckerJob,
      RunnerJobEditor
    }
}

export type DefinitionCheckerSectionViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useDefinitionCheckerSection>>>
