import { toRefs } from 'vue'

import type { WorkspaceNavGroup, WorkspaceNavItem } from './workspace-nav'
import { useSidebar } from '../../components/ui/sidebar'

/** Owns state, effects and commands for WorkspaceNavMenu. */
export function useWorkspaceNavMenu(props: Readonly<{
  groups: WorkspaceNavGroup[]
}>) {
  const route = useRoute()

  const { isMobile, setOpenMobile } = useSidebar()

  function isActive(item: WorkspaceNavItem) {
    return item.exact
      ? route.path === item.to
      : route.path === item.to || route.path.startsWith(`${item.to}/`)
  }

  function onNavigate() {
    if (isMobile.value) setOpenMobile(false)
  }

  return {
      ...toRefs(props),
      isActive,
      onNavigate
    }
}

export type WorkspaceNavMenuViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useWorkspaceNavMenu>>>
