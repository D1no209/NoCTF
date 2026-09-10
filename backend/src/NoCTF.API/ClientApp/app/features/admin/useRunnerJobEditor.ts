import { proxyRefs } from 'vue'
import { toRefs } from 'vue'

import type { RunnerJobModel } from '../../utils/game-config'

/** Owns state, effects and commands for RunnerJobEditor. */
export function useRunnerJobEditor(props: Readonly<Omit<{
  job: RunnerJobModel
  disabled?: boolean
}, "disabled"> & Required<Pick<{
  job: RunnerJobModel
  disabled?: boolean
}, "disabled">>>) {

  const viewBindings = {
      ...toRefs(props),

    }
  const viewState = proxyRefs(viewBindings)

  function onUpdateModelValueJobCommand(value: NonNullable<typeof viewState.job>['command']) {
    viewState.job!.command = value
  }

  function onUpdateModelValueJobEnvironment(value: NonNullable<typeof viewState.job>['environment']) {
    viewState.job!.environment = value
  }

  function onUpdateModelValueJobTimeoutSeconds(value: NonNullable<typeof viewState.job>['timeoutSeconds']) {
    viewState.job!.timeoutSeconds = value
  }

  return { ...viewBindings, onUpdateModelValueJobCommand, onUpdateModelValueJobEnvironment, onUpdateModelValueJobTimeoutSeconds }
}

export type RunnerJobEditorViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useRunnerJobEditor>>>
