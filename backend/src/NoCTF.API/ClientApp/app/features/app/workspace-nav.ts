import type { Component, ComputedRef, InjectionKey } from 'vue'

export interface WorkspaceNavItem {
  to: string
  label: string
  icon: Component
  /** true 时仅精确匹配路由,否则匹配自身与子路由 */
  exact?: boolean
}

export interface WorkspaceNavGroup {
  label?: string | null
  items: WorkspaceNavItem[]
}

export const competitionWorkspaceNavigationKey: InjectionKey<ComputedRef<WorkspaceNavGroup[]>>
  = Symbol('competition-workspace-navigation')
