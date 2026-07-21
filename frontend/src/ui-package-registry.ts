import { defineAsyncComponent, type Component } from 'vue'
import type { UiPackageId } from '@/themes/theme-package'

export interface UiPackageDefinition {
  /** Matches ThemePackage.uiPackage. */
  id: UiPackageId
  /** Route names this package can render. */
  routes: ReadonlySet<string>
  /** Lazy application root component for the package. */
  application: () => Promise<unknown>
}

// V1 renders the full route table; it is also the fallback for any route a
// newer package does not cover yet.
const v1RouteNames: ReadonlySet<string> = new Set([
  'home',
  'login',
  'register',
  'verify-email',
  'competitions',
  'competition-detail',
  'competition-register',
  'teams',
  'awdp-screen',
  'awd-dashboard',
  'koh-dashboard',
  'penetration-dashboard',
  'admin-users',
  'admin-teams',
  'admin-competitions',
  'admin-competition-detail',
  'admin-competition-challenge-create',
  'admin-competition-challenge-edit',
  'admin-competition-operations',
  'admin-collaborators',
  'admin-challenges',
  'admin-challenge-create',
  'admin-containers',
  'admin-plugins',
  'admin-theme-packs',
  'admin-infrastructure',
  'admin-qqbot',
  'admin-email-verification',
  'admin-audit-logs',
  'admin-health',
  'admin-logs',
  'not-found',
])

const v2RouteNames: ReadonlySet<string> = new Set([
  'home',
  'login',
  'register',
  'verify-email',
  'competitions',
  'competition-detail',
  'competition-register',
  'teams',
  'awdp-screen',
  'awd-dashboard',
  'koh-dashboard',
  'penetration-dashboard',
  'admin-theme-packs',
  'admin-users',
  'admin-teams',
  'admin-competitions',
  'admin-competition-detail',
  'admin-competition-operations',
  'admin-collaborators',
  'admin-challenges',
  'admin-challenge-create',
  'admin-containers',
  'admin-plugins',
  'admin-infrastructure',
  'admin-qqbot',
  'admin-email-verification',
  'admin-audit-logs',
  'admin-health',
  'admin-logs',
  'not-found',
])

export const uiPackages: readonly UiPackageDefinition[] = [
  {
    id: 'v1',
    routes: v1RouteNames,
    application: () => import('@/ui-v1/V1Application.vue'),
  },
  {
    id: 'v2',
    routes: v2RouteNames,
    application: () => import('@/ui-v2/V2Application.vue'),
  },
]

export const fallbackUiPackageId: UiPackageId = 'v1'

// Resolve which package renders the given route: the active package wins when
// it covers the route, otherwise the fallback package takes over so no route
// ever renders blank.
export function resolveUiPackage(packageId: string, routeName: string): UiPackageDefinition {
  const active = uiPackages.find(pkg => pkg.id === packageId)
  if (active && active.routes.has(routeName))
    return active
  return uiPackages.find(pkg => pkg.id === fallbackUiPackageId) ?? uiPackages[0]
}

const componentCache = new Map<string, Component>()

// Async components must be cached per package — creating them inside a
// computed would remount the whole tree on every route change.
export function getUiPackageComponent(pkg: UiPackageDefinition): Component {
  let component = componentCache.get(pkg.id)
  if (!component) {
    component = defineAsyncComponent(pkg.application as () => Promise<Component>)
    componentCache.set(pkg.id, component)
  }
  return component
}
