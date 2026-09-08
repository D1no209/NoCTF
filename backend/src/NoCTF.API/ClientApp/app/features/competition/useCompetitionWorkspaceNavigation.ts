import { toRefs } from 'vue'

import { Compass } from '@lucide/vue'
import type { WorkspaceNavGroup, WorkspaceNavItem } from '../app/workspace-nav'

/** Owns state, effects and commands for CompetitionWorkspaceNavigation. */
export function useCompetitionWorkspaceNavigation(props: Readonly<{ groups: WorkspaceNavGroup[] }>) {
  const route = useRoute()

  function isActive(item: WorkspaceNavItem): boolean {
    return item.exact
      ? route.path === item.to
      : route.path === item.to || route.path.startsWith(`${item.to}/`)
  }

  return {
      ...toRefs(props),
      Compass,
      isActive
    }
}

export type CompetitionWorkspaceNavigationViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionWorkspaceNavigation>>>
