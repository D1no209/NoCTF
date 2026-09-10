import { toRefs } from 'vue'

import type { WorkspaceNavGroup, WorkspaceNavItem } from './workspace-nav'

/** Owns state, effects and commands for AppWorkspaceNav. */
export function useAppWorkspaceNav(props: Readonly<{
  groups: WorkspaceNavGroup[]
  title?: string
}>) {
  const route = useRoute()

  function isActive(item: WorkspaceNavItem) {
    return item.exact
      ? route.path === item.to
      : route.path === item.to || route.path.startsWith(`${item.to}/`)
  }

  const groupOptions = computed(() => props.groups.map((group, index) => ({
    value: `workspace-group-${index}`,
    label: group.label ?? props.title ?? '',
    items: group.items.map(item => ({ value: item.to, label: item.label, item })),
  })))

  const options = computed(() => groupOptions.value.flatMap(group => group.items))

  const selectedPath = computed(() => options.value
    .filter(option => isActive(option.item))
    .sort((left, right) => right.value.length - left.value.length)[0]?.value ?? null)

  function selectPath(path: string) {
    if (path !== route.path) void navigateTo(path)
  }

  return {
      ...toRefs(props),
      groupOptions,
      options,
      selectedPath,
      selectPath
    }
}

export type AppWorkspaceNavViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAppWorkspaceNav>>>
