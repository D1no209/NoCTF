import { markRaw, toRefs } from 'vue'

import type { WorkspaceNavGroup } from './workspace-nav'
import WorkspaceNavMenuComponent from './WorkspaceNavMenu.vue'

/** Owns state, effects and commands for AppWorkspaceNav. */
export function useAppWorkspaceNav(props: Readonly<{
  groups: WorkspaceNavGroup[]
  title?: string
}>) {
  const WorkspaceNavMenu = markRaw(WorkspaceNavMenuComponent)

  return {
      ...toRefs(props),
      WorkspaceNavMenu
    }
}

export type AppWorkspaceNavViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAppWorkspaceNav>>>
